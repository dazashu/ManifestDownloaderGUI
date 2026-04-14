using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ManifestDownloaderGUI.Services
{
    public class AppUpdateService : IDisposable
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/dazashu/ManifestDownloaderGUI/releases/latest";
        public const string ReleasePageUrl = "https://github.com/dazashu/ManifestDownloaderGUI/releases/latest";

        private readonly HttpClient _httpClient;
        private readonly ConfigService _configService;

        public AppUpdateService(ConfigService configService)
        {
            _configService = configService;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ManifestDownloaderGUI-SelfUpdater");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

            var token = _configService.GetGitHubToken();
            if (!string.IsNullOrEmpty(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
        }

        public static Version CurrentAppVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v ?? new Version(0, 0, 0, 0);
            }
        }

        public static string CurrentAppVersionString => CurrentAppVersion.ToString(3);

        public async Task<AppUpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
        {
            var result = new AppUpdateCheckResult
            {
                InstalledVersion = CurrentAppVersionString
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
                                a.Name.Equals("ManifestDownloaderGUI.exe", StringComparison.OrdinalIgnoreCase))
                            ?? release.Assets?.FirstOrDefault(a =>
                                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                if (asset == null || string.IsNullOrEmpty(asset.DownloadUrl))
                {
                    result.Error = "Latest release contains no .exe asset.";
                    return result;
                }

                result.LatestTag = release.TagName;
                result.LatestVersion = ParseVersionFromTag(release.TagName);
                result.DownloadUrl = asset.DownloadUrl;
                result.AssetSize = asset.Size;
                result.Success = true;

                result.IsUpdateAvailable = EvaluateIsUpdateAvailable(result.LatestTag, result.LatestVersion);
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private bool EvaluateIsUpdateAvailable(string latestTag, Version? latestVersion)
        {
            var acknowledgedTag = _configService.GetAcknowledgedAppVersion();

            // First-ever check on this install: trust whatever is running right now.
            // Save the latest tag as "acknowledged" so we never nag the user about a
            // release they are already on (e.g. they just downloaded it). Any future
            // release genuinely newer than this will still trigger a prompt.
            if (string.IsNullOrEmpty(acknowledgedTag))
            {
                _configService.SaveAcknowledgedAppVersion(latestTag);
                return false;
            }

            // Same tag they already dismissed/installed → never nag again.
            if (string.Equals(acknowledgedTag, latestTag, StringComparison.OrdinalIgnoreCase))
                return false;

            // Compare parsed versions of (acknowledged → latest). If latest is strictly
            // newer than what the user has acknowledged, it's a real new release.
            var acknowledgedVersion = ParseVersionFromTag(acknowledgedTag);
            if (acknowledgedVersion != null && latestVersion != null)
                return latestVersion > acknowledgedVersion;

            // Fall back to string comparison: any different tag = update available.
            return true;
        }

        public void AcknowledgeVersion(string tag)
        {
            if (!string.IsNullOrEmpty(tag))
                _configService.SaveAcknowledgedAppVersion(tag);
        }

        public async Task<string> DownloadAsync(
            AppUpdateCheckResult update,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(update.DownloadUrl))
                throw new InvalidOperationException("No download URL in update info.");

            var updatesDir = Path.Combine(App.AppDataPath, "Updates");
            Directory.CreateDirectory(updatesDir);
            var newExePath = Path.Combine(updatesDir, "ManifestDownloaderGUI.new.exe");
            if (File.Exists(newExePath))
            {
                try { File.Delete(newExePath); } catch { }
            }

            using (var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;

                using var src = await response.Content.ReadAsStreamAsync(ct);
                using var dst = new FileStream(newExePath, FileMode.Create, FileAccess.Write, FileShare.None);

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

            return newExePath;
        }

        public void LaunchUpdateAndExit(string newExePath)
        {
            var currentExe = Environment.ProcessPath
                             ?? Process.GetCurrentProcess().MainModule?.FileName
                             ?? throw new InvalidOperationException("Cannot determine current executable path.");

            var pid = Process.GetCurrentProcess().Id;
            var updatesDir = Path.Combine(App.AppDataPath, "Updates");
            Directory.CreateDirectory(updatesDir);
            var batPath = Path.Combine(updatesDir, "apply-update.bat");

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("setlocal");
            sb.AppendLine(":wait");
            sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul");
            sb.AppendLine("if %ERRORLEVEL%==0 (");
            sb.AppendLine("  timeout /t 1 /nobreak >nul");
            sb.AppendLine("  goto wait");
            sb.AppendLine(")");
            sb.AppendLine("timeout /t 1 /nobreak >nul");
            sb.AppendLine($"move /Y \"{newExePath}\" \"{currentExe}\"");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  echo Failed to replace executable. The new version is at:");
            sb.AppendLine($"  echo {newExePath}");
            sb.AppendLine("  pause");
            sb.AppendLine("  exit /b 1");
            sb.AppendLine(")");
            sb.AppendLine($"start \"\" \"{currentExe}\"");
            sb.AppendLine("(goto) 2>nul & del \"%~f0\"");

            File.WriteAllText(batPath, sb.ToString(), Encoding.ASCII);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C \"\"{batPath}\"\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WorkingDirectory = updatesDir
            };
            Process.Start(psi);
        }

        private static Version? ParseVersionFromTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var t = tag.Trim();
            if (t.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring(1);
            }
            var plusIdx = t.IndexOf('+');
            if (plusIdx > 0) t = t.Substring(0, plusIdx);
            var dashIdx = t.IndexOf('-');
            if (dashIdx > 0) t = t.Substring(0, dashIdx);
            return Version.TryParse(t, out var v) ? v : null;
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

    public class AppUpdateCheckResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? InstalledVersion { get; set; }
        public string? LatestTag { get; set; }
        public Version? LatestVersion { get; set; }
        public string? DownloadUrl { get; set; }
        public long AssetSize { get; set; }
        public bool IsUpdateAvailable { get; set; }
    }
}
