using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ManifestDownloaderGUI.Services;
using ManifestDownloaderGUI.Windows;
using ManifestInfo = ManifestDownloaderGUI.Services.ManifestInfo;

namespace ManifestDownloaderGUI
{
    public partial class ManifestDownloaderTab : UserControl
    {
        private GitHubApiService _apiService = null!;
        private const string Game = "LoL";
        private const string OsType = "windows";

        private ConfigService _configService = null!;
        private GitHubApiService? _currentApiService;
        private SelectionStateService _stateService = null!;

        public ManifestDownloaderTab()
        {
            try
            {
                InitializeComponent();
                _configService = new ConfigService(App.AppDataPath);
                _stateService = new SelectionStateService(App.AppDataPath);
                InitializeApiService();
                Loaded += async (s, e) => 
                {
                    await LoadServers();
                    RestoreState();
                };
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(Window.GetWindow(this), "Initialization Error",
                    $"Error initializing ManifestDownloaderTab: {ex.Message}\n\n{ex.StackTrace}");
            }
        }

        private async void RestoreState()
        {
            try
            {
                var state = _stateService.LoadState();
                if (state == null) return;

                // Restore server selection
                if (!string.IsNullOrEmpty(state.Server))
                {
                    await Task.Delay(500); // Wait for servers to load
                    var serverItem = ServerCombo.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(item => item.Content?.ToString() == state.Server);
                    if (serverItem != null)
                    {
                        ServerCombo.SelectedItem = serverItem;
                        await LoadPatches();

                        // Restore patch selection
                        if (!string.IsNullOrEmpty(state.Patch))
                        {
                            await Task.Delay(500); // Wait for patches to load
                            var patchItem = PatchCombo.Items.Cast<ComboBoxItem>()
                                .FirstOrDefault(item => item.Content?.ToString() == state.Patch);
                            if (patchItem != null)
                            {
                                PatchCombo.SelectedItem = patchItem;
                                // Manifests will load automatically via event handler
                            }
                        }
                    }
                }
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
                var server = GetComboBoxSelectedValue(ServerCombo);
                var patch = GetComboBoxSelectedValue(PatchCombo);
                string? manifest = null;

                if (ManifestCombo.SelectedItem is ComboBoxItem manifestItem)
                {
                    if (manifestItem.Tag is ManifestInfo info)
                        manifest = info.Name;
                    else
                        manifest = manifestItem.Content?.ToString();
                }

                _stateService.SaveState(
                    string.IsNullOrEmpty(server) || server == "Loading..." || server == "Failed to load servers" ? null : server,
                    string.IsNullOrEmpty(patch) || patch == "Loading..." || patch == "No patches found" ? null : patch,
                    manifest
                );
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving state: {ex.Message}");
            }
        }

        private void InitializeApiService()
        {
            try
            {
                var cacheService = new CacheService(App.CachePath);
                var token = _configService.GetGitHubToken();
                _apiService = new GitHubApiService(cacheService, token);
                _currentApiService = _apiService;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing API service: {ex.Message}");
                // Fallback to service without token
                var cacheService = new CacheService(App.CachePath);
                _apiService = new GitHubApiService(cacheService, null);
                _currentApiService = _apiService;
            }
        }

        private string GetComboBoxSelectedValue(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item)
                return item.Content?.ToString() ?? string.Empty;
            return comboBox.SelectedItem?.ToString() ?? string.Empty;
        }

