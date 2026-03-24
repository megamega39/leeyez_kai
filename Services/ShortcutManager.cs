using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace leeyez_kai.Services
{
    public class ShortcutManager
    {
        private Dictionary<string, Keys> _bindings = new();

        // アクション名一覧
        public static readonly (string id, string label, Keys defaultKey)[] AllActions = new[]
        {
            ("PrevPage", "前のページ", Keys.Left),
            ("NextPage", "次のページ", Keys.Right),
            ("FirstPage", "最初のページ", Keys.Home),
            ("LastPage", "最後のページ", Keys.End),
            ("GoBack", "戻る", Keys.Alt | Keys.Left),
            ("GoForward", "進む", Keys.Alt | Keys.Right),
            ("GoUp", "上の階層", Keys.Alt | Keys.Up),
            ("Refresh", "更新", Keys.F5),
            ("Help", "ヘルプ", Keys.F1),
            ("Fullscreen", "全画面切替", Keys.F11),
            ("FitWindow", "ウィンドウに合わせる", Keys.W),
            ("ZoomIn", "拡大", Keys.Control | Keys.Oemplus),
            ("ZoomOut", "縮小", Keys.Control | Keys.OemMinus),
            ("ZoomReset", "等倍", Keys.Control | Keys.D0),
            ("Binding", "綴じ方向切替", Keys.B),
            ("SingleView", "単頁表示", Keys.D1),
            ("SpreadView", "見開き表示", Keys.D2),
            ("AutoView", "自動見開き", Keys.D3),
        };

        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "leeyez", "shortcuts.json");

        public ShortcutManager()
        {
            ResetToDefault();
            Load();
        }

        public Keys GetKey(string actionId) =>
            _bindings.TryGetValue(actionId, out var key) ? key : Keys.None;

        public void SetKey(string actionId, Keys key)
        {
            _bindings[actionId] = key;
        }

        /// <summary>キーからアクションIDを検索</summary>
        public string? FindAction(Keys keyData)
        {
            foreach (var kv in _bindings)
            {
                if (kv.Value == keyData) return kv.Key;
            }
            return null;
        }

        public void ResetToDefault()
        {
            _bindings.Clear();
            foreach (var (id, _, defaultKey) in AllActions)
                _bindings[id] = defaultKey;
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);
                var saved = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                if (saved != null)
                {
                    foreach (var kv in saved)
                        _bindings[kv.Key] = (Keys)kv.Value;
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var data = _bindings.ToDictionary(kv => kv.Key, kv => (int)kv.Value);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static string KeyToString(Keys key)
        {
            var parts = new List<string>();
            if (key.HasFlag(Keys.Control)) parts.Add("Ctrl");
            if (key.HasFlag(Keys.Alt)) parts.Add("Alt");
            if (key.HasFlag(Keys.Shift)) parts.Add("Shift");

            var baseKey = key & Keys.KeyCode;
            if (baseKey != Keys.None && baseKey != Keys.ControlKey && baseKey != Keys.Menu && baseKey != Keys.ShiftKey)
            {
                var name = baseKey switch
                {
                    Keys.Oemplus => "+",
                    Keys.OemMinus => "-",
                    Keys.D0 => "0", Keys.D1 => "1", Keys.D2 => "2", Keys.D3 => "3",
                    Keys.D4 => "4", Keys.D5 => "5", Keys.D6 => "6", Keys.D7 => "7",
                    Keys.D8 => "8", Keys.D9 => "9",
                    Keys.Left => "←", Keys.Right => "→", Keys.Up => "↑", Keys.Down => "↓",
                    _ => baseKey.ToString()
                };
                parts.Add(name);
            }
            return string.Join(" + ", parts);
        }
    }
}
