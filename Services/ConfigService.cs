using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace ManifestDownloaderGUI.Services
{
    /// <summary>
    /// Service for managing application configuration, including GitHub token storage
    /// </summary>
    public class ConfigService
    {
        private readonly string _configPath;
        private readonly string _configFile;
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ManifestDownloaderGUI-Secret-Key-2024");

        public ConfigService(string appDataPath)
        {
            _configPath = appDataPath;
            _configFile = Path.Combine(_configPath, "config.json");
            Directory.CreateDirectory(_configPath);
        }

        /// <summary>
        /// Get the stored GitHub token (decrypted)
        /// </summary>
        public string? GetGitHubToken()
        {
            try
            {
                if (!File.Exists(_configFile))
                    return null;

                var configJson = File.ReadAllText(_configFile);
                var config = JsonConvert.DeserializeObject<AppConfig>(configJson);

                if (string.IsNullOrEmpty(config?.GitHubTokenEncrypted))
                    return null;

                // Decrypt the token
                var encryptedBytes = Convert.FromBase64String(config.GitHubTokenEncrypted);
                var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading GitHub token: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Save the GitHub token (encrypted)
        /// </summary>
        public void SaveGitHubToken(string token)
        {
            try
            {
                AppConfig config;

                // Load existing config or create new
                if (File.Exists(_configFile))
                {
                    var configJson = File.ReadAllText(_configFile);
                    config = JsonConvert.DeserializeObject<AppConfig>(configJson) ?? new AppConfig();
                }
                else
                {
                    config = new AppConfig();
                }

                // Encrypt the token
                if (!string.IsNullOrEmpty(token))
                {
                    var tokenBytes = Encoding.UTF8.GetBytes(token);
                    var encryptedBytes = ProtectedData.Protect(tokenBytes, _entropy, DataProtectionScope.CurrentUser);
                    config.GitHubTokenEncrypted = Convert.ToBase64String(encryptedBytes);
                }
                else
                {
                    config.GitHubTokenEncrypted = null;
                }

                // Save config
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configFile, json);
                System.Diagnostics.Debug.WriteLine("GitHub token saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving GitHub token: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Check if a GitHub token is configured
        /// </summary>
        public bool HasGitHubToken()
        {
            var token = GetGitHubToken();
            return !string.IsNullOrEmpty(token);
        }

        /// <summary>
        /// Clear the stored GitHub token
        /// </summary>
        public void ClearGitHubToken()
        {
            SaveGitHubToken(string.Empty);
        }
    }

    /// <summary>
    /// Application configuration model
    /// </summary>
    internal class AppConfig
    {
        [JsonProperty("githubTokenEncrypted")]
        public string? GitHubTokenEncrypted { get; set; }
    }
}






