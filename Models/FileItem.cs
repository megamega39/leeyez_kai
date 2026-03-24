using System;
using System.Drawing;
using System.IO;
using System.Linq;

namespace leeyez_kai.Models
{
    public class FileItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsArchiveFile { get; set; }
        public string DisplayType { get; set; } = string.Empty;
        public Icon? Icon { get; set; }
        public Image? Thumbnail { get; set; }

        public string SizeString =>
            IsDirectory ? "" :
            Size < 1024 ? $"{Size} B" :
            Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB" :
            Size < 1024L * 1024 * 1024 ? $"{Size / (1024.0 * 1024):F1} MB" :
            $"{Size / (1024.0 * 1024 * 1024):F2} GB";

        public static FileItem FromFileInfo(FileInfo fi)
        {
            var ext = fi.Extension.ToLowerInvariant();
            return new FileItem
            {
                Name = fi.Name,
                FullPath = fi.FullName,
                Size = fi.Length,
                LastModified = fi.LastWriteTime,
                IsDirectory = false,
                IsArchiveFile = FileExtensions.Archive.Contains(ext),
                DisplayType = string.IsNullOrEmpty(ext) ? "ファイル" : ext.TrimStart('.').ToUpperInvariant()
            };
        }

        public static FileItem FromDirectoryInfo(DirectoryInfo di)
        {
            return new FileItem
            {
                Name = di.Name,
                FullPath = di.FullName,
                Size = 0,
                LastModified = di.LastWriteTime,
                IsDirectory = true,
                IsArchiveFile = false,
                DisplayType = "フォルダ"
            };
        }
    }
}
