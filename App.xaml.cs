using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

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
            
            // Global exception handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            
            try
            {
                // Get AppData Local path
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                AppDataPath = Path.Combine(localAppData, "ManifestDownloaderGUI");
                ManifestsPath = Path.Combine(AppDataPath, "manifests");
                CachePath = Path.Combine(AppDataPath, "cache");
                LocalRepoPath = Path.Combine(AppDataPath, "local-repo");
                
                // Create directories if they don't exist
                Directory.CreateDirectory(AppDataPath);
                Directory.CreateDirectory(ManifestsPath);
                Directory.CreateDirectory(CachePath);
                Directory.CreateDirectory(LocalRepoPath);

                // Auto-deploy tool if missing
                DeployTools();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing application: {ex.Message}\n\n{ex.StackTrace}", 
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeployTools()
        {
            try
            {
                string toolsDir = Path.Combine(AppDataPath, "Tools");
                if (!Directory.Exists(toolsDir))
                {
                    Directory.CreateDirectory(toolsDir);
                }

                string targetExe = Path.Combine(toolsDir, "ManifestDownloader.exe");
                
                // If the exe doesn't exist in AppData, extract it from embedded resources
                if (!File.Exists(targetExe))
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    string resourceName = "ManifestDownloaderGUI.ManifestDownloader.exe";

                    using (Stream? stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream != null)
                        {
                            using (FileStream fileStream = new FileStream(targetExe, FileMode.Create, FileAccess.Write))
                            {
                                stream.CopyTo(fileStream);
                            }
                            System.Diagnostics.Debug.WriteLine($"Extracted tool to: {targetExe}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Embedded resource not found: {resourceName}");
                            // Log all resources for debugging if not found
                            foreach (var res in assembly.GetManifestResourceNames())
                            {
                                System.Diagnostics.Debug.WriteLine($"Available resource: {res}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deploying tools: {ex.Message}");
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"An unhandled exception occurred:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}", 
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show($"A fatal error occurred:\n\n{ex.Message}\n\n{ex.StackTrace}", 
                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

