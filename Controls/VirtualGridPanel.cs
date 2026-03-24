using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private readonly Dictionary<string, Bitmap> _thumbCache = new();
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
                {
                    using var b = new SolidBrush(SelBg);
                    g.FillRectangle(b, rect);
                }
                else if (i == _hoverIndex)
                {
                    using var b = new SolidBrush(HoverBg);
                    g.FillRectangle(b, rect);
                }

                // サムネイル
                var item = _items[i];
                if (_thumbCache.TryGetValue(item.FullPath, out var thumb))
                {
                    var imgRect = FitRect(thumb.Width, thumb.Height, rect);
                    g.InterpolationMode = InterpolationMode.Low;
                    g.DrawImage(thumb, imgRect);
                }
                else
                {
                    // プレースホルダー
                    using var b = new SolidBrush(Color.FromArgb(0xD0, 0xD0, 0xD0));
                    g.FillRectangle(b, rect.X + 4, rect.Y + 4, rect.Width - 8, rect.Height - 8);
                }

                // ファイル名（下部）
                var nameRect = new Rectangle(rect.X, rect.Bottom - 18, rect.Width, 18);
                using var nameBg = new SolidBrush(Color.FromArgb(180, 255, 255, 255));
                g.FillRectangle(nameBg, nameRect);
                TextRenderer.DrawText(g, item.Name, Font, nameRect, Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            // スクロールバー
            if (TotalHeight > Height)
            {
                int barHeight = Math.Max(20, Height * Height / TotalHeight);
                int barY = (int)((float)_scrollOffset / TotalHeight * Height);
                using var barBrush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
                g.FillRectangle(barBrush, Width - 8, barY, 6, barHeight);
            }
        }

        private static Rectangle FitRect(int imgW, int imgH, Rectangle area)
        {
            float scale = Math.Min((float)(area.Width - 8) / imgW, (float)(area.Height - 24) / imgH);
            int w = (int)(imgW * scale), h = (int)(imgH * scale);
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - 24 - h) / 2, w, h);
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
                if (!FileExtensions.IsImage(FileExtensions.GetExt(item.Name))) continue;
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
                        var ext = FileExtensions.GetExt(target.item.Name);
                        Stream? stream = _getFileStream?.Invoke(target.item);
                        if (stream == null) return;

                        var bmp = ImageDecoder.FastDecode(stream, ext, _thumbSize, _thumbSize, out _, out _);
                        stream.Dispose();
                        if (bmp == null || ct.IsCancellationRequested) { bmp?.Dispose(); return; }

                        // サムネイルキャッシュ上限500枚
                        if (_thumbCache.Count >= 500)
                        {
                            var oldest = _thumbCache.Keys.First();
                            _thumbCache[oldest]?.Dispose();
                            _thumbCache.Remove(oldest);
                        }
                        _thumbCache[target.item.FullPath] = bmp;

                        // デバウンス: 複数サムネイルの再描画を1回にまとめる
                        if (!ct.IsCancellationRequested && !_invalidatePending)
                        {
                            _invalidatePending = true;
                            BeginInvoke(() => { _invalidatePending = false; Invalidate(); });
                        }
                    }
                    catch { }
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearCache();
            base.Dispose(disposing);
        }
    }
}