        private async Task LoadServers(bool forceRefresh = false)
        {
            try
            {
                ServerCombo.Items.Clear();
                ServerCombo.Items.Add(new ComboBoxItem { Content = forceRefresh ? "Refreshing servers..." : "Loading servers..." });
                ServerCombo.IsEnabled = false;
                StatusLabel.Text = forceRefresh ? "Refreshing servers from GitHub..." : "Loading servers from cache...";

                var servers = await _apiService.GetServersAsync(Game, forceRefresh);
                
                Dispatcher.Invoke(() =>
                {
                    ServerCombo.Items.Clear();
                    if (servers != null && servers.Any())
                    {
                        foreach (var server in servers)
                        {
                            var item = new ComboBoxItem { Content = server };
                            ServerCombo.Items.Add(item);
                        }
                        ServerCombo.IsEnabled = true;
                        // Don't auto-select a server - let user choose
                        StatusLabel.Text = $"Found {servers.Count} servers - Please select a server";
                    }
                    else
                    {
                        ServerCombo.Items.Add(new ComboBoxItem { Content = "Failed to load servers" });
                        StatusLabel.Text = "Error: Could not load servers. Please check your internet connection and try again.";
                    }
                });
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("rate limit"))
            {
                Dispatcher.Invoke(() =>
                {
                    ServerCombo.Items.Clear();
                    ServerCombo.Items.Add(new ComboBoxItem { Content = "Rate limit exceeded" });
                    StatusLabel.Text = httpEx.Message;
                    System.Diagnostics.Debug.WriteLine($"Rate limit in LoadServers: {httpEx}");
                });
            }
            catch (Exception ex)
            {
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $"\n\nDetails: {ex.InnerException.Message}";
                }
                
                Dispatcher.Invoke(() =>
                {
                    ServerCombo.Items.Clear();
                    ServerCombo.Items.Add(new ComboBoxItem { Content = "Failed to load servers" });
                    StatusLabel.Text = $"Error loading servers: {errorMessage}\n\nPlease check your internet connection and try again.";
                    System.Diagnostics.Debug.WriteLine($"Exception in LoadServers: {ex}");
                });
            }
        }

        private async void ServerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerCombo.SelectedItem == null) return;
            SaveState();
            
            await LoadPatches();
        }

        private async Task LoadPatches(bool forceRefresh = false)
        {
            var server = GetComboBoxSelectedValue(ServerCombo);

            if (string.IsNullOrEmpty(server) || server == "Loading..." || server == "Failed to load servers" || server == "Rate limit exceeded")
                return;

            Dispatcher.Invoke(() =>
            {
                PatchCombo.Items.Clear();
                PatchCombo.Items.Add(new ComboBoxItem { Content = forceRefresh ? "Refreshing patches..." : "Loading patches..." });
                PatchCombo.IsEnabled = false;
                ManifestCombo.Items.Clear();
                DownloadBtn.IsEnabled = false;
                StatusLabel.Text = forceRefresh ? "Refreshing patches from GitHub API... This may take a moment." : "Checking cache and downloading patches from GitHub API if needed...";
            });
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"LoadPatches: Starting with server={server}, forceRefresh={forceRefresh}");
                
                // Update status to indicate we're actively fetching
                Dispatcher.Invoke(() =>
                {
                    StatusLabel.Text = "Fetching patches from GitHub API... This may take 10-30 seconds, please wait...";
                });
                
                // Use Task.Run to ensure the API call doesn't block the UI thread
                var patches = await Task.Run(async () => 
                {
                    return await _apiService.GetPatchesAsync(Game, server, OsType, forceRefresh);
                });
                
                System.Diagnostics.Debug.WriteLine($"LoadPatches: Received {patches?.Count ?? 0} patches");

                Dispatcher.Invoke(() =>
                {
                    PatchCombo.Items.Clear();
                    if (patches != null && patches.Any())
                    {
                        foreach (var patch in patches)
                        {
                            var item = new ComboBoxItem { Content = patch };
                            PatchCombo.Items.Add(item);
                        }
                        PatchCombo.IsEnabled = true;
                        if (patches.Count > 0)
                        {
                            PatchCombo.SelectedIndex = 0;
                        }
                        StatusLabel.Text = $"Found {patches.Count} patches for {server}";
                    }
                    else
                    {
                        PatchCombo.Items.Add(new ComboBoxItem { Content = "No patches found" });
                        StatusLabel.Text = $"No patches found for {server}. Try clicking 'Refresh Data' button.";
                    }
                });
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("rate limit"))
            {
                System.Diagnostics.Debug.WriteLine($"LoadPatches: Rate limit error - {httpEx.Message}");
                Dispatcher.Invoke(() =>
                {
                    PatchCombo.Items.Clear();
                    PatchCombo.Items.Add(new ComboBoxItem { Content = "Rate limit exceeded" });
                    StatusLabel.Text = httpEx.Message;
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadPatches: Exception - {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadPatches: Stack trace - {ex.StackTrace}");
                
                var errorMessage = ex.Message;
                if (ex.InnerException != null)
                {
                    errorMessage += $"\n\nDetails: {ex.InnerException.Message}";
                }
                
                Dispatcher.Invoke(() =>
                {
                    PatchCombo.Items.Clear();
                    PatchCombo.Items.Add(new ComboBoxItem { Content = "Error loading patches" });
                    StatusLabel.Text = $"Error loading patches: {errorMessage}\n\nPlease check your internet connection and try clicking 'Refresh Data'.";
                });
            }
        }

        private async void PatchCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveState();
            
            var patch = GetComboBoxSelectedValue(PatchCombo);
            var server = GetComboBoxSelectedValue(ServerCombo);

            if (string.IsNullOrEmpty(patch) || patch == "Loading..." || 
                patch == "No patches found" || patch == "Error loading patches")
            {
                ManifestCombo.Items.Clear();
                DownloadBtn.IsEnabled = false;
                return;
            }

            if (string.IsNullOrEmpty(server))
            {
                ManifestCombo.Items.Clear();
                DownloadBtn.IsEnabled = false;
                return;
            }

                Dispatcher.Invoke(() =>
                {
                    ManifestCombo.Items.Clear();
                    ManifestCombo.Items.Add(new ComboBoxItem { Content = "Loading..." });
                    ManifestCombo.IsEnabled = false;
                    DownloadBtn.IsEnabled = false;
                    StatusLabel.Text = "Loading manifests...";
                });

            try
            {
                var manifests = await _apiService.GetManifestsForPatchAsync(Game, server, OsType, patch, forceRefresh: false);

                Dispatcher.Invoke(() =>
                {
                    ManifestCombo.Items.Clear();
                    if (manifests != null && manifests.Any())
                    {
                        foreach (var manifest in manifests)
                        {
                            var item = new ComboBoxItem 
                            { 
                                Content = manifest.Name,
                                Tag = manifest
                            };
                            ManifestCombo.Items.Add(item);
                        }
                        ManifestCombo.IsEnabled = true;
                        if (manifests.Count > 0)
                        {
                            ManifestCombo.SelectedIndex = 0;
                        }
                        DownloadBtn.IsEnabled = true;
                        StatusLabel.Text = $"Found {manifests.Count} manifests for patch {patch}";
                        SaveState(); // Save state after manifest selection
                    }
                    else
                    {
                        ManifestCombo.Items.Add(new ComboBoxItem { Content = "No manifests found" });
                        StatusLabel.Text = $"No manifests found for patch {patch}";
                    }
                });
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("rate limit"))
            {
                Dispatcher.Invoke(() =>
                {
                    ManifestCombo.Items.Clear();
                    ManifestCombo.Items.Add(new ComboBoxItem { Content = "Rate limit exceeded" });
                    StatusLabel.Text = httpEx.Message;
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ManifestCombo.Items.Clear();
                    ManifestCombo.Items.Add(new ComboBoxItem { Content = "Error loading manifests" });
                    StatusLabel.Text = $"Error loading manifests: {ex.Message}";
                });
            }
        }

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ManifestCombo.SelectedItem == null) return;

            var selectedItem = ManifestCombo.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is not ManifestInfo manifest)
                return;

            ProgressBar.Visibility = Visibility.Visible;
            DownloadBtn.IsEnabled = false;
            StatusLabel.Text = "Fetching manifest URL...";

            if (string.IsNullOrEmpty(manifest.DownloadUrl))
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                DownloadBtn.IsEnabled = true;
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    "Manifest download URL is missing");
                return;
            }

            var manifestUrl = await _apiService.GetManifestUrlAsync(manifest.DownloadUrl);
            if (string.IsNullOrEmpty(manifestUrl))
            {
                ProgressBar.Visibility = Visibility.Collapsed;
                DownloadBtn.IsEnabled = true;
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    "Failed to get manifest URL from the .txt file");
                return;
            }

            var patch = GetComboBoxSelectedValue(PatchCombo);
            var server = GetComboBoxSelectedValue(ServerCombo);
            var manifestName = manifest.Name.Replace(".txt", ".manifest");
            var savePath = Path.Combine(App.ManifestsPath, $"{server}_{patch}_{manifestName}");

            StatusLabel.Text = "Downloading manifest...";
            var success = await _apiService.DownloadManifestFileAsync(manifestUrl, savePath);

            ProgressBar.Visibility = Visibility.Collapsed;
            DownloadBtn.IsEnabled = true;

            if (success)
            {
                StatusLabel.Text = $"Manifest downloaded successfully!";
                ModernDialog.ShowInfo(Window.GetWindow(this), "Success",
                    $"Manifest downloaded successfully!\n\n{savePath}",
                    icon: "✅");
            }
            else
            {
                StatusLabel.Text = "Failed to download manifest";
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    "Failed to download manifest");
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshBtn.IsEnabled = false;
            StatusLabel.Text = "Refreshing all data from GitHub API (this may take a moment)...";

            try
            {
                // Refresh servers first
                await LoadServers(forceRefresh: true);
                
                // If a server is selected, refresh patches
                var server = GetComboBoxSelectedValue(ServerCombo);
                if (!string.IsNullOrEmpty(server) && server != "Loading..." && server != "Failed to load servers" && server != "Rate limit exceeded")
                {
                    await LoadPatches(forceRefresh: true);
                    
                    // If a patch is selected, refresh manifests
                    var patch = GetComboBoxSelectedValue(PatchCombo);
                    if (!string.IsNullOrEmpty(patch) && patch != "Loading..." && patch != "No patches found" && patch != "Error loading patches" && patch != "Rate limit exceeded")
                    {
                        // Refresh manifests
                        Dispatcher.Invoke(() =>
                        {
                            ManifestCombo.Items.Clear();
                            ManifestCombo.Items.Add(new ComboBoxItem { Content = "Refreshing..." });
                            ManifestCombo.IsEnabled = false;
                            DownloadBtn.IsEnabled = false;
                            StatusLabel.Text = "Refreshing manifests from GitHub API...";
                        });

                        try
                        {
                            var manifests = await _apiService.GetManifestsForPatchAsync(Game, server, OsType, patch, forceRefresh: true);

                            Dispatcher.Invoke(() =>
                            {
                                ManifestCombo.Items.Clear();
                                if (manifests != null && manifests.Any())
                                {
                                    foreach (var manifest in manifests)
                                    {
                                        var item = new ComboBoxItem 
                                        { 
                                            Content = manifest.Name,
                                            Tag = manifest
                                        };
                                        ManifestCombo.Items.Add(item);
                                    }
                                    ManifestCombo.IsEnabled = true;
                                    if (manifests.Count > 0)
                                    {
                                        ManifestCombo.SelectedIndex = 0;
                                    }
                                    DownloadBtn.IsEnabled = true;
                                    StatusLabel.Text = $"Data refreshed successfully! Found {manifests.Count} manifests for patch {patch}";
                                }
                                else
                                {
                                    ManifestCombo.Items.Add(new ComboBoxItem { Content = "No manifests found" });
                                    StatusLabel.Text = $"Data refreshed. No manifests found for patch {patch}";
                                }
                            });
                        }
                        catch (Exception manifestEx)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                StatusLabel.Text = $"Error refreshing manifests: {manifestEx.Message}";
                            });
                        }
                    }
                    else
                    {
                        StatusLabel.Text = "Data refreshed successfully! Select a patch to load manifests.";
                    }
                }
                else
                {
                    StatusLabel.Text = "Data refreshed successfully! Select a server to load patches.";
                }
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error refreshing data: {ex.Message}";
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    $"Error refreshing data: {ex.Message}");
            }
            finally
            {
                RefreshBtn.IsEnabled = true;
            }
        }

        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new Windows.SettingsWindow
                {
                    Owner = Window.GetWindow(this)
                };

                var result = settingsWindow.ShowDialog();

                // If token was saved, reinitialize API service with new token
                if (result == true)
                {
                    InitializeApiService();
                    StatusLabel.Text = "GitHub token updated. You may need to refresh data for it to take effect.";
                }
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(Window.GetWindow(this), "Error",
                    $"Error opening settings: {ex.Message}");
            }
        }

    }
}
