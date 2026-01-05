using System;
using System.IO;
using Newtonsoft.Json;

namespace ManifestDownloaderGUI.Services
{
    /// <summary>
    /// Service for saving and restoring user selections (server, patch, manifest)
    /// </summary>
    public class SelectionStateService
    {
        private readonly string _stateFile;

        public SelectionStateService(string appDataPath, string? customFileName = null)
        {
            _stateFile = Path.Combine(appDataPath, customFileName ?? "selection_state.json");
        }

        public void SaveState(string? server, string? patch, string? manifest)
        {
            try
            {
                var state = new SelectionState
                {
                    Server = server,
                    Patch = patch,
                    Manifest = manifest,
                    SavedAt = DateTime.UtcNow
                };

                var json = JsonConvert.SerializeObject(state, Formatting.Indented);
                File.WriteAllText(_stateFile, json);
                System.Diagnostics.Debug.WriteLine($"Selection state saved: Server={server}, Patch={patch}, Manifest={manifest}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving selection state: {ex.Message}");
            }
        }

        public SelectionState? LoadState()
        {
            try
            {
                if (!File.Exists(_stateFile))
                    return null;

                var json = File.ReadAllText(_stateFile);
                var state = JsonConvert.DeserializeObject<SelectionState>(json);
                return state;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading selection state: {ex.Message}");
                return null;
            }
        }

        public void ClearState()
        {
            try
            {
                if (File.Exists(_stateFile))
                {
                    File.Delete(_stateFile);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing selection state: {ex.Message}");
            }
        }
    }

    public class SelectionState
    {
        [JsonProperty("server")]
        public string? Server { get; set; }

        [JsonProperty("patch")]
        public string? Patch { get; set; }

        [JsonProperty("manifest")]
        public string? Manifest { get; set; }

        [JsonProperty("savedAt")]
        public DateTime SavedAt { get; set; }
    }
}






