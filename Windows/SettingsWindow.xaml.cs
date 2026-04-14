using System;
using System.Windows;
using System.Windows.Controls;
using ManifestDownloaderGUI.Services;

namespace ManifestDownloaderGUI.Windows
{
    public partial class SettingsWindow : Window
    {
        private readonly ConfigService _configService;
        private TextBox? _tokenTextBox;

        private bool _suppressNotifyToggleEvent;

        public SettingsWindow()
        {
            InitializeComponent();
            _configService = new ConfigService(App.AppDataPath);
            LoadExistingToken();
            LoadTokenStatus();
            LoadUpdateSettings();
        }

        private void LoadUpdateSettings()
        {
            _suppressNotifyToggleEvent = true;
            NotifyUpdatesToggle.IsChecked = _configService.GetNotifyUpdates();
            _suppressNotifyToggleEvent = false;

            var installed = _configService.GetManifestDownloaderVersion();
            UpdateStatusText.Text = string.IsNullOrEmpty(installed)
                ? "Installed version: (unknown)"
                : $"Installed version: {installed}";

            AppVersionText.Text = $"Installed version: {AppUpdateService.CurrentAppVersionString}";
        }

        private async void CheckAppUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            CheckAppUpdateBtn.IsEnabled = false;
            var originalContent = CheckAppUpdateBtn.Content;
            CheckAppUpdateBtn.Content = "Checking…";
            AppVersionText.Text = "Checking GitHub for the latest release…";

            try
            {
                using var updateService = new AppUpdateService(_configService);
                var check = await updateService.CheckForUpdateAsync();

                if (!check.Success)
                {
                    AppVersionText.Text = $"Installed version: {AppUpdateService.CurrentAppVersionString}  —  Check failed: {check.Error}";
                    ModernDialog.ShowError(this, "Update Check Failed",
                        $"Could not check for updates:\n\n{check.Error}");
                    return;
                }

                AppVersionText.Text = $"Installed version: {AppUpdateService.CurrentAppVersionString}  —  Latest: {check.LatestTag}";

                if (!check.IsUpdateAvailable)
                {
                    ModernDialog.ShowInfo(this, "No Updates",
                        $"You are running the latest version ({AppUpdateService.CurrentAppVersionString}).",
                        icon: "✅");
                    return;
                }

                var accepted = ModernDialog.ShowUpdateAvailable(
                    this,
                    "App Update Available",
                    "A new version of ManifestDownloaderGUI is available. Download and install now? The app will restart automatically.",
                    check.InstalledVersion ?? "",
                    check.LatestTag ?? "",
                    primaryLabel: "Update & Restart",
                    secondaryLabel: "Later",
                    icon: "🚀");
                if (!accepted) return;

                var dlg = new AppUpdateDownloadWindow(updateService, check) { Owner = this };
                dlg.ShowDialog();

                if (dlg.DownloadSucceeded && !string.IsNullOrEmpty(dlg.DownloadedPath))
                {
                    try
                    {
                        updateService.LaunchUpdateAndExit(dlg.DownloadedPath);
                        Application.Current.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        ModernDialog.ShowError(this, "Update Failed",
                            $"Could not apply update: {ex.Message}\n\nDownloaded file: {dlg.DownloadedPath}");
                    }
                }
                else if (!string.IsNullOrEmpty(dlg.ErrorMessage))
                {
                    ModernDialog.ShowError(this, "Update Failed",
                        $"Update failed: {dlg.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(this, "Error",
                    $"Error checking for updates: {ex.Message}");
            }
            finally
            {
                CheckAppUpdateBtn.IsEnabled = true;
                CheckAppUpdateBtn.Content = originalContent;
            }
        }

