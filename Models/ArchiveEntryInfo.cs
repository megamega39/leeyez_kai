using System;

namespace leeyez_kai.Models
{
    public class ArchiveEntryInfo
    {
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime? LastWriteTime { get; set; }
        public bool IsFolder { get; set; }
    }
}
