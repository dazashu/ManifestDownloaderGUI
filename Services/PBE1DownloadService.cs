using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ManifestDownloaderGUI.Services
{
    /// <summary>
    /// Service to download PBE1 folder from GitHub to local filesystem
    /// </summary>
    public class PBE1DownloadService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly HttpClientHandler _httpClientHandler;
        private readonly List<HttpClient> _downloadClients = new List<HttpClient>();
        private const string GitHubApiBase = "https://api.github.com/repos/Morilli/riot-manifests/contents";
        private const string GitHubTreeApiBase = "https://api.github.com/repos/Morilli/riot-manifests/git/trees/master?recursive=1";
        private const string GitHubRawBase = "https://raw.githubusercontent.com/Morilli/riot-manifests/master";
        private const int MaxConcurrentDownloads = 50; // Maximum parallel downloads for maximum speed
        private readonly string? _githubToken;

        public PBE1DownloadService(string? githubToken = null)
        {
            _githubToken = githubToken;
            
            // Configure HttpClientHandler for maximum performance
            _httpClientHandler = new HttpClientHandler
            {
                MaxConnectionsPerServer = MaxConcurrentDownloads * 2, // Allow more connections per server
                UseCookies = false
            };
            
            _httpClient = new HttpClient(_httpClientHandler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ManifestDownloader-GUI");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.Timeout = TimeSpan.FromMinutes(10); // Increased timeout for large downloads
            
            // Set GitHub token if provided
            if (!string.IsNullOrEmpty(githubToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", githubToken);
                System.Diagnostics.Debug.WriteLine("PBE1DownloadService: GitHub token configured");
            }
            
            // Increase ServicePoint connection limits for better performance
            System.Net.ServicePointManager.DefaultConnectionLimit = MaxConcurrentDownloads * 2;
            
            // Pre-create HttpClient instances for parallel downloads (reuse connections)
            for (int i = 0; i < Math.Min(20, MaxConcurrentDownloads); i++)
            {
                var handler = new HttpClientHandler
                {
                    MaxConnectionsPerServer = 10,
                    UseCookies = false
                };
                
                var client = new HttpClient(handler);
                client.DefaultRequestHeaders.Add("User-Agent", "ManifestDownloader-GUI");
                client.Timeout = TimeSpan.FromMinutes(5);
                
                if (!string.IsNullOrEmpty(githubToken))
                {
                    client.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", githubToken);
                }
                
                _downloadClients.Add(client);
            }
        }

        /// <summary>
        /// Download all .txt files from PBE1/lol-game-client folder
        /// </summary>
        public async Task<bool> DownloadPBE1FolderAsync(string localBasePath, IProgress<string>? progress = null)
        {
            try
            {
                var targetPath = Path.Combine(localBasePath, "LoL", "PBE1", "windows", "lol-game-client");
                Directory.CreateDirectory(targetPath);

                progress?.Report("Fetching file list from GitHub API...");

                // Get all files using Git Trees API (bypasses 1000 item limit)
                var allFiles = new List<GitHubItem>();
                
                progress?.Report("Fetching full directory tree from GitHub (bypassing 1000-item limit)...");
                System.Diagnostics.Debug.WriteLine($"PBE1DownloadService: Fetching tree from: {GitHubTreeApiBase}");

                var treeResponse = await _httpClient.GetAsync(GitHubTreeApiBase);
                
                if (treeResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var errorContent = await treeResponse.Content.ReadAsStringAsync();
                    if (errorContent.Contains("rate limit"))
                    {
                        progress?.Report($"✗ GitHub API rate limit exceeded. Please wait or use a token.");
                        return false;
                    }
                }

                treeResponse.EnsureSuccessStatusCode();
                var treeJson = await treeResponse.Content.ReadAsStringAsync();
                var treeData = JsonConvert.DeserializeObject<GitHubTreeRoot>(treeJson);

                if (treeData == null || treeData.Tree == null)
                {
                    progress?.Report("✗ Failed to parse tree from GitHub.");
                    return false;
                }

                var targetPrefix = "LoL/PBE1/windows/lol-game-client/";
                var txtFiles = treeData.Tree
                    .Where(item => item.Path.StartsWith(targetPrefix) && item.Path.EndsWith(".txt"))
                    .Select(item => new GitHubItem { 
                        Name = Path.GetFileName(item.Path), 
                        Type = "file",
                        Path = item.Path 
                    })
                    .ToList();

                allFiles.AddRange(txtFiles);
                System.Diagnostics.Debug.WriteLine($"Found {allFiles.Count} .txt files in PBE1 using Trees API");

                if (allFiles.Count == 0)
                {
                    progress?.Report("No .txt files found to download.");
                    return false;
                }

                // Validate that we have recent patches (15.x)
                var has15xFiles = allFiles.Any(f => f.Name.StartsWith("15."));
                System.Diagnostics.Debug.WriteLine($"PBE1DownloadService: Total files to download: {allFiles.Count}, has 15.x files: {has15xFiles}");
                
                if (has15xFiles)
                {
                    var recentFiles = allFiles.Where(f => f.Name.StartsWith("15.")).ToList();
                    progress?.Report($"✓ Found {allFiles.Count} files total, including {recentFiles.Count} files from patch 15.x");
                    System.Diagnostics.Debug.WriteLine($"PBE1DownloadService: Found {recentFiles.Count} files from 15.x patches");
                }
                else
                {
                    progress?.Report($"⚠ Warning: Found {allFiles.Count} files but no 15.x patches detected. Some files might be missing.");
                    System.Diagnostics.Debug.WriteLine($"PBE1DownloadService: WARNING - No 15.x files found in {allFiles.Count} total files");
                }

                progress?.Report($"Starting parallel download of {allFiles.Count} files (max {MaxConcurrentDownloads} concurrent downloads)...");

                // Download files in parallel for maximum speed
                var semaphore = new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads);
                var downloadedCount = 0;
                var failedCount = 0;
                var lockObj = new object();
                var clientIndex = 0;
                var clientLock = new object();
                var startTime = DateTime.UtcNow;

                var downloadTasks = allFiles.Select(async file =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var fileUrl = $"{GitHubRawBase}/LoL/PBE1/windows/lol-game-client/{file.Name}";
                        var filePath = Path.Combine(targetPath, file.Name);

                        // Skip if file already exists (resume capability)
                        if (File.Exists(filePath))
                        {
                            lock (lockObj)
                            {
                                downloadedCount++;
                                if (downloadedCount % 50 == 0 || downloadedCount == allFiles.Count)
                                {
                                    var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                                    var speed = downloadedCount / Math.Max(elapsed, 1);
                                    var remaining = allFiles.Count - downloadedCount;
                                    var eta = remaining / Math.Max(speed, 1);
                                    progress?.Report($"Downloading... {downloadedCount}/{allFiles.Count} files " +
                                                   $"({speed:F1} files/sec, ~{eta:F0}s remaining)");
                                }
                            }
                            return;
                        }

                        // Use a dedicated HttpClient from the pool for better parallelism
                        HttpClient downloadClient;
                        lock (clientLock)
                        {
                            downloadClient = _downloadClients[clientIndex % _downloadClients.Count];
                            clientIndex++;
                        }
                        
                        var fileContent = await downloadClient.GetStringAsync(fileUrl);
                        
                        // Write file asynchronously
                        await File.WriteAllTextAsync(filePath, fileContent);

                        lock (lockObj)
                        {
                            downloadedCount++;
                            if (downloadedCount % 50 == 0 || downloadedCount == allFiles.Count)
                            {
                                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                                var speed = downloadedCount / Math.Max(elapsed, 1);
                                var remaining = allFiles.Count - downloadedCount;
                                var eta = remaining / Math.Max(speed, 1);
                                progress?.Report($"Downloading... {downloadedCount}/{allFiles.Count} files " +
                                               $"({speed:F1} files/sec, ~{eta:F0}s remaining)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj)
                        {
                            failedCount++;
                        }
                        System.Diagnostics.Debug.WriteLine($"Error downloading file {file.Name}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                // Wait for all downloads to complete
                await Task.WhenAll(downloadTasks);

                var elapsedTime = (DateTime.UtcNow - startTime).TotalSeconds;
                progress?.Report($"✓ Successfully downloaded {downloadedCount}/{allFiles.Count} files " +
                               $"({downloadedCount / Math.Max(elapsedTime, 1):F1} files/sec) to:\n{targetPath}" +
                               (failedCount > 0 ? $"\n⚠ {failedCount} files failed to download" : ""));

                return downloadedCount > 0;
            }
            catch (HttpRequestException httpEx)
            {
                var errorMsg = $"Network error: {httpEx.Message}";
                if (httpEx.InnerException != null)
                {
                    errorMsg += $"\nDetails: {httpEx.InnerException.Message}";
                }
                progress?.Report($"✗ {errorMsg}");
                System.Diagnostics.Debug.WriteLine($"PBE1DownloadService HTTP error: {httpEx.Message}\n{httpEx.StackTrace}");
                return false;
            }
            catch (TaskCanceledException)
            {
                progress?.Report("✗ Request timeout. The download took too long. Please check your internet connection and try again.");
                System.Diagnostics.Debug.WriteLine("PBE1DownloadService: Request timeout");
                return false;
            }
            catch (Exception ex)
            {
                var errorMsg = $"Error: {ex.GetType().Name}\n{ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMsg += $"\nInner: {ex.InnerException.Message}";
                }
                progress?.Report($"✗ {errorMsg}");
                System.Diagnostics.Debug.WriteLine($"PBE1DownloadService error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }


        public void Dispose()
        {
            _httpClient?.Dispose();
            _httpClientHandler?.Dispose();
            
            foreach (var client in _downloadClients)
            {
                client?.Dispose();
            }
            _downloadClients.Clear();
        }

        private class GitHubItem
        {
            [JsonProperty("name")]
            public string Name { get; set; } = string.Empty;

            [JsonProperty("type")]
            public string Type { get; set; } = string.Empty;

            [JsonProperty("path")]
            public string Path { get; set; } = string.Empty;
        }

        private class GitHubTreeRoot
        {
            [JsonProperty("tree")]
            public List<GitHubTreeItemInternal> Tree { get; set; } = new List<GitHubTreeItemInternal>();
        }

        private class GitHubTreeItemInternal
        {
            [JsonProperty("path")]
            public string Path { get; set; } = string.Empty;
            [JsonProperty("type")]
            public string Type { get; set; } = string.Empty;
        }
    }
}

