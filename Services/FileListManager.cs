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

namespace leeyez_kai.Services
{
    /// <summary>
    /// ファイルリスト (ListView) の管理
    /// </summary>
    public class FileListManager
    {
        private readonly ListView _listView;
        private readonly ImageList _smallIconList;
        private readonly ImageList _largeIconList;
        private readonly Dictionary<string, int> _iconCache = new();
        private int _nextIconIndex;

        // サムネイル生成
        private CancellationTokenSource? _thumbCts;
        private readonly Dictionary<string, bool> _thumbGenerated = new();
        private Func<FileItem, Stream?>? _getFileStream;

        private List<FileItem> _allItems = new();
        private List<FileItem> _filteredItems = new();
        private Dictionary<string, int> _pathToListIndex = new();
        private string _filter = string.Empty;
        private string _sortColumn = "Name";
        private bool _sortDescending;

        public event Action<FileItem>? FileSelected;
        public event Action<FileItem>? FileDoubleClicked;
        public event Action? SortChanged;

        public List<FileItem> Items => _filteredItems;

        public FileListManager(ListView listView)
        {
            _listView = listView;
            _smallIconList = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            _largeIconList = new ImageList { ImageSize = new Size(128, 128), ColorDepth = ColorDepth.Depth32Bit };

            _listView.SmallImageList = _smallIconList;
            _listView.LargeImageList = _largeIconList;
            _listView.View = View.Details;
            _listView.FullRowSelect = true;
            _listView.MultiSelect = true;
            _listView.HideSelection = false;
            _listView.LabelEdit = true;
            _listView.OwnerDraw = true;
            _listView.DrawColumnHeader += ListView_DrawColumnHeader;
            _listView.DrawItem += ListView_DrawItem;
            _listView.DrawSubItem += ListView_DrawSubItem;
            _listView.AfterLabelEdit += ListView_AfterLabelEdit;
            _listView.Font = new Font("MS UI Gothic", 9f);
            _listView.BackColor = Color.FromArgb(0xEB, 0xF4, 0xFF);

            // カラム設定
            _listView.Columns.Add("名前", 200);
            _listView.Columns.Add("サイズ", 80, HorizontalAlignment.Right);
            _listView.Columns.Add("種類", 80);
            _listView.Columns.Add("更新日時", 130);

            _listView.ColumnClick += ListView_ColumnClick;
            _listView.SelectedIndexChanged += ListView_SelectedIndexChanged;
            _listView.MouseDoubleClick += ListView_MouseDoubleClick;

            // デフォルトアイコン
            var folderIcon = NativeMethods.GetFolderIcon(false, true);
            if (folderIcon != null)
            {
                _smallIconList.Images.Add("folder", folderIcon);
                _nextIconIndex = 1;
            }
            var folderIconLarge = NativeMethods.GetFolderIcon(false, false);
            if (folderIconLarge != null)
                _largeIconList.Images.Add("folder", folderIconLarge);
        }

        public bool RecursiveMode { get; set; }