        private void NotifyUpdatesToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressNotifyToggleEvent) return;
            try
            {
                _configService.SaveNotifyUpdates(NotifyUpdatesToggle.IsChecked == true);
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(this, "Error",
                    $"Error saving setting: {ex.Message}");
            }
        }

        private async void CheckUpdateBtn_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdateBtn.IsEnabled = false;
            var originalContent = CheckUpdateBtn.Content;
            CheckUpdateBtn.Content = "Checking…";
            UpdateStatusText.Text = "Checking GitHub for the latest release…";

            try
            {
                using var updateService = new ManifestDownloaderUpdateService(_configService);
                var check = await updateService.CheckForUpdateAsync();

                if (!check.Success)
                {
                    UpdateStatusText.Text = $"Check failed: {check.Error}";
                    ModernDialog.ShowError(this, "Update Check Failed",
                        $"Could not check for updates:\n\n{check.Error}");
                    return;
                }

                if (!check.IsUpdateAvailable)
                {
                    UpdateStatusText.Text = $"You already have the latest version ({check.LatestVersion}).";
                    ModernDialog.ShowInfo(this, "No Updates",
                        $"ManifestDownloader is up to date.\n\nVersion: {check.LatestVersion}",
                        icon: "✅");
                    return;
                }

                var accepted = ModernDialog.ShowUpdateAvailable(
                    this,
                    "ManifestDownloader Update Available",
                    "A new version of the ManifestDownloader tool is available. Download now?",
                    check.InstalledVersion ?? "(unknown)",
                    check.LatestVersion ?? "",
                    primaryLabel: "Download",
                    secondaryLabel: "Later",
                    icon: "⬇️");
                if (!accepted) return;

                var dlg = new DownloadProgressWindow(updateService, check) { Owner = this };
                dlg.ShowDialog();

                if (dlg.DownloadSucceeded)
                {
                    UpdateStatusText.Text = $"Installed version: {check.LatestVersion}";
                    ModernDialog.ShowInfo(this, "Update Complete",
                        $"ManifestDownloader updated to {check.LatestVersion}.",
                        icon: "✅");
                }
                else if (!string.IsNullOrEmpty(dlg.ErrorMessage))
                {
                    ModernDialog.ShowError(this, "Update Failed",
                        $"Update failed: {dlg.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(this, "Error",
                    $"Error checking for updates: {ex.Message}");
            }
            finally
            {
                CheckUpdateBtn.IsEnabled = true;
                CheckUpdateBtn.Content = originalContent;
            }
        }

        private void LoadExistingToken()
        {
            try
            {
                var existingToken = _configService.GetGitHubToken();
                if (!string.IsNullOrEmpty(existingToken))
                {
                    // Pre-fill the token field (will be masked)
                    TokenInput.Password = existingToken;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading existing token: {ex.Message}");
            }
        }

        private void LoadTokenStatus()
        {
            var hasToken = _configService.HasGitHubToken();
            if (hasToken)
            {
                TokenStatus.Text = "✓ GitHub token is configured. Your rate limit is increased to 5000 requests/hour.";
                TokenStatus.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TealAccentBrush"];
            }
            else
            {
                TokenStatus.Text = "⚠ No GitHub token configured. Using default rate limit (60 requests/hour).";
                TokenStatus.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextSecondaryBrush"];
            }
        }

        private void ShowToken_Checked(object sender, RoutedEventArgs e)
        {
            // Convert PasswordBox to TextBox to show password
            var password = TokenInput.Password;
            var border = TokenInput.Parent as Border;
            
            if (border == null)
            {
                System.Diagnostics.Debug.WriteLine("Error: TokenInput.Parent is not a Border");
                return;
            }

            _tokenTextBox = new TextBox
            {
                Text = password,
                Height = 50,
                FontSize = 14,
                Padding = new Thickness(15, 0, 15, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent
            };

            border.Child = _tokenTextBox;
        }

        private void ShowToken_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_tokenTextBox == null) return;

            // Convert TextBox back to PasswordBox
            var text = _tokenTextBox.Text;
            var border = _tokenTextBox.Parent as Border;
            
            if (border == null)
            {
                System.Diagnostics.Debug.WriteLine("Error: _tokenTextBox.Parent is not a Border");
                return;
            }

            TokenInput.Password = text;
            border.Child = TokenInput;

            _tokenTextBox = null;
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var token = _tokenTextBox?.Text ?? TokenInput.Password;
                token = token.Trim();

                if (string.IsNullOrEmpty(token))
                {
                    ModernDialog.ShowInfo(this, "No Token",
                        "Please enter a GitHub token or click 'Clear Token' to remove the existing one.",
                        icon: "ℹ️");
                    return;
                }

                // Validate token format (GitHub tokens are typically 40 or 64 characters)
                if (token.Length < 20)
                {
                    var confirmed = ModernDialog.ShowConfirm(this,
                        "Token Validation",
                        "The token you entered seems too short. GitHub tokens are usually 40 or more characters.\n\nDo you want to save it anyway?",
                        primaryLabel: "Save Anyway",
                        secondaryLabel: "Cancel",
                        icon: "⚠️");

                    if (!confirmed)
                        return;
                }

                _configService.SaveGitHubToken(token);
                LoadTokenStatus();

                ModernDialog.ShowInfo(this, "Success",
                    "GitHub token saved successfully!\n\nYou may need to restart the application for the changes to take effect.",
                    icon: "✅");

                DialogResult = true;
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(this, "Error",
                    $"Error saving token: {ex.Message}");
            }
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            var confirmed = ModernDialog.ShowConfirm(this,
                "Clear Token",
                "Are you sure you want to clear the GitHub token? You will go back to the default rate limit (60 requests/hour).",
                primaryLabel: "Clear",
                secondaryLabel: "Cancel",
                icon: "❓");

            if (confirmed)
            {
                try
                {
                    _configService.ClearGitHubToken();
                    TokenInput.Password = "";
                    if (_tokenTextBox != null)
                    {
                        _tokenTextBox.Text = "";
                    }
                    LoadTokenStatus();

                    ModernDialog.ShowInfo(this, "Success",
                        "GitHub token cleared successfully!",
                        icon: "✅");
                }
                catch (Exception ex)
                {
                    ModernDialog.ShowError(this, "Error",
                        $"Error clearing token: {ex.Message}");
                }
            }
        }

        private void HardRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            var confirmed = ModernDialog.ShowConfirm(this,
                "Hard Refresh / Factory Reset",
                "This will COMPLETELY reset the application. It will delete:\n" +
                "• All downloaded manifests\n" +
                "• All cached data\n" +
                "• Your GitHub settings (including the token)\n" +
                "• All saved selection states\n" +
                "• The ManifestDownloader.exe tool (will be re-downloaded on next launch)\n\n" +
                "The application will close and you will need to restart it.\n\n" +
                "Are you sure you want to continue?",
                primaryLabel: "Reset Everything",
                secondaryLabel: "Cancel",
                icon: "⚠️");

            if (confirmed)
            {
                try
                {
                    var appDataDir = new System.IO.DirectoryInfo(App.AppDataPath);
                    if (appDataDir.Exists)
                    {
                        // Delete ALL subdirectories (including Tools)
                        foreach (var dir in appDataDir.GetDirectories())
                        {
                            try { dir.Delete(true); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not delete directory {dir.Name}: {ex.Message}"); }
                        }

                        // Delete all files in the root AppData directory
                        foreach (var file in appDataDir.GetFiles())
                        {
                            try { file.Delete(); }
                            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Could not delete file {file.Name}: {ex.Message}"); }
                        }
                    }

                    ModernDialog.ShowInfo(this, "Success",
                        "Factory reset complete. The application will now close.\nManifestDownloader.exe will be re-downloaded from GitHub on next launch.",
                        icon: "✅");
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    ModernDialog.ShowError(this, "Error",
                        $"Error during factory reset: {ex.Message}\n\nSome files might be in use. Try closing the app and deleting the folder '{App.AppDataPath}' manually.");
                }
            }
        }
    }
}

