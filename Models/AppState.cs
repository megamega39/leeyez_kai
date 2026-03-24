using System.Collections.Generic;

namespace leeyez_kai.Models
{
    public class AppState
    {
        public string LastPath { get; set; } = string.Empty;
        public string LastSelectedFileName { get; set; } = string.Empty;
        /// <summary>表示中のファイルのフルパス（書庫内パス含む）</summary>
        public string LastViewingFile { get; set; } = string.Empty;
        /// <summary>表示中ファイルのインデックス</summary>
        public int LastFileIndex { get; set; } = -1;

        public int? WindowWidth { get; set; }
        public int? WindowHeight { get; set; }
        public int? WindowTop { get; set; }
        public int? WindowLeft { get; set; }
        public int WindowState { get; set; } = 0; // 0=Normal, 1=Minimized, 2=Maximized
        public int SidebarWidth { get; set; } = 300;
        public List<string> Favorites { get; set; } = new();
    }
}
