using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ManifestDownloaderGUI.Services;
using ManifestDownloaderGUI.Windows;

namespace ManifestDownloaderGUI
{
    public partial class ManifestLibraryTab : UserControl
    {
        private Process? _downloadProcess;
        private bool _stopRequested;
        private string? _exePath;
        private bool _isRefreshing = false;
        private bool _isLoadingPatches = false;
        private bool _isLoadingManifests = false;
        private SelectionStateService _libraryStateService = null!;
        private bool _hasRestoredState = false;

        private class ManifestFileInfo
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
        }

        public ManifestLibraryTab()
        {
            try
            {
                InitializeComponent();
                FindManifestDownloaderExe();
                _libraryStateService = new SelectionStateService(App.AppDataPath, "library_selection_state.json");
                // Don't call RefreshManifests here - let MainWindow handle it when tab is selected
                
                // Ensure process is killed when app closes
                Application.Current.Exit += (s, e) => StopDownload();
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(Window.GetWindow(this), "Initialization Error",
                    $"Error initializing ManifestLibraryTab: {ex.Message}\n\n{ex.StackTrace}");
            }
        }

        private void FindManifestDownloaderExe()
        {
            // Store the exe in AppData/ManifestDownloaderGUI/Tools/
            var toolsDir = Path.Combine(App.AppDataPath, "Tools");
            
            // Create the Tools directory if it doesn't exist
            if (!Directory.Exists(toolsDir))
            {
                Directory.CreateDirectory(toolsDir);
                System.Diagnostics.Debug.WriteLine($"Created Tools directory: {toolsDir}");
            }
            
            _exePath = Path.Combine(toolsDir, "ManifestDownloader.exe");
            System.Diagnostics.Debug.WriteLine($"Looking for ManifestDownloader.exe at: {_exePath}");
            
            if (!File.Exists(_exePath))
            {
                System.Diagnostics.Debug.WriteLine($"ManifestDownloader.exe not found at: {_exePath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Found ManifestDownloader.exe at: {_exePath}");
            }
        }

