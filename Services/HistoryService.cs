using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using leeyez_kai.Models;

namespace leeyez_kai.Services
{
    public class HistoryService : IDisposable
    {
        private const int MaxEntries = 500;

        private static readonly string HistoryFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "leeyez",
            "history.json");

        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly object _lock = new();
        private List<HistoryEntry> _entries = new();
        private System.Threading.Timer? _saveTimer;
        private bool _dirty;

        public IReadOnlyList<HistoryEntry> Entries
        {
            get
            {
                lock (_lock)
                    return _entries.OrderByDescending(e => e.LastAccessedTicks).ToList();
            }
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        public void Load()
        {
            try
            {
                if (File.Exists(HistoryFile))
                {
                    var json = File.ReadAllText(HistoryFile);
                    var data = JsonSerializer.Deserialize<HistoryData>(json);
                    if (data?.Entries != null)
                    {
                        lock (_lock)
                            _entries = data.Entries;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load history: {ex.Message}");
            }
        }

        public void AddEntry(string path, string name, string entryType)
        {
            if (string.IsNullOrEmpty(path)) return;

            lock (_lock)
            {
                var existing = _entries.FirstOrDefault(e =>
                    e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    existing.LastAccessedTicks = DateTime.UtcNow.Ticks;
                    existing.Name = name;
                    existing.EntryType = entryType;
                }
                else
                {
                    _entries.Add(new HistoryEntry
                    {
                        Path = path,
                        Name = name,
                        EntryType = entryType,
                        LastAccessedTicks = DateTime.UtcNow.Ticks
                    });
                }

                // 上限超過時に古いものを削除
                if (_entries.Count > MaxEntries)
                {
                    _entries = _entries
                        .OrderByDescending(e => e.LastAccessedTicks)
                        .Take(MaxEntries)
                        .ToList();
                }

                _dirty = true;
            }

            ScheduleSave();
        }

        public void RemoveEntry(string path)
        {
            lock (_lock)
            {
                _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                _dirty = true;
            }
            ScheduleSave();
        }

        public void ClearAll()
        {
            lock (_lock)
            {
                _entries.Clear();
                _dirty = true;
            }
            ScheduleSave();
        }

        /// <summary>デバウンス付き非同期保存をスケジュール</summary>
        private void ScheduleSave()
        {
            _saveTimer?.Dispose();
            _saveTimer = new System.Threading.Timer(_ => SaveInternal(), null, 500, Timeout.Infinite);
        }

        /// <summary>即座に保存（アプリ終了時用）</summary>
        public void Save()
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
            SaveInternal();
        }

        private void SaveInternal()
        {
            HistoryData data;
            lock (_lock)
            {
                if (!_dirty) return;
                data = new HistoryData { Entries = new List<HistoryEntry>(_entries) };
                _dirty = false;
            }

            try
            {
                var dir = Path.GetDirectoryName(HistoryFile);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(data, _jsonOptions);

                // Atomic write: 一時ファイル→リネーム
                var tmpFile = HistoryFile + ".tmp";
                File.WriteAllText(tmpFile, json);
                File.Move(tmpFile, HistoryFile, true);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to save history: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }
    }
}
