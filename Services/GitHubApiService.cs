using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ManifestDownloaderGUI.Services
{
    public class GitHubApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly CacheService _cacheService = null!;
        private readonly LocalFileService? _localFileService;
        private const string GitHubApiBase = "https://api.github.com/repos/Morilli/riot-manifests/contents";
        private const string GitHubTreeApiBase = "https://api.github.com/repos/Morilli/riot-manifests/git/trees/master?recursive=1";
        
        private List<GitHubTreeItem>? _treeCache;
        private DateTime _cacheTimestamp = DateTime.MinValue;
        private readonly TimeSpan _treeCacheExpiry = TimeSpan.FromMinutes(30);

        public GitHubApiService(CacheService? cacheService = null, string? githubToken = null)
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ManifestDownloader-GUI");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.Timeout = TimeSpan.FromSeconds(60); // Increased timeout for large API calls
            _cacheService = cacheService ?? new CacheService(App.CachePath);
            
            // Initialize local file service for PBE1
            _localFileService = new LocalFileService(App.LocalRepoPath);
            
            // Set GitHub token if provided
            if (!string.IsNullOrEmpty(githubToken))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", githubToken);
                System.Diagnostics.Debug.WriteLine("GitHub token configured in API service");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No GitHub token configured - using default rate limit");
            }
            
            // Clear expired cache on startup
            _cacheService.ClearExpiredCache();
        }

        public GitHubApiService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ManifestDownloader-GUI");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<List<string>> GetServersAsync(string game = "LoL", bool forceRefresh = false)
        {
            // Try cache first if not forcing refresh
            if (!forceRefresh)
            {
                var cached = _cacheService.GetCachedServers(game);
                if (cached != null && cached.Data != null && cached.Data.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"Using cached servers for {game}");
                    return cached.Data;
                }
            }

            try
            {
                var url = $"{GitHubApiBase}/{game}";
                System.Diagnostics.Debug.WriteLine($"Fetching servers from GitHub API: {url}");
                
                var response = await _httpClient.GetAsync(url);
                
                // Check for rate limit error
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (errorContent.Contains("rate limit") || errorContent.Contains("API rate limit"))
                    {
                        // Try to use cache as fallback even if expired
                        var cached = _cacheService.GetCachedServers(game);
                        if (cached != null && cached.Data != null && cached.Data.Any())
                        {
                            System.Diagnostics.Debug.WriteLine("Rate limit hit, using expired cache as fallback");
                            return cached.Data;
                        }
                        
                        throw new HttpRequestException(
                            "GitHub API rate limit exceeded. Please wait a few minutes (usually 60 seconds) and try again.\n\n" +
                            "Tip: You can use a GitHub personal access token to increase your rate limit from 60 to 5000 requests per hour.",
                            null,
                            response.StatusCode);
                    }
                }

                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Response received, length: {content.Length}");
                
                var items = JsonConvert.DeserializeObject<List<GitHubItem>>(content);

                if (items == null)
                {
                    System.Diagnostics.Debug.WriteLine("Deserialized items is null");
                    return new List<string>();
                }

                var servers = items
                    .Where(item => item.Type == "dir")
                    .Select(item => item.Name)
                    .OrderBy(name => name)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Found {servers.Count} servers: {string.Join(", ", servers)}");
                
                // Save to cache
                _cacheService.SaveServers(servers, game);
                
                return servers;
            }
            catch (HttpRequestException)
            {
                // Re-throw HTTP exceptions (including rate limit) immediately
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching servers: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw; // Re-throw to allow caller to handle
            }
        }

        private async Task<List<GitHubItem>> GetAllItemsWithPaginationAsync(string path)
        {
            // First, try to use the Tree API cache if available or appropriate
            try
            {
                var treeItems = await GetItemsFromTreeAsync(path);
                if (treeItems != null && treeItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"Successfully retrieved {treeItems.Count} items for {path} using Trees API");
                    return treeItems;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tree API failed, falling back to Contents API: {ex.Message}");
            }

            // Fallback to Contents API (Legacy/Limited)
            // Note: GitHub's Contents API is limited to 1000 items and doesn't actually support pagination for directory listings.
            var allItems = new List<GitHubItem>();
            var url = $"{GitHubApiBase}/{path}";
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"Falling back to Contents API for path: {path}");
                var response = await _httpClient.GetAsync(url);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (errorContent.Contains("rate limit"))
                    {
                        throw new HttpRequestException("GitHub API rate limit exceeded.", null, response.StatusCode);
                    }
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var items = JsonConvert.DeserializeObject<List<GitHubItem>>(content);
                
                if (items != null)
                {
                    allItems.AddRange(items);
                    if (items.Count >= 1000)
                    {
                        System.Diagnostics.Debug.WriteLine("WARNING: Contents API result truncated at 1000 items. Tree API should have been used.");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Contents API fallback also failed: {ex.Message}");
                throw;
            }

            return allItems;
        }

        private async Task<List<GitHubItem>> GetItemsFromTreeAsync(string path)
        {
            await EnsureTreeCacheAsync();

            if (_treeCache == null) return new List<GitHubItem>();

            // Normalize path for matching (GitHub uses forward slashes)
            var normalizedPath = path.Replace('\\', '/').TrimEnd('/');
            var searchPrefix = normalizedPath + "/";

            var items = _treeCache
                .Where(item => item.Path.StartsWith(searchPrefix))
                .Select(item => {
                    // Get the name by taking the part after the prefix and excluding subdirectories
                    var relativePath = item.Path.Substring(searchPrefix.Length);
                    if (relativePath.Contains('/')) return null; // It's in a subdirectory

                    return new GitHubItem
                    {
                        Name = relativePath,
                        Path = item.Path,
                        Type = item.Type == "tree" ? "dir" : "file",
                        DownloadUrl = $"https://raw.githubusercontent.com/Morilli/riot-manifests/master/{item.Path}"
                    };
                })
                .Where(item => item != null)
                .Cast<GitHubItem>()
                .ToList();

            return items;
        }

        private async Task EnsureTreeCacheAsync(bool forceRefresh = false)
        {
            if (!forceRefresh && _treeCache != null && (DateTime.Now - _cacheTimestamp) < _treeCacheExpiry)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine("Fetching fresh Git Tree from GitHub...");
            try
            {
                var response = await _httpClient.GetAsync(GitHubTreeApiBase);
                
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    if (errorContent.Contains("rate limit"))
                    {
                        System.Diagnostics.Debug.WriteLine("Trees API Rate Limit Hit");
                        if (_treeCache != null) return; // Use existing cache if available
                        throw new HttpRequestException("GitHub API rate limit exceeded.", null, response.StatusCode);
                    }
                }

                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var treeResponse = JsonConvert.DeserializeObject<GitHubTreeResponse>(content);

                if (treeResponse != null && treeResponse.Tree != null)
                {
                    _treeCache = treeResponse.Tree;
                    _cacheTimestamp = DateTime.Now;
                    System.Diagnostics.Debug.WriteLine($"Cached {_treeCache.Count} items from Git Tree. Truncated: {treeResponse.Truncated}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Git Tree: {ex.Message}");
                if (_treeCache == null) throw; // Only re-throw if we have no cache at all
            }
        }

        public async Task<List<string>> GetPatchesAsync(string game, string server, string osType, bool forceRefresh = false)
        {
            // Check if local filesystem is available for this server (PBE1)
            if (_localFileService != null && _localFileService.IsLocalPathAvailable(game, server, osType))
            {
                System.Diagnostics.Debug.WriteLine($"Using local filesystem for {game}/{server}/{osType} patches");
                var localPatches = await _localFileService.GetPatchesAsync(game, server, osType);
                
                // Validate local patches - check if we have recent patches (15.x)
                if (localPatches != null && localPatches.Any())
                {
                    var hasRecentPatches = localPatches.Any(p => p.StartsWith("15."));
                    var highestPatch = localPatches
                        .OrderByDescending(p =>
                        {
                            try { return new Version(p); }
                            catch { return new Version(0, 0); }
                        })
                        .FirstOrDefault();
                    
                    System.Diagnostics.Debug.WriteLine($"Local patches: Found {localPatches.Count} patches, highest: {highestPatch}, has 15.x: {hasRecentPatches}");
                    
                    // If we don't have 15.x patches for PBE1, the local folder is incomplete - use API instead
                    if (!hasRecentPatches && server == "PBE1")
                    {
                        System.Diagnostics.Debug.WriteLine($"WARNING: Local PBE1 folder doesn't contain 15.x patches. Highest is {highestPatch}. Falling back to GitHub API.");
                        // Don't return local patches - continue to API fetch below
                    }
                    else
                    {
                        // Local patches are complete, use them
                        _cacheService.SavePatches(localPatches, game, server, osType);
                        return localPatches;
                    }
                }
                else
                {
                    // No local patches found, fall back to API
                    System.Diagnostics.Debug.WriteLine($"No local patches found, falling back to GitHub API");
                }
            }

            // Try cache first if not forcing refresh
            if (!forceRefresh)
            {
                var cached = _cacheService.GetCachedPatches(game, server, osType);
                if (cached != null && cached.Data != null && cached.Data.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"Using cached patches for {game}/{server}/{osType} - {cached.Data.Count} patches");
                    return cached.Data;
                }
                else if (cached != null && cached.Data != null && cached.Data.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Cache exists but is empty for {game}/{server}/{osType} - will fetch from API");
                    // Cache exists but is empty - this might mean the server has no patches
                    // Still try to fetch from API in case new patches were added
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"No cache found for {game}/{server}/{osType} - will fetch from API");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Force refresh requested for {game}/{server}/{osType} - will fetch from API");
            }

            var patches = new HashSet<string>();
            var patchPattern = new Regex(@"^(\d+\.\d+)");

            try
            {
                System.Diagnostics.Debug.WriteLine($"GetPatchesAsync: Fetching patches for game={game}, server={server}, osType={osType}");
                var path = $"{game}/{server}/{osType}/lol-game-client";
                
                System.Diagnostics.Debug.WriteLine($"GetPatchesAsync: Full path: {path}");
                System.Diagnostics.Debug.WriteLine($"GetPatchesAsync: Calling GetAllItemsWithPaginationAsync...");
                var items = await GetAllItemsWithPaginationAsync(path);
                System.Diagnostics.Debug.WriteLine($"GetPatchesAsync: Received {items.Count} total items from GitHub API");

                var txtFilesCount = 0;
                var processedPatches = new HashSet<string>(); // Track processed patches for logging
                
                foreach (var item in items)
                {
                    if (item.Type == "file" && item.Name.EndsWith(".txt"))
                    {
                        txtFilesCount++;
                        var match = patchPattern.Match(item.Name);
                        if (match.Success)
                        {
                            var patchVersion = match.Groups[1].Value;
                            var wasAdded = patches.Add(patchVersion); // HashSet.Add returns true if new item
                            
                            // Log first few patches and any 15.x patches to track pagination
                            if (wasAdded && (patches.Count <= 10 || patchVersion.StartsWith("15.")))
                            {
                                System.Diagnostics.Debug.WriteLine($"GetPatchesAsync: Found patch {patchVersion} from file {item.Name}");
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"GetPatchesAsync: Found {txtFilesCount} .txt files, extracted {patches.Count} unique patch versions");
                
                // Log highest patch version found to verify we got 15.24
                if (patches.Count > 0)
                {
                    var highestPatch = patches.OrderByDescending(p => 
                    {
                        try { return new Version(p); }
                        catch { return new Version(0, 0); }
                    }).First();
                    System.Diagnostics.Debug.WriteLine($"GetPatchesAsync: Highest patch version found: {highestPatch}");
                }

                // Sort in descending order (newest first)
                var sortedPatches = patches
                    .OrderByDescending(p => 
                    {
                        try { return new Version(p); }
                        catch { return new Version(0, 0); }
                    })
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Saving {sortedPatches.Count} patches to cache");
                // Save to cache
                _cacheService.SavePatches(sortedPatches, game, server, osType);

                return sortedPatches;
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("rate limit"))
            {
                System.Diagnostics.Debug.WriteLine($"Rate limit error when fetching patches: {httpEx.Message}");
                // Try to use cache as fallback even if expired
                var cached = _cacheService.GetCachedPatches(game, server, osType);
                if (cached != null && cached.Data != null && cached.Data.Any())
                {
                    System.Diagnostics.Debug.WriteLine("Rate limit hit, using expired cache as fallback");
                    return cached.Data;
                }
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching patches: {ex.GetType().Name}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // Try to use cache as fallback
                var cached = _cacheService.GetCachedPatches(game, server, osType);
                if (cached != null && cached.Data != null && cached.Data.Any())
                {
                    System.Diagnostics.Debug.WriteLine("Using expired cache as fallback due to error");
                    return cached.Data;
                }
                
                throw; // Re-throw to allow caller to handle
            }
        }

        public async Task<List<ManifestInfo>> GetManifestsForPatchAsync(string game, string server, string osType, string patch, bool forceRefresh = false)
        {
            // Check if local filesystem is available for this server (PBE1)
            if (_localFileService != null && _localFileService.IsLocalPathAvailable(game, server, osType))
            {
                System.Diagnostics.Debug.WriteLine($"Using local filesystem for {game}/{server}/{osType}/{patch} manifests");
                var localManifests = await _localFileService.GetManifestsForPatchAsync(game, server, osType, patch);
                
                // Cache the local manifests for consistency
                if (localManifests != null && localManifests.Any())
                {
                    _cacheService.SaveManifests(localManifests, game, server, osType, patch);
                }
                
                return localManifests ?? new List<ManifestInfo>();
            }

            // Try cache first if not forcing refresh
            if (!forceRefresh)
            {
                var cached = _cacheService.GetCachedManifests(game, server, osType, patch);
                if (cached != null && cached.Data != null && cached.Data.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"Using cached manifests for {game}/{server}/{osType}/{patch} - {cached.Data.Count} manifests");
                    return cached.Data;
                }
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"GetManifestsForPatchAsync: Fetching manifests for {game}/{server}/{osType} patch {patch}");
                var path = $"{game}/{server}/{osType}/lol-game-client";
                
                System.Diagnostics.Debug.WriteLine($"GetManifestsForPatchAsync: Path: {path}");
                var items = await GetAllItemsWithPaginationAsync(path);
                System.Diagnostics.Debug.WriteLine($"GetManifestsForPatchAsync: Retrieved {items.Count} total items from API");

                // Filter: only files ending with .txt that start with the patch number
                var patchPattern = new Regex($"^{Regex.Escape(patch)}\\.");
                System.Diagnostics.Debug.WriteLine($"GetManifestsForPatchAsync: Filtering with pattern: ^{Regex.Escape(patch)}\\.");

                var allTxtFiles = items.Where(item => item.Type == "file" && item.Name.EndsWith(".txt")).ToList();
                System.Diagnostics.Debug.WriteLine($"GetManifestsForPatchAsync: Found {allTxtFiles.Count} .txt files total");

                var manifests = allTxtFiles
                    .Where(item => patchPattern.IsMatch(item.Name))
                    .GroupBy(item => item.Name) // Group by name to remove duplicates
                    .Select(group => group.First()) // Take first item from each group
                    .Select(item => new ManifestInfo
                    {
                        Name = item.Name,
                        Path = item.Path,
                        DownloadUrl = item.DownloadUrl
                    })
                    .OrderBy(m => m.Name)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"GetManifestsForPatchAsync: Filtered to {manifests.Count} manifests matching patch {patch}");

                // Save to cache
                _cacheService.SaveManifests(manifests, game, server, osType, patch);

                return manifests;
            }
            catch (HttpRequestException httpEx) when (httpEx.Message.Contains("rate limit"))
            {
                // Try to use cache as fallback even if expired
                var cached = _cacheService.GetCachedManifests(game, server, osType, patch);
                if (cached != null && cached.Data != null && cached.Data.Any())
                {
                    System.Diagnostics.Debug.WriteLine("Rate limit hit, using expired cache as fallback");
                    return cached.Data;
                }
                throw;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching manifests for patch {patch}: {ex.Message}");
                
                // Try to use cache as fallback
                var cached = _cacheService.GetCachedManifests(game, server, osType, patch);
                if (cached != null && cached.Data != null && cached.Data.Any())
                {
                    System.Diagnostics.Debug.WriteLine("Using expired cache as fallback due to error");
                    return cached.Data;
                }
                
                return new List<ManifestInfo>();
            }
        }

        public async Task<string?> GetManifestUrlAsync(string manifestFileUrl)
        {
            try
            {
                // Check if this is a local file path (starts with "LoL/")
                if (manifestFileUrl.StartsWith("LoL/") && _localFileService != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Getting manifest URL from local file: {manifestFileUrl}");
                    return await _localFileService.GetManifestUrlAsync(manifestFileUrl);
                }
                
                // Otherwise, fetch from GitHub API
                var content = await _httpClient.GetStringAsync(manifestFileUrl);
                var manifestUrl = content.Trim();
                return manifestUrl.StartsWith("http") ? manifestUrl : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching manifest URL: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> DownloadManifestFileAsync(string manifestUrl, string savePath)
        {
            try
            {
                var response = await _httpClient.GetAsync(manifestUrl);
                response.EnsureSuccessStatusCode();

                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var content = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(savePath, content);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading manifest: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class GitHubItem
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("download_url")]
        public string? DownloadUrl { get; set; }
    }

    public class ManifestInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? DownloadUrl { get; set; }
    }

    public class GitHubTreeResponse
    {
        [JsonProperty("sha")]
        public string Sha { get; set; } = string.Empty;

        [JsonProperty("tree")]
        public List<GitHubTreeItem> Tree { get; set; } = new List<GitHubTreeItem>();

        [JsonProperty("truncated")]
        public bool Truncated { get; set; }
    }

    public class GitHubTreeItem
    {
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("mode")]
        public string Mode { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("sha")]
        public string Sha { get; set; } = string.Empty;

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; } = string.Empty;
    }
}
