using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ManifestDownloaderGUI.Services;

namespace ManifestDownloaderGUI.Services
{
    /// <summary>
    /// Service to read manifest files from local filesystem instead of GitHub API
    /// Useful for servers like PBE1 that have more than 1000 files
    /// </summary>
    public class LocalFileService
    {
        private readonly string _localBasePath;
        private const string PBE1LocalPath = "LoL/PBE1/windows/lol-game-client";

        public LocalFileService(string localBasePath)
        {
            _localBasePath = localBasePath;
        }

        /// <summary>
        /// Check if local filesystem path exists for a given server and is complete
        /// </summary>
        public bool IsLocalPathAvailable(string game, string server, string osType)
        {
            // Currently only support PBE1 with local filesystem
            if (server != "PBE1")
                return false;

            var localPath = Path.Combine(_localBasePath, PBE1LocalPath);
            if (!Directory.Exists(localPath))
                return false;

            // Check if the folder has a reasonable number of files (PBE1 should have 1000+ files)
            var files = Directory.GetFiles(localPath, "*.txt", SearchOption.TopDirectoryOnly);
            if (files.Length < 100)
            {
                System.Diagnostics.Debug.WriteLine($"LocalFileService: Local path exists but has only {files.Length} files, might be incomplete");
                return false;
            }

            // Check if we have recent patches (15.x) - if not, files might be outdated
            var hasRecentPatches = files.Any(f =>
            {
                var fileName = Path.GetFileName(f);
                return fileName.StartsWith("15.");
            });

            if (!hasRecentPatches && files.Length > 0)
            {
                System.Diagnostics.Debug.WriteLine($"LocalFileService: Local path exists but no 15.x patches found. Files might be outdated.");
                // Still return true but will log a warning - user can re-download if needed
            }

            return true;
        }

        /// <summary>
        /// Get all patches from local filesystem
        /// </summary>
        public Task<List<string>> GetPatchesAsync(string game, string server, string osType)
        {
            if (!IsLocalPathAvailable(game, server, osType))
                return Task.FromResult<List<string>>(new List<string>());

            var patches = new HashSet<string>();
            var patchPattern = new Regex(@"^(\d+\.\d+)");
            var localPath = Path.Combine(_localBasePath, PBE1LocalPath);

            try
            {
                System.Diagnostics.Debug.WriteLine($"LocalFileService: Reading patches from local path: {localPath}");

                if (!Directory.Exists(localPath))
                {
                    System.Diagnostics.Debug.WriteLine($"LocalFileService: Local path does not exist: {localPath}");
                    return Task.FromResult<List<string>>(new List<string>());
                }

                var txtFiles = Directory.GetFiles(localPath, "*.txt", SearchOption.TopDirectoryOnly);

                System.Diagnostics.Debug.WriteLine($"LocalFileService: Found {txtFiles.Length} .txt files in local directory");

                foreach (var file in txtFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var match = patchPattern.Match(fileName);
                    if (match.Success)
                    {
                        var patchVersion = match.Groups[1].Value;
                        patches.Add(patchVersion);
                    }
                }

                // Sort in descending order (newest first)
                var sortedPatches = patches
                    .OrderByDescending(p =>
                    {
                        try { return new Version(p); }
                        catch { return new Version(0, 0); }
                    })
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"LocalFileService: Extracted {sortedPatches.Count} unique patches from local filesystem");
                
                if (sortedPatches.Count > 0)
                {
                    var highestPatch = sortedPatches.First();
                    System.Diagnostics.Debug.WriteLine($"LocalFileService: Highest patch version: {highestPatch}");
                    
                    // Check if we have patches up to at least 15.24
                    var hasRecentPatches = sortedPatches.Any(p => p.StartsWith("15."));
                    if (!hasRecentPatches)
                    {
                        System.Diagnostics.Debug.WriteLine($"LocalFileService: WARNING - No 15.x patches found. Highest is {highestPatch}. Consider re-downloading PBE1 folder.");
                    }
                }

                return Task.FromResult(sortedPatches);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalFileService: Error reading patches from local path: {ex.Message}");
                return Task.FromResult<List<string>>(new List<string>());
            }
        }

        /// <summary>
        /// Get all manifests for a specific patch from local filesystem
        /// </summary>
        public Task<List<ManifestInfo>> GetManifestsForPatchAsync(string game, string server, string osType, string patch)
        {
            if (!IsLocalPathAvailable(game, server, osType))
                return Task.FromResult<List<ManifestInfo>>(new List<ManifestInfo>());

            var localPath = Path.Combine(_localBasePath, PBE1LocalPath);
            var patchPattern = new Regex($"^{Regex.Escape(patch)}\\.");

            try
            {
                System.Diagnostics.Debug.WriteLine($"LocalFileService: Reading manifests for patch {patch} from local path: {localPath}");

                if (!Directory.Exists(localPath))
                {
                    System.Diagnostics.Debug.WriteLine($"LocalFileService: Local path does not exist: {localPath}");
                    return Task.FromResult<List<ManifestInfo>>(new List<ManifestInfo>());
                }

                var txtFiles = Directory.GetFiles(localPath, "*.txt", SearchOption.TopDirectoryOnly)
                    .Where(file => patchPattern.IsMatch(Path.GetFileName(file)))
                    .Select(file => new ManifestInfo
                    {
                        Name = Path.GetFileName(file),
                        Path = Path.GetRelativePath(_localBasePath, file).Replace('\\', '/'),
                        DownloadUrl = null // Local files don't have download URLs
                    })
                    .OrderBy(m => m.Name)
                    .DistinctBy(m => m.Name) // Remove duplicates by name
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"LocalFileService: Found {txtFiles.Count} manifests for patch {patch}");

                return Task.FromResult(txtFiles);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalFileService: Error reading manifests from local path: {ex.Message}");
                return Task.FromResult<List<ManifestInfo>>(new List<ManifestInfo>());
            }
        }

        /// <summary>
        /// Get manifest URL from local .txt file content
        /// </summary>
        public async Task<string?> GetManifestUrlAsync(string manifestFilePath)
        {
            try
            {
                var fullPath = Path.Combine(_localBasePath, manifestFilePath);
                
                if (!File.Exists(fullPath))
                {
                    System.Diagnostics.Debug.WriteLine($"LocalFileService: Manifest file not found: {fullPath}");
                    return null;
                }

                var content = await File.ReadAllTextAsync(fullPath);
                var manifestUrl = content.Trim();
                
                return manifestUrl.StartsWith("http") ? manifestUrl : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LocalFileService: Error reading manifest URL from file: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the expected local path for PBE1
        /// </summary>
        public static string GetPBE1ExpectedPath(string basePath)
        {
            return Path.Combine(basePath, "LoL", "PBE1", "windows", "lol-game-client");
        }
    }
}

