using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkiaSharp;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai.Controls
{
    /// <summary>
    /// 仮想スクロール付きグリッドパネル
    /// 見えている範囲のサムネイルだけ生成・描画する
    /// </summary>
    public class VirtualGridPanel : Panel
    {
        private List<FileItem> _items = new();
        private readonly ConcurrentDictionary<string, SKBitmap> _thumbCache = new();
        private readonly LinkedList<string> _thumbCacheOrder = new(); // LRU順序管理
        private readonly object _thumbCacheLock = new(); // _thumbCacheOrder保護用
        private CancellationTokenSource? _thumbCts;
        private Func<FileItem, Stream?>? _getFileStream;
        private volatile bool _invalidatePending;

        private int _thumbSize = 128;
        private int _padding = 8;
        private int _colCount = 1;
        private int _rowCount;
        private int _scrollOffset;
        private int _selectedIndex = -1;

        private static readonly Color SelBg = Color.FromArgb(0xFF, 0xC0, 0xC0);
        private static readonly Color HoverBg = Color.FromArgb(0xE0, 0xE8, 0xFF);
        private static readonly SolidBrush SelBrush = new(SelBg);
        private static readonly SolidBrush HoverBrush = new(HoverBg);
        private static readonly SolidBrush PlaceholderBrush = new(Color.FromArgb(0xD0, 0xD0, 0xD0));
        private static readonly SolidBrush NameBgBrush = new(Color.FromArgb(180, 255, 255, 255));
        private static readonly SolidBrush ScrollBarBrush = new(Color.FromArgb(100, 0, 0, 0));

        private int _hoverIndex = -1;

        public event Action<FileItem>? ItemSelected;
        public event Action<FileItem>? ItemDoubleClicked;

        public VirtualGridPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(0xEB, 0xF4, 0xFF);
            AutoScroll = false;

            MouseWheel += OnMouseWheel;
            MouseClick += OnMouseClick;
            MouseDoubleClick += OnMouseDoubleClick;
            MouseMove += OnMouseMove;
            MouseLeave += (s, e) => { if (_hoverIndex >= 0) { _hoverIndex = -1; Invalidate(); } };
            Resize += (s, e) => { CalcLayout(); Invalidate(); LoadVisibleThumbs(); };
        }

        public void SetFileStreamProvider(Func<FileItem, Stream?> provider) => _getFileStream = provider;

        public void SetItems(List<FileItem> items)
        {
            _thumbCts?.Cancel();
            _items = items;
            _scrollOffset = 0;
            _selectedIndex = -1;
            CalcLayout();
            Invalidate();
            LoadVisibleThumbs();
        }

        public void SelectByPath(string path)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].FullPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                {
                    _selectedIndex = i;
                    EnsureVisible(i);
                    Invalidate();
                    LoadVisibleThumbs();
                    return;
                }
            }
        }

        public int SelectedIndex => _selectedIndex;

        private void CalcLayout()
        {
            int cellSize = _thumbSize + _padding;
            _colCount = Math.Max(1, (Width - _padding) / cellSize);
            _rowCount = (_items.Count + _colCount - 1) / _colCount;
        }

        private int TotalHeight => _rowCount * (_thumbSize + _padding) + _padding;
        private int VisibleRows => (Height / (_thumbSize + _padding)) + 2;

        private int FirstVisibleRow => _scrollOffset / (_thumbSize + _padding);
        private int FirstVisibleIndex => FirstVisibleRow * _colCount;
        private int LastVisibleIndex => Math.Min(_items.Count, (FirstVisibleRow + VisibleRows) * _colCount);

        private Rectangle GetCellRect(int index)
        {
            int col = index % _colCount;
            int row = index / _colCount;
            int x = _padding + col * (_thumbSize + _padding);
            int y = _padding + row * (_thumbSize + _padding) - _scrollOffset;
            return new Rectangle(x, y, _thumbSize, _thumbSize);
        }

        private int HitTest(Point pt)
        {
            int col = (pt.X - _padding) / (_thumbSize + _padding);
            int row = (pt.Y + _scrollOffset - _padding) / (_thumbSize + _padding);
            if (col < 0 || col >= _colCount || row < 0) return -1;
            int idx = row * _colCount + col;
            return idx < _items.Count ? idx : -1;
        }

        private void EnsureVisible(int index)
        {
            var rect = GetCellRect(index);
            if (rect.Top < 0)
                _scrollOffset += rect.Top - _padding;
            else if (rect.Bottom > Height)
                _scrollOffset += rect.Bottom - Height + _padding;
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, TotalHeight - Height));
        }

        #region Paint

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.Clear(BackColor);

            int first = FirstVisibleIndex;
            int last = LastVisibleIndex;

            for (int i = first; i < last; i++)
            {
                var rect = GetCellRect(i);
                if (rect.Bottom < 0 || rect.Top > Height) continue;

                // 背景
                if (i == _selectedIndex)
                    g.FillRectangle(SelBrush, rect);
                else if (i == _hoverIndex)
                    g.FillRectangle(HoverBrush, rect);

                // サムネイル
                var item = _items[i];
                if (_thumbCache.TryGetValue(item.FullPath, out var thumb))
                {
                    var imgRect = FitRect(thumb.Width, thumb.Height, rect);
                    BlitSKBitmap(g, thumb, imgRect);
                }
                else
                {
                    g.FillRectangle(PlaceholderBrush, rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
                }

                // ファイル名（下部）
                var nameRect = new Rectangle(rect.X, rect.Bottom - 18, rect.Width, 18);
                g.FillRectangle(NameBgBrush, nameRect);
                TextRenderer.DrawText(g, item.Name, Font, nameRect, Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            // スクロールバー
            if (TotalHeight > Height)
            {
                int barHeight = Math.Max(20, Height * Height / TotalHeight);
                int barY = (int)((float)_scrollOffset / TotalHeight * Height);
                g.FillRectangle(ScrollBarBrush, Width - 8, barY, 6, barHeight);
            }
        }

        private static Rectangle FitRect(int imgW, int imgH, Rectangle area)
        {
            float scale = Math.Min((float)(area.Width - 8) / imgW, (float)(area.Height - 24) / imgH);
            int w = (int)(imgW * scale), h = (int)(imgH * scale);
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - 24 - h) / 2, w, h);
        }

        // Win32 API for StretchDIBits
        [DllImport("gdi32.dll")]
        private static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int wDest, int hDest,
            int xSrc, int ySrc, int wSrc, int hSrc, IntPtr lpBits, ref BITMAPINFO lpBmi, uint iUsage, uint dwRop);
        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int mode);
        private const int HALFTONE = 4;
        private const uint DIB_RGB_COLORS = 0;
        private const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize; public int biWidth; public int biHeight;
            public ushort biPlanes; public ushort biBitCount; public uint biCompression;
            public uint biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public uint biClrUsed; public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

        private static void BlitSKBitmap(Graphics g, SKBitmap skBmp, Rectangle destRect)
        {
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = skBmp.Width,
                    biHeight = -skBmp.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = (uint)(skBmp.RowBytes * skBmp.Height)
                }
            };
            IntPtr hdc = g.GetHdc();
            try
            {
                SetStretchBltMode(hdc, HALFTONE);
                StretchDIBits(hdc,
                    destRect.X, destRect.Y, destRect.Width, destRect.Height,
                    0, 0, skBmp.Width, skBmp.Height,
                    skBmp.GetPixels(), ref bmi, DIB_RGB_COLORS, SRCCOPY);
            }
            finally { g.ReleaseHdc(hdc); }
        }

        #endregion

        #region Input

        private void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            _scrollOffset -= e.Delta / 2;
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, Math.Max(0, TotalHeight - Height)));
            Invalidate();
            LoadVisibleThumbs();
            if (e is HandledMouseEventArgs hme) hme.Handled = true;
        }

        private void OnMouseClick(object? sender, MouseEventArgs e)
        {
            int idx = HitTest(e.Location);
            if (idx >= 0 && idx < _items.Count)
            {
                _selectedIndex = idx;
                Invalidate();
                ItemSelected?.Invoke(_items[idx]);
            }
        }

        private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
        {
            int idx = HitTest(e.Location);
            if (idx >= 0 && idx < _items.Count)
                ItemDoubleClicked?.Invoke(_items[idx]);
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            int idx = HitTest(e.Location);
            if (idx != _hoverIndex)
            {
                _hoverIndex = idx;
                Invalidate();
            }
        }

        #endregion

        #region Thumbnail loading

        private static readonly SemaphoreSlim _thumbSemaphore = new(8); // 最大8並行

        private void LoadVisibleThumbs()
        {
            _thumbCts?.Cancel();
            _thumbCts = new CancellationTokenSource();
            var ct = _thumbCts.Token;
            int first = FirstVisibleIndex;
            int last = LastVisibleIndex;

            // 見える範囲のアイテムを並行でサムネイル生成（セマフォで制限）
            var targets = new List<(int idx, FileItem item)>();
            for (int i = first; i < last; i++)
            {
                if (i < 0 || i >= _items.Count) continue;
                var item = _items[i];
                if (_thumbCache.ContainsKey(item.FullPath) || item.IsDirectory) continue;
                if (!item.IsImage) continue;
                targets.Add((i, item));
            }

            if (targets.Count == 0) return;

            Task.Run(() =>
            {
                Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, target =>
                {
                    if (ct.IsCancellationRequested) return;
                    _thumbSemaphore.Wait(ct);
                    try
                    {
                        Stream? stream = _getFileStream?.Invoke(target.item);
                        if (stream == null) return;

                        var skBmp = ImageDecoder.FastDecode(stream, target.item.Ext, _thumbSize, _thumbSize, out _, out _);
                        stream.Dispose();
                        if (skBmp == null || ct.IsCancellationRequested) { skBmp?.Dispose(); return; }

                        // サムネイルキャッシュ上限500枚（LRU）
                        lock (_thumbCacheLock)
                        {
                            if (_thumbCache.Count >= 500 && _thumbCacheOrder.Last != null)
                            {
                                var oldest = _thumbCacheOrder.Last.Value;
                                _thumbCacheOrder.RemoveLast();
                                if (_thumbCache.TryRemove(oldest, out var old))
                                    old?.Dispose();
                            }
                            _thumbCacheOrder.AddFirst(target.item.FullPath);
                        }
                        _thumbCache[target.item.FullPath] = skBmp;

                        // デバウンス: 複数サムネイルの再描画を1回にまとめる
                        if (!ct.IsCancellationRequested && !_invalidatePending)
                        {
                            _invalidatePending = true;
                            BeginInvoke(() => { _invalidatePending = false; Invalidate(); });
                        }
                    }
                    catch (Exception ex) { Logger.Log($"Failed to generate grid thumbnail: {ex.Message}"); }
                    finally { _thumbSemaphore.Release(); }
                });
            }, ct);
        }

        #endregion

        public void ClearCache()
        {
            _thumbCts?.Cancel();
            foreach (var bmp in _thumbCache.Values) bmp.Dispose();
            _thumbCache.Clear();
            lock (_thumbCacheLock) { _thumbCacheOrder.Clear(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearCache();
            base.Dispose(disposing);
        }
    }
}
