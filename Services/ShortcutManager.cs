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

        // アクション名一覧（labelKeyはi18nキー）
        public static readonly (string id, string labelKey, Keys defaultKey)[] AllActions = new[]
        {
            ("PrevPage", "sc.prevpage", Keys.Left),
            ("NextPage", "sc.nextpage", Keys.Right),
            ("FirstPage", "sc.firstpage", Keys.Home),
            ("LastPage", "sc.lastpage", Keys.End),
            ("GoBack", "sc.goback", Keys.Alt | Keys.Left),
            ("GoForward", "sc.goforward", Keys.Alt | Keys.Right),
            ("GoUp", "sc.goup", Keys.Alt | Keys.Up),
            ("Refresh", "sc.refresh", Keys.F5),
            ("Help", "sc.help", Keys.F1),
            ("Fullscreen", "sc.fullscreen", Keys.F11),
            ("FitWindow", "sc.fitwindow", Keys.W),
            ("ZoomIn", "sc.zoomin", Keys.Control | Keys.Oemplus),
            ("ZoomOut", "sc.zoomout", Keys.Control | Keys.OemMinus),
            ("ZoomReset", "sc.zoomreset", Keys.Control | Keys.D0),
            ("Binding", "sc.binding", Keys.B),
            ("SingleView", "sc.singleview", Keys.D1),
            ("SpreadView", "sc.spreadview", Keys.D2),
            ("AutoView", "sc.autoview", Keys.D3),
            ("PrevFolder", "sc.prevfolder", Keys.Up),
            ("NextFolder", "sc.nextfolder", Keys.Down),
            ("CopyImage", "sc.copyimage", Keys.Control | Keys.C),
            ("RotateCW", "sc.rotatecw", Keys.Control | Keys.R),
            ("RotateCCW", "sc.rotateccw", Keys.Control | Keys.Shift | Keys.R),
            ("ToggleBookshelf", "sc.togglebookshelf", Keys.C),
            ("ViewModeToggle", "sc.viewmodetoggle", Keys.V),
            ("SetBindingLTR", "sc.setbindingltr", Keys.L),
            ("SetBindingRTL", "sc.setbindingrtl", Keys.R),
            ("ToggleExpand", "sc.toggleexpand", Keys.Enter),
        };

        private static readonly string FilePath = AppPaths.GetPath("shortcuts.json");

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
            catch (Exception ex) { Logger.Log($"Failed to load shortcut bindings: {ex.Message}"); }
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
            catch (Exception ex) { Logger.Log($"Failed to save shortcut bindings: {ex.Message}"); }
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
