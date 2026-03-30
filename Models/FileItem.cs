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

        private string? _ext;
        public string Ext => _ext ??= FileExtensions.GetExt(Name);
        public bool IsImage => FileExtensions.IsImage(Ext);
        public bool IsMedia => FileExtensions.IsMedia(Ext);
        public bool IsViewable => FileExtensions.IsViewable(Ext);
        public bool IsArchiveExt => FileExtensions.IsArchive(Ext);

        public string SizeString =>
            IsDirectory ? "" :
            Size < 1024 ? $"{Size} B" :
            Size < 1024 * 1024 ? $"{Size / 1024.0:F1} KB" :
            Size < 1024L * 1024 * 1024 ? $"{Size / (1024.0 * 1024):F1} MB" :
            $"{Size / (1024.0 * 1024 * 1024):F2} GB";

        public static FileItem FromFileInfo(FileInfo fi)
        {
            var item = new FileItem
            {
                Name = fi.Name,
                FullPath = fi.FullName,
                Size = fi.Length,
                LastModified = fi.LastWriteTime,
                IsDirectory = false,
            };
            item.IsArchiveFile = item.IsArchiveExt;
            item.DisplayType = string.IsNullOrEmpty(item.Ext) ? "ファイル" : item.Ext.TrimStart('.').ToUpperInvariant();
            return item;
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
