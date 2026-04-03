using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace leeyez_kai.Models
{
    public class AppSettings
    {
        public bool WrapNavigation { get; set; } = true;
        public bool AutoSpreadCover { get; set; } = true; // 見開き時に最初のページを単独表示
        public bool RecursiveMedia { get; set; } = false;  // サブフォルダも含めて画像表示
        public bool AutoPlay { get; set; } = true;
        public float SpreadThreshold { get; set; } = 1.0f;
        public int MemoryLimitMB { get; set; } = 256; // キャッシュメモリ上限(MB)
        public int ThumbnailSize { get; set; } = 128;
        public int HoverPreviewSize { get; set; } = 320;
        public int SidebarFontSize { get; set; } = 9;
        public string Language { get; set; } = "ja";
        public string TreeSortMode { get; set; } = "Name";
        public bool TreeSortDescending { get; set; } = false;

        private static readonly string FilePath = AppPaths.GetPath("settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            }
            catch (Exception ex) { Logger.Log($"Failed to load settings: {ex.Message}"); }
            return new();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Logger.Log($"Failed to save settings: {ex.Message}"); }
        }
    }
}