        public void RefreshManifests()
        {
            System.Diagnostics.Debug.WriteLine($"\n>>>>>> RefreshManifests CALLED, _isRefreshing={_isRefreshing}, _hasRestoredState={_hasRestoredState}");
            
            // Prevent multiple simultaneous calls
            if (_isRefreshing)
            {
                System.Diagnostics.Debug.WriteLine(">>>>>> RefreshManifests EXITING: already refreshing");
                return;
            }
            
            // Set flag BEFORE BeginInvoke to prevent multiple queued calls
            _isRefreshing = true;
            
            // Ensure we're on UI thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new System.Action(() => 
                {
                    _isRefreshing = false; // Reset flag before actual execution
                    RefreshManifests();
                }));
                return;
            }

            try
            {
                if (!Directory.Exists(App.ManifestsPath) || !Directory.GetFiles(App.ManifestsPath, "*.manifest").Any())
                {
                    ServerCombo.Items.Clear();
                    PatchCombo.Items.Clear();
                    ManifestCombo.Items.Clear();
                    ServerCombo.Items.Add("No manifests found");
                    ServerCombo.IsEnabled = false;
                    PatchCombo.IsEnabled = false;

                    AllManifestsList.ItemsSource = null;
                    NoManifestsLabel.Visibility = Visibility.Visible;
                    AllManifestsList.Visibility = Visibility.Collapsed;
                    return;
                }

                var manifestFiles = Directory.GetFiles(App.ManifestsPath, "*.manifest");

                // Process files - this might take time, so we do it synchronously but quickly
                var servers = new HashSet<string>();
                var allManifests = new List<ManifestFileInfo>();
                foreach (var file in manifestFiles)
                {
                    try
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        allManifests.Add(new ManifestFileInfo { Name = fileName, FullPath = file });
                        
                        var parts = fileName.Split('_', 3);
                        if (parts.Length >= 2)
                        {
                            servers.Add(parts[0]);
                        }
                    }
                    catch
                    {
                        // Skip invalid files
                    }
                }

                // Update AllManifestsList
                AllManifestsList.ItemsSource = allManifests.OrderBy(m => m.Name).ToList();
                NoManifestsLabel.Visibility = allManifests.Any() ? Visibility.Collapsed : Visibility.Visible;
                AllManifestsList.Visibility = allManifests.Any() ? Visibility.Visible : Visibility.Collapsed;

                var sortedServers = servers.OrderBy(s => s).ToList();

                // Temporarily disable events to prevent recursion
                ServerCombo.SelectionChanged -= ServerCombo_SelectionChanged;
                PatchCombo.SelectionChanged -= PatchCombo_SelectionChanged;
                
                // Clear all combos
                ServerCombo.Items.Clear();
                PatchCombo.Items.Clear();
                ManifestCombo.Items.Clear();
                
                if (sortedServers.Any())
                {
                    foreach (var server in sortedServers)
                    {
                        ServerCombo.Items.Add(server);
                    }
                    ServerCombo.IsEnabled = true;
                }
                else
                {
                    ServerCombo.Items.Add("No servers found");
                    ServerCombo.IsEnabled = false;
                }
                
                // Re-enable event handlers
                ServerCombo.SelectionChanged += ServerCombo_SelectionChanged;
                PatchCombo.SelectionChanged += PatchCombo_SelectionChanged;

                // If no server is selected, show ALL manifests in the manifest combo
                if (ServerCombo.SelectedItem == null && manifestFiles.Any())
                {
                    PopulateManifestCombo(manifestFiles);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RefreshManifests: {ex.Message}\n{ex.StackTrace}");
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    $"Error loading manifests: {ex.Message}");
            }
            finally
            {
                _isRefreshing = false;
                
                // Restore saved state ONLY ONCE - this will trigger server selection naturally
                if (!_hasRestoredState)
                {
                    System.Diagnostics.Debug.WriteLine(">>>>>> RefreshManifests: Calling RestoreState for the first time");
                    _hasRestoredState = true;
                    RestoreState();
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine(">>>>>> RefreshManifests: Skipping RestoreState (already restored)");
                }
                
                System.Diagnostics.Debug.WriteLine(">>>>>> RefreshManifests COMPLETE\n");
            }
        }

        private void RestoreState()
        {
            try
            {
                var state = _libraryStateService.LoadState();
                if (state == null || string.IsNullOrEmpty(state.Server))
                {
                    System.Diagnostics.Debug.WriteLine("RestoreState: No saved state found");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"RestoreState: Attempting to restore server '{state.Server}'");
                
                // Find and select the saved server
                for (int i = 0; i < ServerCombo.Items.Count; i++)
                {
                    var itemText = ServerCombo.Items[i]?.ToString() ?? string.Empty;
                    if (itemText == state.Server)
                    {
                        System.Diagnostics.Debug.WriteLine($"RestoreState: Found server at index {i}, selecting it");
                        ServerCombo.SelectedIndex = i;
                        return;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"RestoreState: Server '{state.Server}' not found in current list");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring state: {ex.Message}");
            }
        }

        private void SaveState()
        {
            try
            {
                var server = ServerCombo.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(server) || server == "No servers found" || server == "No manifests found")
                {
                    _libraryStateService.SaveState(null, null, null);
                }
                else
                {
                    _libraryStateService.SaveState(server, null, null);
                    System.Diagnostics.Debug.WriteLine($"SaveState: Saved server '{server}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving state: {ex.Message}");
            }
        }

        private void ServerCombo_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            System.Diagnostics.Debug.WriteLine("\n========== ServerCombo_SelectionChanged CALLED ==========");
            System.Diagnostics.Debug.WriteLine($"  Sender: {sender}");
            System.Diagnostics.Debug.WriteLine($"  EventArgs: {(e == null ? "NULL (manual call)" : $"Added={e.AddedItems.Count}, Removed={e.RemovedItems.Count}")}");
            System.Diagnostics.Debug.WriteLine($"  _isRefreshing: {_isRefreshing}");
            System.Diagnostics.Debug.WriteLine($"  _isLoadingPatches: {_isLoadingPatches}");
            System.Diagnostics.Debug.WriteLine($"  ServerCombo.SelectedItem: {ServerCombo?.SelectedItem}");
            System.Diagnostics.Debug.WriteLine($"  ServerCombo.SelectedIndex: {ServerCombo?.SelectedIndex}");
            
            // Only prevent execution during refresh
            if (_isRefreshing)
            {
                System.Diagnostics.Debug.WriteLine("  EXITING: _isRefreshing is true");
                return;
            }
            if (ServerCombo?.SelectedItem == null)
            {
                System.Diagnostics.Debug.WriteLine("  ServerCombo.SelectedItem is null - showing all manifests");
                var allFiles = Directory.Exists(App.ManifestsPath) 
                    ? Directory.GetFiles(App.ManifestsPath, "*.manifest") 
                    : Array.Empty<string>();
                PopulateManifestCombo(allFiles);
                return;
            }
            
            // Prevent re-entry
            if (_isLoadingPatches)
            {
                System.Diagnostics.Debug.WriteLine("  EXITING: _isLoadingPatches is true (re-entry prevention)");
                return;
            }
            
            // Ensure ComboBox stays enabled and interactive
            ServerCombo.IsEnabled = true;
            System.Diagnostics.Debug.WriteLine("  Proceeding with server change...");
            
            _isLoadingPatches = true;
            
            try
            {
                // Handle both string and ComboBoxItem
                string server = ServerCombo.SelectedItem is ComboBoxItem item 
                    ? item.Content?.ToString() ?? string.Empty
                    : ServerCombo.SelectedItem?.ToString() ?? string.Empty;
                
                System.Diagnostics.Debug.WriteLine($"  Selected server: '{server}'");

                if (string.IsNullOrEmpty(server) || server == "No servers found" || server == "No manifests found")
                {
                    PatchCombo.SelectionChanged -= PatchCombo_SelectionChanged;
                    PatchCombo.Items.Clear();
                    PatchCombo.IsEnabled = false;
                    PatchCombo.SelectionChanged += PatchCombo_SelectionChanged;

                    // If server selection is cleared, show ALL manifests
                    var allFiles = Directory.Exists(App.ManifestsPath) 
                        ? Directory.GetFiles(App.ManifestsPath, "*.manifest") 
                        : Array.Empty<string>();
                    PopulateManifestCombo(allFiles);
                    return;
                }
                
                // Get all manifest files
                string[] allManifestFiles;
                try
                {
                    allManifestFiles = Directory.GetFiles(App.ManifestsPath, "*.manifest");
                }
                catch (Exception dirEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading directory: {dirEx.Message}");
                    allManifestFiles = Array.Empty<string>();
                }
                
                // Filter by server - filename format: SERVER_PATCH_NAME.manifest
                var serverManifests = allManifestFiles
                    .Where(file =>
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var parts = fileName.Split('_', 3);
                        return parts.Length >= 1 && parts[0] == server;
                    })
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Found {serverManifests.Count} manifests for server '{server}'");

                // Extract patches for this server
                var patches = new HashSet<string>();
                foreach (var file in serverManifests)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var parts = fileName.Split('_', 3);
                    if (parts.Length >= 2)
                    {
                        patches.Add(parts[1]);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Found {patches.Count} unique patches for server '{server}': {string.Join(", ", patches)}");

                // Sort patches in descending order (newest first)
                var sortedPatches = patches
                    .OrderByDescending(p => 
                    {
                        try { return new Version(p); }
                        catch { return new Version(0, 0); }
                    })
                    .ToList();

                // Store current selection to try to preserve it
                string? currentPatch = PatchCombo.SelectedItem?.ToString();
                System.Diagnostics.Debug.WriteLine($"  Current patch selection: '{currentPatch}'");
                
                // Detach event handler to prevent it from firing during population
                System.Diagnostics.Debug.WriteLine("  Detaching PatchCombo.SelectionChanged handler");
                PatchCombo.SelectionChanged -= PatchCombo_SelectionChanged;
                
                PatchCombo.Items.Clear();
                ManifestCombo.Items.Clear();
                
                if (sortedPatches.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"  Adding {sortedPatches.Count} patches to PatchCombo");
                    foreach (var patch in sortedPatches)
                    {
                        PatchCombo.Items.Add(patch);
                    }
                    PatchCombo.IsEnabled = true;
                    
                    // Don't auto-select any patch - let user choose
                    System.Diagnostics.Debug.WriteLine($"  Added {sortedPatches.Count} patches, no auto-selection");
                    
                    // Reattach the event handler
                    System.Diagnostics.Debug.WriteLine("  Reattaching PatchCombo.SelectionChanged handler");
                    PatchCombo.SelectionChanged += PatchCombo_SelectionChanged;
                }
                else
                {
                    PatchCombo.Items.Add("No patches found");
                    PatchCombo.IsEnabled = false;
                    PatchCombo.SelectionChanged += PatchCombo_SelectionChanged;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading patches for server: {ex.Message}\n{ex.StackTrace}");
                
                PatchCombo.SelectionChanged -= PatchCombo_SelectionChanged;
                PatchCombo.Items.Clear();
                PatchCombo.Items.Add(new ComboBoxItem { Content = "Error loading patches" });
                PatchCombo.IsEnabled = false;
                PatchCombo.SelectionChanged += PatchCombo_SelectionChanged;
            }
            finally
            {
                _isLoadingPatches = false;
                SaveState(); // Save state after patches are loaded
                System.Diagnostics.Debug.WriteLine($"  Set _isLoadingPatches = false");
                System.Diagnostics.Debug.WriteLine("========== ServerCombo_SelectionChanged COMPLETE ==========\n");
            }
        }

        private void PatchCombo_SelectionChanged(object? sender, SelectionChangedEventArgs? e)
        {
            System.Diagnostics.Debug.WriteLine("\n---------- PatchCombo_SelectionChanged CALLED ----------");
            System.Diagnostics.Debug.WriteLine($"  _isRefreshing: {_isRefreshing}");
            System.Diagnostics.Debug.WriteLine($"  _isLoadingManifests: {_isLoadingManifests}");
            System.Diagnostics.Debug.WriteLine($"  PatchCombo.SelectedItem: {PatchCombo?.SelectedItem}");
            
            // Only prevent execution during refresh
            if (_isRefreshing)
            {
                System.Diagnostics.Debug.WriteLine($"  EXITING: _isRefreshing is true");
                return;
            }
            
            // Prevent re-entry while this method is executing
            if (_isLoadingManifests)
            {
                System.Diagnostics.Debug.WriteLine($"  EXITING: _isLoadingManifests is true (re-entry prevention)");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine("  Proceeding with patch change...");
            
            _isLoadingManifests = true;
            
            try
            {
                if (PatchCombo?.SelectedItem == null)
                {
                    ManifestCombo.Items.Clear();
                    ManifestCombo.IsEnabled = false;
                    DownloadBtn.IsEnabled = false;
                    return;
                }
                
                // Handle both string and ComboBoxItem
                string patch = PatchCombo.SelectedItem is ComboBoxItem item
                    ? item.Content?.ToString() ?? string.Empty
                    : PatchCombo.SelectedItem?.ToString() ?? string.Empty;

                if (ServerCombo.SelectedItem == null)
                {
                    ManifestCombo.Items.Clear();
                    ManifestCombo.IsEnabled = false;
                    DownloadBtn.IsEnabled = false;
                    return;
                }
                
                string server = ServerCombo.SelectedItem is ComboBoxItem serverItem
                    ? serverItem.Content?.ToString() ?? string.Empty
                    : ServerCombo.SelectedItem?.ToString() ?? string.Empty;
                
                System.Diagnostics.Debug.WriteLine($"  Server: '{server}', Patch: '{patch}'");

                if (string.IsNullOrEmpty(patch) || patch == "No patches found" || patch == "No manifests found")
                {
                    ManifestCombo.Items.Clear();
                    ManifestCombo.IsEnabled = false;
                    DownloadBtn.IsEnabled = false;
                    return;
                }

                if (string.IsNullOrEmpty(server) || server == "No servers found" || server == "No manifests found")
                {
                    ManifestCombo.Items.Clear();
                    ManifestCombo.IsEnabled = false;
                    DownloadBtn.IsEnabled = false;
                    return;
                }
                
                // Get all manifest files - wrap in try-catch
                string[] allManifestFiles;
                try
                {
                    allManifestFiles = Directory.GetFiles(App.ManifestsPath, "*.manifest");
                }
                catch (Exception dirEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading directory: {dirEx.Message}");
                    allManifestFiles = Array.Empty<string>();
                }
                System.Diagnostics.Debug.WriteLine($"PatchCombo_SelectionChanged: Found {allManifestFiles.Length} total manifest files");
                
                // Filter by server AND patch - filename format: SERVER_PATCH_NAME.manifest
                var manifestFiles = allManifestFiles
                    .Where(file =>
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var parts = fileName.Split('_', 3);
                        if (parts.Length >= 2)
                        {
                            return parts[0] == server && parts[1] == patch;
                        }
                        return false;
                    })
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"PatchCombo_SelectionChanged: Found {manifestFiles.Count} manifests for server={server}, patch={patch}");
                PopulateManifestCombo(manifestFiles, autoSelectFirst: true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading manifests: {ex.Message}\n{ex.StackTrace}");
                ManifestCombo.Items.Clear();
                ManifestCombo.Items.Add(new ComboBoxItem { Content = "Error loading manifests" });
                ManifestCombo.IsEnabled = false;
                DownloadBtn.IsEnabled = false;
            }
            finally
            {
                _isLoadingManifests = false;
                System.Diagnostics.Debug.WriteLine("  Set _isLoadingManifests = false");
                System.Diagnostics.Debug.WriteLine("---------- PatchCombo_SelectionChanged COMPLETE ----------\n");
            }
        }

        private void PopulateManifestCombo(IEnumerable<string> filePaths, bool autoSelectFirst = false)
        {
            ManifestCombo.Items.Clear();
            DownloadBtn.IsEnabled = false;
            DeleteManifestBtn.Visibility = Visibility.Collapsed;

            var sortedFiles = filePaths.OrderBy(f => Path.GetFileName(f)).ToList();
            if (sortedFiles.Any())
            {
                foreach (var file in sortedFiles)
                {
                    var fileInfo = new FileInfo(file);
                    var manifestInfo = new ManifestFileInfo
                    {
                        Name = fileInfo.Name,
                        FullPath = fileInfo.FullName
                    };
                    var comboItem = new ComboBoxItem
                    {
                        Content = manifestInfo.Name,
                        Tag = manifestInfo
                    };
                    ManifestCombo.Items.Add(comboItem);
                }
                ManifestCombo.IsEnabled = true;
                
                if (autoSelectFirst)
                {
                    ManifestCombo.SelectedIndex = 0;
                }
            }
            else
            {
                ManifestCombo.Items.Add(new ComboBoxItem { Content = "No manifests found" });
                ManifestCombo.IsEnabled = false;
            }
        }

        private void ManifestCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"\n>>> ManifestCombo_SelectionChanged: SelectedItem = {ManifestCombo.SelectedItem}");
            if (ManifestCombo.SelectedItem is ComboBoxItem item && item.Tag is ManifestFileInfo info)
            {
                System.Diagnostics.Debug.WriteLine("  Valid manifest selected, enabling download button");
                DownloadBtn.IsEnabled = true;
                DeleteManifestBtn.Visibility = Visibility.Visible;
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("  Invalid or no manifest selected, disabling download button");
                DownloadBtn.IsEnabled = false;
                DeleteManifestBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void DeleteManifestFromList_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ManifestFileInfo info)
            {
                DeleteManifestInternal(info);
            }
        }

        private void DeleteManifestBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ManifestCombo.SelectedItem is not ComboBoxItem item || item.Tag is not ManifestFileInfo info)
                return;

            DeleteManifestInternal(info);
        }

        private void DeleteManifestInternal(ManifestFileInfo info)
        {
            var confirmed = ModernDialog.ShowConfirm(
                Window.GetWindow(this),
                "Confirm Delete",
                $"Are you sure you want to delete the manifest '{info.Name}'?\n\nThis will remove it from your library.",
                primaryLabel: "Delete",
                secondaryLabel: "Cancel",
                icon: "🗑️");

            if (confirmed)
            {
                try
                {
                    if (File.Exists(info.FullPath))
                    {
                        File.Delete(info.FullPath);
                        System.Diagnostics.Debug.WriteLine($"Deleted manifest: {info.FullPath}");

                        // Explicitly clear UI to prevent zombie entries
                        DownloadBtn.IsEnabled = false;
                        DeleteManifestBtn.Visibility = Visibility.Collapsed;

                        // Refresh the list
                        RefreshManifests();
                    }
                }
                catch (Exception ex)
                {
                    ModernDialog.ShowError(Window.GetWindow(this), "Error",
                        $"Error deleting manifest: {ex.Message}");
                }
            }
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Output Folder"
            };
            if (dialog.ShowDialog() == true)
            {
                OutputPath.Text = dialog.FolderName;
            }
        }

        private List<string> BuildArgs()
        {
            var args = new List<string>();

            var output = OutputPath.Text.Trim();
            if (!string.IsNullOrEmpty(output) && output != "output")
            {
                args.Add("-o");
                args.Add(output);
            }

            var filterText = FilterInput.Text.Trim();
            if (!string.IsNullOrEmpty(filterText))
            {
                args.Add("-f");
                args.Add(filterText);
            }

            var unfilterText = UnfilterInput.Text.Trim();
            if (!string.IsNullOrEmpty(unfilterText))
            {
                args.Add("-u");
                args.Add(unfilterText);
            }

            var langsText = LanguagesInput.Text.Trim();
            if (!string.IsNullOrEmpty(langsText))
            {
                args.Add("-l");
                args.AddRange(langsText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            if (IncludeNeutral.IsChecked == true)
            {
                args.Add("-n");
            }

            if (NoLangs.IsChecked == true)
            {
                args.Add("--no-langs");
            }

            if (int.TryParse(ThreadsSpin.Text, out var threads) && threads > 1)
            {
                args.Add("-t");
                args.Add(threads.ToString());
            }

            var bundle = BundleInput.Text.Trim();
            if (!string.IsNullOrEmpty(bundle))
            {
                args.Add("-b");
                args.Add(bundle);
            }

            if (VerifyOnly.IsChecked == true)
            {
                args.Add("--verify-only");
            }

            if (ExistingOnly.IsChecked == true)
            {
                args.Add("--existing-only");
            }

            if (SkipExisting.IsChecked == true)
            {
                args.Add("--skip-existing");
            }

            if (int.TryParse(VerbositySpin.Text, out var verbosity))
            {
                for (int i = 0; i < verbosity; i++)
                {
                    args.Add("-v");
                }
            }

            return args;
        }

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_exePath) || !File.Exists(_exePath))
            {
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    $"ManifestDownloader.exe not found at:\n{_exePath}\n\nPlease ensure the executable is in the correct location.");
                return;
            }

            if (ManifestCombo.SelectedItem == null)
            {
                ModernDialog.ShowError(Window.GetWindow(this), "Warning",
                    "Please select a manifest first.");
                return;
            }

            try
            {
                if (ManifestCombo.SelectedItem is not ComboBoxItem selectedItem ||
                    selectedItem.Tag is not ManifestFileInfo manifestInfo)
                {
                    ModernDialog.ShowError(Window.GetWindow(this), "Warning",
                        "Please select a valid manifest first.");
                    return;
                }

                var manifestPath = manifestInfo.FullPath;

                if (string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
                {
                    ModernDialog.ShowError(Window.GetWindow(this), "Error",
                        "Selected manifest file does not exist.");
                    return;
                }

                var args = BuildArgs();
                args.Insert(0, manifestPath);

                LogOutput.Clear();
                ProgressBar.Visibility = Visibility.Visible;
                DownloadBtn.IsEnabled = false;
                StopDownloadBtn.IsEnabled = true;
                LogPanel.Visibility = Visibility.Visible;
                ShowLogBtn.Visibility = Visibility.Collapsed;
                _stopRequested = false;

                await StartDownloadProcessAsync(_exePath, args);
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    $"Error: {ex.Message}");
                ProgressBar.Visibility = Visibility.Collapsed;
                DownloadBtn.IsEnabled = true;
            }
        }

        private async Task StartDownloadProcessAsync(string exePath, List<string> args)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = string.Join(" ", args.Select(arg => $"\"{arg}\"")),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                LogOutput.AppendText($"Starting download...\n");
                LogOutput.AppendText($"Command: {exePath} {startInfo.Arguments}\n\n");

                _downloadProcess = new Process { StartInfo = startInfo };

                _downloadProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogOutput.AppendText(e.Data + "\n");
                            LogOutput.ScrollToEnd();
                        });
                    }
                };

                _downloadProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            LogOutput.AppendText(e.Data + "\n");
                            LogOutput.ScrollToEnd();
                        });
                    }
                };

                _downloadProcess.Start();
                _downloadProcess.BeginOutputReadLine();
                _downloadProcess.BeginErrorReadLine();

                await Task.Run(() => _downloadProcess.WaitForExit());

                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Visibility = Visibility.Collapsed;
                    DownloadBtn.IsEnabled = true;
                    StopDownloadBtn.IsEnabled = false;

                    var wasCancelled = _downloadProcess.ExitCode != 0 && _stopRequested;
                    if (_downloadProcess.ExitCode == 0)
                    {
                        LogOutput.AppendText("\n✓ Download completed successfully!\n");
                        var outputDir = OutputPath.Text.Trim();
                        ModernDialog.ShowInfo(
                            Window.GetWindow(this),
                            "Download Complete",
                            $"Manifest download finished successfully.\n\n" +
                            (string.IsNullOrEmpty(outputDir) ? "" : $"Output folder:\n{outputDir}"),
                            icon: "✅");
                    }
                    else if (!wasCancelled)
                    {
                        LogOutput.AppendText($"\n✗ Download failed with exit code {_downloadProcess.ExitCode}\n");
                        ModernDialog.ShowError(
                            Window.GetWindow(this),
                            "Download Failed",
                            $"ManifestDownloader exited with code {_downloadProcess.ExitCode}. Check the log for details.");
                    }
                    else
                    {
                        LogOutput.AppendText("\n⏹ Download cancelled.\n");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Visibility = Visibility.Collapsed;
                    DownloadBtn.IsEnabled = true;
                    LogOutput.AppendText($"\n✗ Error: {ex.Message}\n");
                    ModernDialog.ShowError(
                        Window.GetWindow(this),
                        "Download Error",
                        $"An error occurred during download:\n\n{ex.Message}");
                });
            }
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshManifests();
        }

        private void CloseLogBtn_Click(object sender, RoutedEventArgs e)
        {
            LogPanel.Visibility = Visibility.Collapsed;
            ShowLogBtn.Visibility = Visibility.Visible;
        }

        private void ShowLogBtn_Click(object sender, RoutedEventArgs e)
        {
            LogPanel.Visibility = Visibility.Visible;
            ShowLogBtn.Visibility = Visibility.Collapsed;
        }

        private void ClearLogBtn_Click(object sender, RoutedEventArgs e)
        {
            LogOutput.Clear();
        }

        private void StopDownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            _stopRequested = true;
            StopDownload();
            LogOutput.AppendText("\n⚠ Download stopped by user\n");
        }

        private void StopDownload()
        {
            try
            {
                if (_downloadProcess != null && !_downloadProcess.HasExited)
                {
                    _stopRequested = true;
                    System.Diagnostics.Debug.WriteLine("Killing download process...");
                    _downloadProcess.Kill(true); // Kill process tree
                    _downloadProcess.WaitForExit(2000); // Wait up to 2 seconds
                    
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Visibility = Visibility.Collapsed;
                        DownloadBtn.IsEnabled = true;
                        StopDownloadBtn.IsEnabled = false;
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping download: {ex.Message}");
            }
        }
    }
}
