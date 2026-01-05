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

        public SettingsWindow()
        {
            InitializeComponent();
            _configService = new ConfigService(App.AppDataPath);
            LoadExistingToken();
            LoadTokenStatus();
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
                    MessageBox.Show("Please enter a GitHub token or click 'Clear Token' to remove the existing one.", 
                        "No Token", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Validate token format (GitHub tokens are typically 40 or 64 characters)
                if (token.Length < 20)
                {
                    var result = MessageBox.Show(
                        "The token you entered seems too short. GitHub tokens are usually 40 or more characters.\n\nDo you want to save it anyway?",
                        "Token Validation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.No)
                        return;
                }

                _configService.SaveGitHubToken(token);
                LoadTokenStatus();
                
                MessageBox.Show("GitHub token saved successfully!\n\nYou may need to restart the application for the changes to take effect.", 
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving token: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear the GitHub token? You will go back to the default rate limit (60 requests/hour).",
                "Clear Token", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
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
                    
                    MessageBox.Show("GitHub token cleared successfully!", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error clearing token: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void HardRefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will COMPLETELY reset the application. It will delete:\n" +
                "- All downloaded manifests\n" +
                "- All cached data\n" +
                "- Your GitHub settings (including the token)\n" +
                "- All saved selection states\n\n" +
                "The only thing kept will be the ManifestDownloader.exe tool.\n\n" +
                "The application will close and you will need to restart it.\n\n" +
                "Are you sure you want to continue?",
                "Hard Refresh / Factory Reset", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var appDataDir = new System.IO.DirectoryInfo(App.AppDataPath);
                    if (appDataDir.Exists)
                    {
                        // Delete all subdirectories except "Tools"
                        foreach (var dir in appDataDir.GetDirectories())
                        {
                            if (dir.Name.Equals("Tools", StringComparison.OrdinalIgnoreCase))
                                continue;
                            
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

                    MessageBox.Show("Factory reset complete. The application will now close.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    Application.Current.Shutdown();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error during factory reset: {ex.Message}\n\nSome files might be in use. Try closing the app and deleting the folder '{App.AppDataPath}' manually (keep the 'Tools' folder).", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}

