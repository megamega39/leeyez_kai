using System.Collections.Generic;

namespace leeyez_kai.Models
{
    public class HistoryEntry
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string EntryType { get; set; } = "folder";
        public long LastAccessedTicks { get; set; }
    }

    public class HistoryData
    {
        public List<HistoryEntry> Entries { get; set; } = new();
    }
}
