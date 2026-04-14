using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ManifestDownloaderGUI.Services;

namespace ManifestDownloaderGUI.Windows
{
    public partial class DownloadProgressWindow : Window
    {
        private readonly ManifestDownloaderUpdateService _updateService;
        private readonly UpdateCheckResult _update;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _completed;

        public bool DownloadSucceeded { get; private set; }
        public string? ErrorMessage { get; private set; }

        public DownloadProgressWindow(ManifestDownloaderUpdateService updateService, UpdateCheckResult update)
        {
            InitializeComponent();
            _updateService = updateService;
            _update = update;
            HeaderText.Text = $"Downloading ManifestDownloader {update.LatestVersion}…";
            Loaded += (_, _) => _ = RunAsync();
            Closing += (s, e) =>
            {
                if (!_completed)
                    _cts.Cancel();
            };
        }

        private async Task RunAsync()
        {
            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress.Value = p.Percentage;
                var mb = p.BytesRead / 1024.0 / 1024.0;
                var total = p.TotalBytes / 1024.0 / 1024.0;
                StatusText.Text = total > 0
                    ? $"{mb:F2} MB / {total:F2} MB  ({p.Percentage:F0}%)"
                    : $"{mb:F2} MB downloaded";
            });

            try
            {
                await _updateService.DownloadAsync(_update, progress, _cts.Token);
                _completed = true;
                DownloadSucceeded = true;
                StatusText.Text = "Download complete.";
                Progress.Value = 100;
                CloseBtn.Content = "Close";
                DialogResult = true;
                Close();
            }
            catch (OperationCanceledException)
            {
                _completed = true;
                DownloadSucceeded = false;
                ErrorMessage = "Download cancelled.";
                DialogResult = false;
                Close();
            }
            catch (Exception ex)
            {
                _completed = true;
                DownloadSucceeded = false;
                ErrorMessage = ex.Message;
                StatusText.Text = $"Error: {ex.Message}";
                CloseBtn.Content = "Close";
            }
        }

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_completed)
                _cts.Cancel();
            else
            {
                DialogResult = DownloadSucceeded;
                Close();
            }
        }
    }
}
