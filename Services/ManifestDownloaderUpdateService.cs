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
    public class ManifestDownloaderUpdateService : IDisposable
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/Morilli/ManifestDownloader/releases/latest";
        public const string ReleasePageUrl = "https://github.com/Morilli/ManifestDownloader/releases/latest";

        private readonly HttpClient _httpClient;
        private readonly ConfigService _configService;
        private readonly string _toolsDir;
        private readonly string _exePath;

        public ManifestDownloaderUpdateService(ConfigService configService)
        {
            _configService = configService;
            _toolsDir = Path.Combine(App.AppDataPath, "Tools");
            _exePath = Path.Combine(_toolsDir, "ManifestDownloader.exe");

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ManifestDownloaderGUI-Updater");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

            var token = _configService.GetGitHubToken();
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        public string ExePath => _exePath;
        public bool ExeExists => File.Exists(_exePath);

        public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
        {
            var result = new UpdateCheckResult
            {
                InstalledVersion = _configService.GetManifestDownloaderVersion(),
                ExeExists = ExeExists
            };

            try
            {
                var response = await _httpClient.GetAsync(LatestReleaseApi, ct);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync(ct);
                var release = JsonConvert.DeserializeObject<GitHubRelease>(json);

                if (release == null || string.IsNullOrEmpty(release.TagName))
                {
                    result.Error = "Could not parse GitHub release response.";
                    return result;
                }

                var asset = release.Assets?.FirstOrDefault(a =>
                                a.Name.Equals("ManifestDownloader.exe", StringComparison.OrdinalIgnoreCase))
                            ?? release.Assets?.FirstOrDefault(a =>
                                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (asset == null || string.IsNullOrEmpty(asset.DownloadUrl))
                {
                    result.Error = "Latest release contains no .exe asset.";
                    return result;
                }

                result.LatestVersion = release.TagName;
                result.LatestReleaseName = release.Name;
                result.DownloadUrl = asset.DownloadUrl;
                result.AssetSize = asset.Size;
                result.IsUpdateAvailable = !ExeExists ||
                    !string.Equals(result.InstalledVersion, release.TagName, StringComparison.OrdinalIgnoreCase);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        public async Task<bool> DownloadAsync(
            UpdateCheckResult update,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(update.DownloadUrl))
                throw new InvalidOperationException("No download URL in update info.");

            Directory.CreateDirectory(_toolsDir);
            var tempPath = _exePath + ".new";

            using (var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;

                using var src = await response.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await dst.WriteAsync(buffer, 0, n, ct);
                    read += n;
                    progress?.Report(new DownloadProgress
                    {
                        BytesRead = read,
                        TotalBytes = totalBytes
                    });
                }
            }

            if (File.Exists(_exePath))
            {
                try { File.Delete(_exePath); }
                catch (IOException)
                {
                    var backup = _exePath + ".old";
                    try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                    File.Move(_exePath, backup);
                }
            }
            File.Move(tempPath, _exePath);

            _configService.SaveManifestDownloaderVersion(update.LatestVersion ?? string.Empty);
            return true;
        }

        public void Dispose() => _httpClient.Dispose();

        private class GitHubRelease
        {
            [JsonProperty("tag_name")] public string TagName { get; set; } = string.Empty;
            [JsonProperty("name")] public string? Name { get; set; }
            [JsonProperty("assets")] public List<GitHubAsset>? Assets { get; set; }
        }

        private class GitHubAsset
        {
            [JsonProperty("name")] public string Name { get; set; } = string.Empty;
            [JsonProperty("browser_download_url")] public string DownloadUrl { get; set; } = string.Empty;
            [JsonProperty("size")] public long Size { get; set; }
        }
    }

    public class UpdateCheckResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? InstalledVersion { get; set; }
        public string? LatestVersion { get; set; }
        public string? LatestReleaseName { get; set; }
        public string? DownloadUrl { get; set; }
        public long AssetSize { get; set; }
        public bool ExeExists { get; set; }
        public bool IsUpdateAvailable { get; set; }
    }

    public class DownloadProgress
    {
        public long BytesRead { get; set; }
        public long TotalBytes { get; set; }
        public double Percentage => TotalBytes > 0 ? (double)BytesRead / TotalBytes * 100.0 : 0;
    }
}
