using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ManifestDownloaderGUI.Services;

namespace ManifestDownloaderGUI.Windows
{
    public partial class PBE1DownloadWindow : Window
    {
        private readonly PBE1DownloadService _downloadService;

        public PBE1DownloadWindow()
        {
            InitializeComponent();
            
            // Load GitHub token if available
            var configService = new Services.ConfigService(App.AppDataPath);
            var githubToken = configService.GetGitHubToken();
            
            _downloadService = new PBE1DownloadService(githubToken);
        }

        private string _lastErrorMessage = string.Empty;

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            DownloadBtn.IsEnabled = false;
            ProgressBar.Visibility = Visibility.Visible;
            StatusText.Text = "Starting download...";
            _lastErrorMessage = string.Empty;

            try
            {
                var progress = new Progress<string>(message =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        StatusText.Text = message;
                        
                        // Save error messages to show later
                        if (message.StartsWith("✗"))
                        {
                            _lastErrorMessage = message;
                        }
                    });
                });

                var success = await _downloadService.DownloadPBE1FolderAsync(App.LocalRepoPath, progress);

                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Visibility = Visibility.Collapsed;
                    DownloadBtn.IsEnabled = true;

                    if (success)
                    {
                        StatusText.Text = "✓ Download completed successfully! The application will now use local files for PBE1.";
                        ModernDialog.ShowInfo(this, "Success",
                            "PBE1 folder downloaded successfully!\n\n" +
                            "The application will now automatically use local files for PBE1.\n" +
                            "You can close this window and select PBE1 server to see all patches.",
                            icon: "✅");
                    }
                    else
                    {
                        // Use the detailed error message from the service if available
                        var errorMessage = !string.IsNullOrEmpty(_lastErrorMessage) 
                            ? _lastErrorMessage.Replace("✗", "").Trim()
                            : "Download failed. Please check your internet connection and try again.";
                        
                        StatusText.Text = $"✗ {errorMessage}";
                        ModernDialog.ShowError(this, "Error",
                            $"Download failed:\n\n{errorMessage}\n\n" +
                            "Possible solutions:\n" +
                            "• Check your internet connection\n" +
                            "• Configure a GitHub token in Settings to avoid rate limits\n" +
                            "• Wait a few minutes and try again");
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Visibility = Visibility.Collapsed;
                    DownloadBtn.IsEnabled = true;
                    var errorMsg = !string.IsNullOrEmpty(_lastErrorMessage) 
                        ? _lastErrorMessage.Replace("✗", "").Trim()
                        : ex.Message;
                    
                    StatusText.Text = $"✗ Error: {errorMsg}";
                    ModernDialog.ShowError(this, "Error",
                        $"Error downloading PBE1 folder:\n\n{errorMsg}\n\n" +
                        (ex.InnerException != null ? $"Details: {ex.InnerException.Message}\n\n" : "") +
                        "Please check your internet connection and try again.");
                });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _downloadService?.Dispose();
            base.OnClosed(e);
        }
    }
}

