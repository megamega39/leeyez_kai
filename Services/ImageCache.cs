using System;
using System.Collections.Generic;
using SkiaSharp;

namespace leeyez_kai.Services
{
    /// <summary>
    /// SKBitmap をキャッシュ（変換済みなので表示は即座）
    /// </summary>
    public class ImageCache : IDisposable
    {
        private int _maxEntries;
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly LinkedList<string> _lruOrder = new();
        private readonly object _lock = new();

        private class CacheEntry
        {
            public SKBitmap Bitmap { get; set; } = null!;
            public int OriginalWidth { get; set; }
            public int OriginalHeight { get; set; }
            public LinkedListNode<string> LruNode { get; set; } = null!;
        }

        public ImageCache(int maxEntries = 50)
        {
            _maxEntries = maxEntries;
        }

        public SKBitmap? Get(string key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                {
                    _lruOrder.Remove(entry.LruNode);
                    _lruOrder.AddFirst(entry.LruNode);
                    return entry.Bitmap;
                }
                return null;
            }
        }

        public (int w, int h) GetOriginalSize(string key)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(key, out var entry))
                    return (entry.OriginalWidth, entry.OriginalHeight);
                return (0, 0);
            }
        }

        /// <summary>キャッシュに追加。既にキーが存在する場合は false を返し bitmap を変更しない。</summary>
        public bool Put(string key, SKBitmap bitmap, int origW, int origH)
        {
            lock (_lock)
            {
                if (_cache.ContainsKey(key))
                {
                    // 既存を維持（表示中の参照を壊さない）。LRU更新のみ。
                    var existing = _cache[key];
                    _lruOrder.Remove(existing.LruNode);
                    _lruOrder.AddFirst(existing.LruNode);
                    return false;
                }

                while (_cache.Count >= _maxEntries && _lruOrder.Last != null)
                {
                    var evictKey = _lruOrder.Last.Value;
                    _lruOrder.RemoveLast();
                    if (_cache.TryGetValue(evictKey, out var evicted))
                    {
                        evicted.Bitmap.Dispose();
                        _cache.Remove(evictKey);
                    }
                }

                var node = _lruOrder.AddFirst(key);
                _cache[key] = new CacheEntry { Bitmap = bitmap, OriginalWidth = origW, OriginalHeight = origH, LruNode = node };
                return true;
            }
        }

        public bool Contains(string key)
        {
            lock (_lock) { return _cache.ContainsKey(key); }
        }

        public void SetMaxEntries(int max)
        {
            lock (_lock)
            {
                _maxEntries = max;
                while (_cache.Count > _maxEntries && _lruOrder.Last != null)
                {
                    var evictKey = _lruOrder.Last.Value;
                    _lruOrder.RemoveLast();
                    if (_cache.TryGetValue(evictKey, out var evicted))
                    {
                        evicted.Bitmap.Dispose();
                        _cache.Remove(evictKey);
                    }
                }
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                foreach (var entry in _cache.Values)
                    entry.Bitmap.Dispose();
                _cache.Clear();
                _lruOrder.Clear();
            }
        }

        public void Dispose() => Clear();
    }
}
