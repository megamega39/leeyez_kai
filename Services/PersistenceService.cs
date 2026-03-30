using System;
using System.IO;
using System.Text.Json;
using leeyez_kai.Models;

namespace leeyez_kai.Services
{
    public static class PersistenceService
    {
        private static readonly string SettingsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "leeyez",
            "state.json");

        public static void SaveState(AppState state)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFile);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
                Logger.Log($"State saved: LastPath={state.LastPath}, LastViewingFile={state.LastViewingFile}, LastFileIndex={state.LastFileIndex}");
            }
            catch (Exception ex) { Logger.Log($"Failed to save state: {ex.Message}"); }
        }

        public static AppState? LoadState()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    return JsonSerializer.Deserialize<AppState>(json);
                }
            }
            catch (Exception ex) { Logger.Log($"Failed to load state: {ex.Message}"); }
            return null;
        }
    }
}
