using System.Windows;

namespace ManifestDownloaderGUI
{
    public partial class MainWindow : Window
    {
        private bool _libraryTabActive = false;
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Refresh library when switching to it, and save state when leaving downloader tab
            MainTabControl.SelectionChanged += (s, e) =>
            {
                if (MainTabControl.SelectedItem == LibraryTab)
                {
                    // Only refresh if we're switching TO the library tab, not if it's already active
                    if (!_libraryTabActive)
                    {
                        _libraryTabActive = true;
                        System.Diagnostics.Debug.WriteLine("*** MainWindow: Switching TO Library tab, calling RefreshManifests");
                        // Small delay to ensure UI is ready
                        Dispatcher.BeginInvoke(new System.Action(() =>
                        {
                            LibraryTabContent.RefreshManifests();
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("*** MainWindow: Library tab already active, skipping RefreshManifests");
                    }
                }
                else
                {
                    _libraryTabActive = false;
                    System.Diagnostics.Debug.WriteLine("*** MainWindow: Switching AWAY from Library tab");
                }
            };
        }
    }
}

