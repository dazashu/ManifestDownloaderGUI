using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ManifestDownloaderGUI.Services
{
    /// <summary>
    /// Service for caching GitHub API data locally to avoid rate limiting
    /// </summary>
    public class CacheService
    {
        private readonly string _cacheBasePath;
        private const int CacheValidHours = 24; // Cache data for 24 hours

        public CacheService(string cachePath)
        {
            _cacheBasePath = cachePath;
            Directory.CreateDirectory(_cacheBasePath);
        }

        /// <summary>
        /// Get cached servers, or null if cache doesn't exist or is expired
        /// </summary>
        public CachedData<List<string>>? GetCachedServers(string game = "LoL")
        {
            var cacheFile = Path.Combine(_cacheBasePath, $"{game}_servers.json");
            return GetCachedData<List<string>>(cacheFile);
        }

        /// <summary>
        /// Save servers to cache
        /// </summary>
        public void SaveServers(List<string> servers, string game = "LoL")
        {
            var cacheFile = Path.Combine(_cacheBasePath, $"{game}_servers.json");
            SaveCachedData(cacheFile, servers);
        }

        /// <summary>
        /// Get cached patches for a server, or null if cache doesn't exist or is expired
        /// </summary>
        public CachedData<List<string>>? GetCachedPatches(string game, string server, string osType)
        {
            var cacheFile = Path.Combine(_cacheBasePath, $"{game}_{server}_{osType}_patches.json");
            var cached = GetCachedData<List<string>>(cacheFile);
            
            // Validate cache - check if it looks incomplete (e.g., missing recent patches)
            if (cached != null && cached.Data != null && cached.Data.Any())
            {
                var sorted = cached.Data
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Select(p =>
                    {
                        try { return new { Version = new Version(p), String = p }; }
                        catch { return null; }
                    })
                    .Where(v => v != null)
                    .OrderByDescending(v => v!.Version)
                    .ToList();
                
                if (sorted.Any())
                {
                    var highestPatch = sorted.First()!;
                    var now = DateTime.UtcNow;
                    var cacheAge = now - cached.CachedAt;
                    
                    // If highest patch is 14.x or lower and cache is older than 7 days, it might be incomplete
                    // Also check if we're in 2025 and highest patch is still 14.x (should be 15.x by now)
                    if (highestPatch.Version.Major < 15 && cacheAge.TotalDays > 7)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cache validation: Highest patch is {highestPatch.String} (age: {cacheAge.TotalDays:F1} days), might be incomplete. Consider refreshing.");
                    }
                }
            }
            
            return cached;
        }

        /// <summary>
        /// Save patches to cache
        /// </summary>
        public void SavePatches(List<string> patches, string game, string server, string osType)
        {
            var cacheFile = Path.Combine(_cacheBasePath, $"{game}_{server}_{osType}_patches.json");
            SaveCachedData(cacheFile, patches);
        }

        /// <summary>
        /// Get cached manifests for a patch, or null if cache doesn't exist or is expired
        /// </summary>
        public CachedData<List<ManifestInfo>>? GetCachedManifests(string game, string server, string osType, string patch)
        {
            var cacheFile = Path.Combine(_cacheBasePath, $"{game}_{server}_{osType}_{patch}_manifests.json");
            var cached = GetCachedData<List<ManifestInfo>>(cacheFile);
            
            // Validate cache - check for duplicates which indicate corruption
            if (cached != null && cached.Data != null)
            {
                var uniqueNames = cached.Data.Select(m => m.Name).Distinct().Count();
                var totalCount = cached.Data.Count;
                
                // If there are duplicates (more items than unique names), cache is corrupted
                if (totalCount > uniqueNames)
                {
                    System.Diagnostics.Debug.WriteLine($"Cache corruption detected: {cacheFile} has {totalCount} items but only {uniqueNames} unique names. Cache invalidated.");
                    try
                    {
                        File.Delete(cacheFile);
                    }
                    catch
                    {
                        // Ignore deletion errors
                    }
                    return null;
                }
            }
            
            return cached;
        }

        /// <summary>
        /// Save manifests to cache
        /// </summary>
        public void SaveManifests(List<ManifestInfo> manifests, string game, string server, string osType, string patch)
        {
            var cacheFile = Path.Combine(_cacheBasePath, $"{game}_{server}_{osType}_{patch}_manifests.json");
            SaveCachedData(cacheFile, manifests);
        }

        /// <summary>
        /// Get cached data from file if it exists and is still valid
        /// </summary>
        private CachedData<T>? GetCachedData<T>(string cacheFile)
        {
            try
            {
                if (!File.Exists(cacheFile))
                    return null;

                var content = File.ReadAllText(cacheFile);
                var cachedData = JsonConvert.DeserializeObject<CachedData<T>>(content);

                if (cachedData == null)
                    return null;

                // Check if cache is still valid
                var cacheAge = DateTime.UtcNow - cachedData.CachedAt;
                if (cacheAge.TotalHours > CacheValidHours)
                {
                    System.Diagnostics.Debug.WriteLine($"Cache expired for {cacheFile} (age: {cacheAge.TotalHours:F2} hours)");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"Cache hit for {cacheFile} (age: {cacheAge.TotalMinutes:F2} minutes)");
                return cachedData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading cache file {cacheFile}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save data to cache file
        /// </summary>
        private void SaveCachedData<T>(string cacheFile, T data)
        {
            try
            {
                var cachedData = new CachedData<T>
                {
                    Data = data,
                    CachedAt = DateTime.UtcNow
                };

                var content = JsonConvert.SerializeObject(cachedData, Formatting.Indented);
                File.WriteAllText(cacheFile, content);
                System.Diagnostics.Debug.WriteLine($"Cache saved to {cacheFile}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving cache file {cacheFile}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear all cache files
        /// </summary>
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(_cacheBasePath))
                {
                    var files = Directory.GetFiles(_cacheBasePath, "*.json");
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                    System.Diagnostics.Debug.WriteLine($"Cleared {files.Length} cache files");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear expired cache files
        /// </summary>
        public void ClearExpiredCache()
        {
            try
            {
                if (!Directory.Exists(_cacheBasePath))
                    return;

                var files = Directory.GetFiles(_cacheBasePath, "*.json");
                var deletedCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        var cachedData = JsonConvert.DeserializeObject<CachedData<object>>(content);

                        if (cachedData != null)
                        {
                            var cacheAge = DateTime.UtcNow - cachedData.CachedAt;
                            if (cacheAge.TotalHours > CacheValidHours)
                            {
                                File.Delete(file);
                                deletedCount++;
                            }
                        }
                    }
                    catch
                    {
                        // Ignore errors when checking individual files
                    }
                }

                if (deletedCount > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"Cleared {deletedCount} expired cache files");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing expired cache: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Container for cached data with timestamp
    /// </summary>
    public class CachedData<T>
    {
        [JsonProperty("data")]
        public T? Data { get; set; }

        [JsonProperty("cachedAt")]
        public DateTime CachedAt { get; set; }
    }
}



