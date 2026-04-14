using System.Windows;
using System.Windows.Input;

namespace ManifestDownloaderGUI.Windows
{
    public partial class ModernDialog : Window
    {
        public bool PrimaryClicked { get; private set; }

        private ModernDialog()
        {
            InitializeComponent();
        }

        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
        {
            PrimaryClicked = true;
            DialogResult = true;
            Close();
        }

        private void SecondaryBtn_Click(object sender, RoutedEventArgs e)
        {
            PrimaryClicked = false;
            DialogResult = false;
            Close();
        }

        private void CloseXBtn_Click(object sender, RoutedEventArgs e)
        {
            PrimaryClicked = false;
            DialogResult = false;
            Close();
        }

        public static bool ShowUpdateAvailable(
            Window? owner,
            string title,
            string message,
            string installedVersion,
            string latestVersion,
            string primaryLabel = "Download",
            string secondaryLabel = "Later",
            string icon = "✨")
        {
            var dlg = new ModernDialog
            {
                Owner = owner
            };
            dlg.TitleText.Text = title;
            dlg.IconText.Text = icon;
            dlg.MessageText.Text = message;
            dlg.InstalledVersionText.Text = string.IsNullOrWhiteSpace(installedVersion) ? "(unknown)" : installedVersion;
            dlg.LatestVersionText.Text = latestVersion;
            dlg.VersionCard.Visibility = Visibility.Visible;
            dlg.PrimaryBtn.Content = primaryLabel;
            dlg.SecondaryBtn.Content = secondaryLabel;
            dlg.ShowDialog();
            return dlg.PrimaryClicked;
        }

        public static void ShowInfo(
            Window? owner,
            string title,
            string message,
            string icon = "✅",
            string buttonLabel = "OK")
        {
            var dlg = new ModernDialog
            {
                Owner = owner
            };
            dlg.TitleText.Text = title;
            dlg.IconText.Text = icon;
            dlg.MessageText.Text = message;
            dlg.VersionCard.Visibility = Visibility.Collapsed;
            dlg.PrimaryBtn.Content = buttonLabel;
            dlg.SecondaryBtn.Visibility = Visibility.Collapsed;
            dlg.ShowDialog();
        }

        public static void ShowError(
            Window? owner,
            string title,
            string message,
            string icon = "⚠️",
            string buttonLabel = "OK")
        {
            ShowInfo(owner, title, message, icon, buttonLabel);
        }

        public static bool ShowConfirm(
            Window? owner,
            string title,
            string message,
            string primaryLabel = "Yes",
            string secondaryLabel = "No",
            string icon = "❓")
        {
            var dlg = new ModernDialog
            {
                Owner = owner
            };
            dlg.TitleText.Text = title;
            dlg.IconText.Text = icon;
            dlg.MessageText.Text = message;
            dlg.VersionCard.Visibility = Visibility.Collapsed;
            dlg.PrimaryBtn.Content = primaryLabel;
            dlg.SecondaryBtn.Content = secondaryLabel;
            dlg.ShowDialog();
            return dlg.PrimaryClicked;
        }
    }
}
