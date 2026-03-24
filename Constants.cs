using System;
using System.Collections.Generic;
using System.IO;

namespace leeyez_kai
{
    public static class FileExtensions
    {
        // HashSetでO(1)検索
        public static readonly HashSet<string> Archive = new(StringComparer.OrdinalIgnoreCase)
            { ".zip", ".cbz", ".rar", ".cbr", ".7z", ".7zip", ".tar", ".gz", ".bz2", ".xz", ".iso", ".lzh", ".lha", ".lzma" };
        public static readonly HashSet<string> Image = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".avif", ".tiff", ".ico" };
        public static readonly HashSet<string> Video = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv", ".m4v", ".ogv" };
        public static readonly HashSet<string> Audio = new(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".wav", ".ogg", ".flac", ".m4a", ".ac3", ".aac" };

        public static bool IsImage(string ext) => Image.Contains(ext);
        public static bool IsVideo(string ext) => Video.Contains(ext);
        public static bool IsAudio(string ext) => Audio.Contains(ext);
        public static bool IsMedia(string ext) => Video.Contains(ext) || Audio.Contains(ext);
        public static bool IsArchive(string ext) => Archive.Contains(ext);
        public static bool IsViewable(string ext) => IsImage(ext) || IsMedia(ext);

        public static string GetExt(string name) => Path.GetExtension(name).ToLowerInvariant();
    }

    public static class AppConstants
    {
        public const int ImageCacheSize = 50;
        public const int PrefetchCount = 10;
        public const int DebounceMs = 16; // ~60fps
        public const int AutoSaveMs = 500;
        public const int FileStreamBuffer = 81920;
        public const int ZoomStepPercent = 25;
        public const float SpreadThreshold = 1.0f; // 縦横比がこの値以下なら縦向き（見開き対象）
        public const int ZoomMin = 25;
        public const int ZoomMax = 1600;
        public const int MaxDecodeWidth = 3840;
        public const int MaxDecodeHeight = 2160;
    }
}