        public void LoadFolder(string folderPath)
        {
            _allItems.Clear();
            _filter = string.Empty;

            try
            {
                var di = new DirectoryInfo(folderPath);
                if (!di.Exists) return;

                // フォルダ（再帰モードでは非表示）
                if (!RecursiveMode)
                {
                    foreach (var dir in di.EnumerateDirectories())
                    {
                        try { _allItems.Add(FileItem.FromDirectoryInfo(dir)); } catch (Exception ex) { Logger.Log($"Failed to enumerate directory: {ex.Message}"); }
                    }
                }

                // ファイル
                var searchOption = RecursiveMode ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                foreach (var file in di.EnumerateFiles("*", searchOption))
                {
                    try
                    {
                        var ext = file.Extension.ToLowerInvariant();
                        if (!IsSupportedFile(ext)) continue;
                        var item = FileItem.FromFileInfo(file);
                        _allItems.Add(item);
                    }
                    catch (Exception ex) { Logger.Log($"Failed to enumerate file: {ex.Message}"); }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            ApplyFilterAndSort();
        }

        public void LoadArchiveEntries(List<ArchiveEntryInfo> entries, string archivePath, string subPath)
        {
            _allItems.Clear();
            _filter = string.Empty;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 単一フォルダ自動展開: 表示名からプレフィックスを除去
            string? prefix = null;
            var files = entries.Where(e => !e.IsFolder && !string.IsNullOrEmpty(e.FileName)).ToList();
            if (files.Count > 0)
            {
                var firstSlash = files[0].FileName.IndexOf('/');
                if (firstSlash > 0)
                {
                    var p = files[0].FileName.Substring(0, firstSlash + 1);
                    if (files.All(f => f.FileName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                        prefix = p;
                }
            }

            foreach (var entry in entries)
            {
                if (entry.IsFolder) continue;
                if (string.IsNullOrEmpty(entry.FileName)) continue;

                var ext = Path.GetExtension(entry.FileName).ToLowerInvariant();
                if (!IsSupportedFile(ext)) continue;

                var fullPath = archivePath + "!" + entry.FileName; // 元のパスを保持
                if (!seen.Add(fullPath)) continue;

                var displayName = entry.FileName;
                if (prefix != null && displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    displayName = displayName.Substring(prefix.Length);

                _allItems.Add(new FileItem
                {
                    Name = Path.GetFileName(displayName),
                    FullPath = fullPath,
                    Size = entry.Size,
                    LastModified = entry.LastWriteTime ?? DateTime.MinValue,
                    IsDirectory = false,
                    IsArchiveFile = false,
                    DisplayType = ext.TrimStart('.').ToUpperInvariant()
                });
            }

            Logger.Log($"LoadArchiveEntries: {entries.Count} entries, {_allItems.Count} supported files");
            ApplyFilterAndSort();
        }

        public void SetFilter(string filter)
        {
            _filter = filter ?? string.Empty;
            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            // フィルター
            if (string.IsNullOrWhiteSpace(_filter))
            {
                _filteredItems = new List<FileItem>(_allItems);
            }
            else
            {
                _filteredItems = _allItems
                    .Where(f => f.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // ソート（名前ソート時のみフォルダ優先、それ以外は混在）
            Func<FileItem, IComparable> keySelector = _sortColumn switch
            {
                "Size" or "サイズ" => f => f.Size,
                "DisplayType" or "種類" => f => f.DisplayType,
                "LastModified" or "更新日時" => f => f.LastModified,
                _ => f => f.Name
            };

            if (_sortColumn == "Name")
            {
                // 名前ソートのみフォルダ優先
                var dirs = _filteredItems.Where(f => f.IsDirectory);
                var files = _filteredItems.Where(f => !f.IsDirectory);
                dirs = _sortDescending ? dirs.OrderByDescending(keySelector) : dirs.OrderBy(keySelector);
                files = _sortDescending ? files.OrderByDescending(keySelector) : files.OrderBy(keySelector);
                _filteredItems = dirs.Concat(files).ToList();
            }
            else
            {
                // その他のソートはフォルダ・ファイル混在
                _filteredItems = _sortDescending
                    ? _filteredItems.OrderByDescending(keySelector).ToList()
                    : _filteredItems.OrderBy(keySelector).ToList();
            }

            RefreshListView();
        }

        private void RefreshListView()
        {
            _listView.BeginUpdate();
            _listView.Items.Clear();

            // アイコンを事前に一括登録（ループ中のImageList変更を避ける）
            var newExts = new HashSet<string>();
            foreach (var item in _filteredItems)
            {
                if (item.IsDirectory) continue;
                var ext = Path.GetExtension(item.Name).ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext) && !_iconCache.ContainsKey(ext) && newExts.Add(ext))
                {
                    var iconSmall = NativeMethods.GetExtensionIcon(ext, true);
                    if (iconSmall != null)
                        _smallIconList.Images.Add(ext, iconSmall);
                    var iconLarge = NativeMethods.GetExtensionIcon(ext, false);
                    if (iconLarge != null)
                        _largeIconList.Images.Add(ext, iconLarge);
                    _iconCache[ext] = _nextIconIndex++;
                }
            }

            // ListViewItemをバッチ生成してAddRange（個別Addより高速）
            var items = new ListViewItem[_filteredItems.Count];
            for (int i = 0; i < _filteredItems.Count; i++)
            {
                var item = _filteredItems[i];
                var lvi = new ListViewItem(item.Name);
                lvi.SubItems.Add(item.SizeString);
                lvi.SubItems.Add(item.DisplayType);
                lvi.SubItems.Add(item.LastModified > DateTime.MinValue ? item.LastModified.ToString("yyyy/MM/dd HH:mm") : "");
                lvi.Tag = item;
                lvi.ImageKey = item.IsDirectory ? "folder" : Path.GetExtension(item.Name).ToLowerInvariant();
                items[i] = lvi;
            }
            _listView.Items.AddRange(items);

            _pathToListIndex = new Dictionary<string, int>(_filteredItems.Count);
            for (int i = 0; i < _filteredItems.Count; i++)
                _pathToListIndex[_filteredItems[i].FullPath] = i;

            _listView.EndUpdate();
        }

        private void ListView_ColumnClick(object? sender, ColumnClickEventArgs e)
        {
            var col = e.Column switch
            {
                0 => "Name",
                1 => "Size",
                2 => "DisplayType",
                3 => "LastModified",
                _ => "Name"
            };

            if (_sortColumn == col)
            {
                _sortDescending = !_sortDescending;
            }
            else
            {
                _sortColumn = col;
                _sortDescending = false;
            }

            ApplyFilterAndSort();
            SortChanged?.Invoke();
        }

        private void ListView_AfterLabelEdit(object? sender, LabelEditEventArgs e)
        {
            if (e.Label == null || e.CancelEdit) return; // ESCでキャンセル
            var newName = e.Label.Trim();
            if (string.IsNullOrEmpty(newName)) { e.CancelEdit = true; return; }

            var lvi = _listView.Items[e.Item];
            var fileItem = lvi.Tag as FileItem;
            if (fileItem == null || fileItem.FullPath.Contains('!')) { e.CancelEdit = true; return; }

            try
            {
                var dir = Path.GetDirectoryName(fileItem.FullPath);
                if (dir == null) { e.CancelEdit = true; return; }

                var newPath = Path.Combine(dir, newName);
                if (newPath == fileItem.FullPath) { e.CancelEdit = true; return; }

                if (fileItem.IsDirectory)
                    Directory.Move(fileItem.FullPath, newPath);
                else
                    File.Move(fileItem.FullPath, newPath);

                // FileItemを更新
                fileItem.FullPath = newPath;
                fileItem.Name = newName;

                // SubItemsも更新
                lvi.SubItems[0].Text = newName;
            }
            catch (Exception ex)
            {
                e.CancelEdit = true;
                System.Windows.Forms.MessageBox.Show(
                    $"名前の変更に失敗しました: {ex.Message}", "エラー",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private void ListView_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_suppressSelectEvent) return;
            if (_listView.SelectedItems.Count == 1)
            {
                var item = _listView.SelectedItems[0].Tag as FileItem;
                if (item != null) FileSelected?.Invoke(item);
            }
        }

        private void ListView_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (_listView.SelectedItems.Count == 1)
            {
                var item = _listView.SelectedItems[0].Tag as FileItem;
                if (item != null) FileDoubleClicked?.Invoke(item);
            }
        }

        public FileItem? GetItemByIndex(int index)
        {
            if (index >= 0 && index < _filteredItems.Count)
                return _filteredItems[index];
            return null;
        }

        public int GetIndex(FileItem item)
        {
            // まず参照一致を試行、次にパスで検索
            int idx = _filteredItems.IndexOf(item);
            if (idx >= 0) return idx;
            return _filteredItems.FindIndex(f => f.FullPath == item.FullPath);
        }

        private bool _suppressSelectEvent;

        public void SelectItem(int index)
        {
            SelectItems(new[] { index });
        }

        public void SelectItems(IEnumerable<int> indices)
        {
            _suppressSelectEvent = true;
            try
            {
                // 既存選択を解除
                foreach (int i in _listView.SelectedIndices.Cast<int>().ToList())
                    _listView.Items[i].Selected = false;

                int first = -1;
                foreach (var index in indices)
                {
                    if (index >= 0 && index < _listView.Items.Count)
                    {
                        _listView.Items[index].Selected = true;
                        if (first < 0)
                        {
                            _listView.Items[index].Focused = true;
                            _listView.Items[index].EnsureVisible();
                            first = index;
                        }
                    }
                }
            }
            finally { _suppressSelectEvent = false; }
        }

        // ── OwnerDraw: Leeyes風ハイライト ──
        private static readonly Color SelBg = Color.FromArgb(0xFF, 0xC0, 0xC0);   // 薄いピンク
        private static readonly Color SelFg = Color.Black;
        private static readonly Color NormalFg = Color.Black;

        private void ListView_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
        {
            e.DrawDefault = true;
        }

        private void ListView_DrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            // DrawSubItemに任せる
        }

        private void ListView_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
        {
            if (e.Item == null) return;
            bool selected = e.Item.Selected;
            var bg = selected ? SelBg : _listView.BackColor;
            var fg = selected ? SelFg : NormalFg;

            using var bgBrush = new SolidBrush(bg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            // アイコン（最初のカラムのみ）
            if (e.ColumnIndex == 0 && e.Item.ImageKey != null && _smallIconList.Images.ContainsKey(e.Item.ImageKey))
            {
                var icon = _smallIconList.Images[e.Item.ImageKey];
                int iconY = e.Bounds.Y + (e.Bounds.Height - 16) / 2;
                e.Graphics.DrawImage(icon, e.Bounds.X + 2, iconY, 16, 16);

                var textRect = new Rectangle(e.Bounds.X + 20, e.Bounds.Y, e.Bounds.Width - 22, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", _listView.Font, textRect, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else
            {
                var flags = e.ColumnIndex == 1
                    ? TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
                    : TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
                var textRect = new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height);
                TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", _listView.Font, textRect, fg, flags | TextFormatFlags.NoPrefix);
            }

            // フォーカス枠
            if (selected && e.Item.Focused && e.ColumnIndex == 0)
                ControlPaint.DrawFocusRectangle(e.Graphics, e.Item.Bounds);
        }

        private static bool IsSupportedFile(string ext)
            => FileExtensions.IsViewable(ext) || FileExtensions.IsArchive(ext);

        /// <summary>ファイルストリーム取得関数を設定（書庫内ファイル対応）</summary>
        public void SetFileStreamProvider(Func<FileItem, Stream?> provider)
        {
            _getFileStream = provider;
        }

        /// <summary>グリッド表示用サムネイルをバックグラウンド生成（バッチ方式）</summary>
        public void GenerateThumbnailsAsync()
        {
            _thumbCts?.Cancel();
            _thumbCts = new CancellationTokenSource();
            var ct = _thumbCts.Token;
            var items = _filteredItems.ToList();

            Task.Run(() =>
            {
                // バッチ: 10枚ずつまとめてUIに反映
                var batch = new List<(string thumbKey, Bitmap thumb, string fullPath)>();
                const int batchSize = 10;

                foreach (var item in items)
                {
                    if (ct.IsCancellationRequested) return;
                    if (item.IsDirectory) continue;
                    if (!item.IsImage) continue;

                    var thumbKey = "thumb_" + item.FullPath.GetHashCode().ToString("X");
                    lock (_thumbGenerated)
                    {
                        if (_thumbGenerated.ContainsKey(thumbKey)) continue;
                    }

                    try
                    {
                        Stream? stream = null;
                        if (_getFileStream != null)
                            stream = _getFileStream(item);
                        else if (File.Exists(item.FullPath))
                            stream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                        if (stream == null) continue;

                        var skBmp = ImageDecoder.FastDecode(stream, item.Ext, 128, 128, out _, out _);
                        stream.Dispose();
                        if (skBmp == null || ct.IsCancellationRequested) { skBmp?.Dispose(); continue; }

                        using var gdiBmp = SKBitmapHelper.ToGdiBitmap(skBmp);
                        skBmp.Dispose();
                        var thumb = CreateSquareThumb(gdiBmp, 128);

                        if (ct.IsCancellationRequested) { thumb.Dispose(); continue; }

                        batch.Add((thumbKey, thumb, item.FullPath));
                        lock (_thumbGenerated) { _thumbGenerated[thumbKey] = true; }

                        if (batch.Count >= batchSize)
                        {
                            FlushThumbBatch(batch, ct);
                            batch = new List<(string, Bitmap, string)>();
                        }
                    }
                    catch (Exception ex) { Logger.Log($"Failed to generate thumbnail: {ex.Message}"); }
                }

                // 残りをフラッシュ
                if (batch.Count > 0 && !ct.IsCancellationRequested)
                    FlushThumbBatch(batch, ct);
            }, ct);
        }

        private void FlushThumbBatch(List<(string thumbKey, Bitmap thumb, string fullPath)> batch, CancellationToken ct)
        {
            var localBatch = batch.ToList();
            try
            {
                _listView.BeginInvoke(() =>
                {
                    if (ct.IsCancellationRequested) { foreach (var b in localBatch) b.thumb.Dispose(); return; }

                    _listView.BeginUpdate();
                    foreach (var (thumbKey, thumb, fullPath) in localBatch)
                    {
                        if (!_largeIconList.Images.ContainsKey(thumbKey))
                            _largeIconList.Images.Add(thumbKey, thumb);

                        // インデックスで直接アクセス（O(1)）
                        if (_pathToListIndex.TryGetValue(fullPath, out int idx)
                            && idx < _listView.Items.Count)
                            _listView.Items[idx].ImageKey = thumbKey;
                    }
                    _listView.EndUpdate();
                });
            }
            catch (Exception ex) { Logger.Log($"Failed to flush thumbnail batch: {ex.Message}"); }
        }

        private static Bitmap CreateSquareThumb(Bitmap src, int size)
        {
            var thumb = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(thumb);
            g.Clear(Color.White);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;

            float scale = Math.Min((float)size / src.Width, (float)size / src.Height);
            int dw = (int)(src.Width * scale);
            int dh = (int)(src.Height * scale);
            int dx = (size - dw) / 2;
            int dy = (size - dh) / 2;
            g.DrawImage(src, dx, dy, dw, dh);
            return thumb;
        }
    }
}
