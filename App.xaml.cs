using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ManifestDownloaderGUI.Services;
using ManifestDownloaderGUI.Windows;

namespace ManifestDownloaderGUI
{
    public partial class App : Application
    {
        public static string AppDataPath { get; private set; } = string.Empty;
        public static string ManifestsPath { get; private set; } = string.Empty;
        public static string CachePath { get; private set; } = string.Empty;
        public static string LocalRepoPath { get; private set; } = string.Empty;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                AppDataPath = Path.Combine(localAppData, "ManifestDownloaderGUI");
                ManifestsPath = Path.Combine(AppDataPath, "manifests");
                CachePath = Path.Combine(AppDataPath, "cache");
                LocalRepoPath = Path.Combine(AppDataPath, "local-repo");

                Directory.CreateDirectory(AppDataPath);
                Directory.CreateDirectory(ManifestsPath);
                Directory.CreateDirectory(CachePath);
                Directory.CreateDirectory(Path.Combine(AppDataPath, "Tools"));

                var configService = new ConfigService(AppDataPath);
                var updateService = new ManifestDownloaderUpdateService(configService);

                if (!updateService.ExeExists)
                {
                    BootstrapDownloadBlocking(updateService);
                }
                else
                {
                    ScheduleBackgroundUpdateCheck(configService, updateService);
                }

                ScheduleBackgroundAppUpdateCheck(configService);
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(null, "Startup Error",
                    $"Error initializing application: {ex.Message}\n\n{ex.StackTrace}");
            }
        }

        private void BootstrapDownloadBlocking(ManifestDownloaderUpdateService updateService)
        {
            try
            {
                var check = updateService.CheckForUpdateAsync().GetAwaiter().GetResult();
                if (!check.Success || string.IsNullOrEmpty(check.DownloadUrl))
                {
                    ModernDialog.ShowError(null, "Setup Required",
                        "ManifestDownloader.exe is not installed and could not be downloaded from GitHub:\n\n" +
                        (check.Error ?? "Unknown error") +
                        "\n\nPlease check your internet connection and restart the app.");
                    return;
                }

                var dlg = new DownloadProgressWindow(updateService, check);
                dlg.ShowDialog();

                if (!dlg.DownloadSucceeded)
                {
                    ModernDialog.ShowError(null, "Download Failed",
                        "The ManifestDownloader tool could not be downloaded. The app may not function until it is installed.\n\n" +
                        (dlg.ErrorMessage ?? ""));
                }
            }
            catch (Exception ex)
            {
                ModernDialog.ShowError(null, "Setup Error",
                    $"Error during first-time download: {ex.Message}");
            }
        }

        private void ScheduleBackgroundUpdateCheck(ConfigService configService, ManifestDownloaderUpdateService updateService)
        {
            if (!configService.GetNotifyUpdates())
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500);
                    var check = await updateService.CheckForUpdateAsync();
                    if (!check.Success || !check.IsUpdateAvailable || string.IsNullOrEmpty(check.DownloadUrl))
                        return;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        var owner = Current?.MainWindow;
                        var accepted = ModernDialog.ShowUpdateAvailable(
                            owner,
                            "ManifestDownloader Update Available",
                            "A new version of the ManifestDownloader tool is available. Do you want to download it now?",
                            check.InstalledVersion ?? "(unknown)",
                            check.LatestVersion ?? "",
                            primaryLabel: "Download",
                            secondaryLabel: "Later",
                            icon: "⬇️");

                        if (accepted)
                        {
                            var dlg = new DownloadProgressWindow(updateService, check) { Owner = owner };
                            dlg.ShowDialog();

                            if (dlg.DownloadSucceeded)
                            {
                                ModernDialog.ShowInfo(owner,
                                    "Update Complete",
                                    $"ManifestDownloader has been updated to {check.LatestVersion}.",
                                    icon: "✅");
                            }
                            else if (!string.IsNullOrEmpty(dlg.ErrorMessage))
                            {
                                ModernDialog.ShowError(owner,
                                    "Update Failed",
                                    $"Update failed: {dlg.ErrorMessage}");
                            }
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background update check failed: {ex.Message}");
                }
            });
        }

        private void ScheduleBackgroundAppUpdateCheck(ConfigService configService)
        {
            if (!configService.GetNotifyUpdates())
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2500);
                    using var appUpdateService = new AppUpdateService(configService);
                    var check = await appUpdateService.CheckForUpdateAsync();
                    if (!check.Success || !check.IsUpdateAvailable || string.IsNullOrEmpty(check.DownloadUrl))
                        return;

                    await Dispatcher.InvokeAsync(() =>
                    {
                        var owner = Current?.MainWindow;
                        var accepted = ModernDialog.ShowUpdateAvailable(
                            owner,
                            "App Update Available",
                            "A new version of ManifestDownloaderGUI is available. Download and install now? The app will restart automatically.",
                            check.InstalledVersion ?? "",
                            check.LatestTag ?? "",
                            primaryLabel: "Update & Restart",
                            secondaryLabel: "Later",
                            icon: "🚀");

                        if (!accepted) return;

                        var dlg = new AppUpdateDownloadWindow(appUpdateService, check) { Owner = owner };
                        dlg.ShowDialog();

                        if (dlg.DownloadSucceeded && !string.IsNullOrEmpty(dlg.DownloadedPath))
                        {
                            try
                            {
                                appUpdateService.LaunchUpdateAndExit(dlg.DownloadedPath);
                                Current?.Shutdown();
                            }
                            catch (Exception ex)
                            {
                                ModernDialog.ShowError(owner,
                                    "Update Failed",
                                    $"Could not apply update: {ex.Message}\n\nThe downloaded file is at:\n{dlg.DownloadedPath}");
                            }
                        }
                        else if (!string.IsNullOrEmpty(dlg.ErrorMessage))
                        {
                            ModernDialog.ShowError(owner,
                                "Update Failed",
                                $"Update failed: {dlg.ErrorMessage}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Background app update check failed: {ex.Message}");
                }
            });
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                ModernDialog.ShowError(Current?.MainWindow, "Error",
                    $"An unhandled exception occurred:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}");
            }
            catch
            {
                MessageBox.Show($"An unhandled exception occurred:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                try
                {
                    ModernDialog.ShowError(Current?.MainWindow, "Fatal Error",
                        $"A fatal error occurred:\n\n{ex.Message}\n\n{ex.StackTrace}");
                }
                catch
                {
                    MessageBox.Show($"A fatal error occurred:\n\n{ex.Message}\n\n{ex.StackTrace}",
                        "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
