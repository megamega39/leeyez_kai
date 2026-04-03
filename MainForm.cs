using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using leeyez_kai.Controls;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm : Form
    {
        // ── 共有フォント（重複生成回避） ──
        private static readonly Font _fontUI8 = new("Yu Gothic UI", 8f);
        private static readonly Font _fontUI85 = new("Yu Gothic UI", 8.5f);
        private static readonly Font _fontUI9 = new("Yu Gothic UI", 9f);
        private static readonly Font _fontUI10 = new("Yu Gothic UI", 10f);
        private static readonly Font _fontUI10B = new("Yu Gothic UI", 10f, FontStyle.Bold);
        private static readonly Font _fontUI15B = new("Yu Gothic UI", 15f, FontStyle.Bold);
        private static readonly Font _fontSegoe10 = new("Segoe UI", 10f);
        private Font _fontIcon9 = null!;
        private Font _fontIcon10 = null!;
        private Font _fontIcon18 = null!;

        // ── サービス ──
        private readonly NavigationManager _nav = new();
        private readonly ImageCache _imageCache;
        private FolderWatcherService? _folderWatcher;
        private FolderTreeManager? _treeManager;
        private FileListManager? _fileListManager;

        // ── 書庫 ──
        private string _sevenZipLibPath = string.Empty;
        private List<ArchiveEntryInfo>? _archiveEntries;
        private string? _currentArchivePath;
        private readonly Dictionary<string, List<ArchiveEntryInfo>> _archiveEntryCache = new();
        private readonly List<string> _archiveEntryCacheOrder = new();
        private readonly object _archiveEntryCacheLock = new();
        private const int MaxArchiveEntryCacheSize = 20;
        // 書庫内ファイルのStreamキャッシュ（展開済みバイト列、LRU管理）
        private readonly object _streamCacheLock = new();
        private readonly Dictionary<string, byte[]> _archiveStreamCache = new();
        private readonly LinkedList<string> _archiveStreamCacheOrder = new();
        private long _archiveStreamCacheBytes;
        private const long MaxArchiveStreamCacheBytes = 20 * 1024 * 1024; // 20MB

        // ── ビューアー状態 ──
        private List<FileItem> _viewableFiles = new();
        private int _currentFileIndex = -1;
        private bool _isRTL = true;
        private int _viewMode; // 0=Auto, 1=Single, 2=Spread

        // ── ナビゲーション制御 ──
        private bool _isNavigating;
        private bool _skipSelectPath;
        private bool _pendingSkipSelectPath; // デバウンス用

        // ── 高速化 ──
        private System.Windows.Forms.Timer? _debounceTimer;
        private int _pendingFileIndex = -1;
        private int _navDirection = 1; // プリフェッチ方向予測用
        private CancellationTokenSource? _prefetchCts;

        // ── UIコントロール ──
        private ToolStrip _navBar = null!;
        private ToolStripSplitButton _btnBack = null!, _btnForward = null!;
        private ToolStripButton _btnUp = null!, _btnRefresh = null!;
        private ToolStripButton _btnHoverPreview = null!, _btnListView = null!, _btnGridView = null!;
        private ToolStripButton _btnHelp = null!, _btnSettings = null!;

        private Panel _addressBarPanel = null!;
        private TextBox _addressBox = null!;
        private Label _addressLabel = null!;
        private FlowLayoutPanel _breadcrumbPanel = null!;

        private SplitContainer _mainSplit = null!;
        private SplitContainer _sidebarSplit = null!;
        private TreeView _folderTree = null!;
        private Panel _filterPanel = null!;
        private TextBox _filterBox = null!;
        private ListView _fileList = null!;
        private VirtualGridPanel _virtualGrid = null!;

        private Panel _viewerPanel = null!;
        private ToolStrip _viewerToolbar = null!;
        private ImageViewer _imageViewer = null!;
        private FFmpegPlayerPanel _mediaPlayer = null!;

        private ToolStripButton _btnFirst = null!, _btnPrev = null!, _btnNext = null!, _btnLast = null!;
        private ToolStripLabel _pageLabel = null!;
        private ToolStripButton _btnFitWindow = null!, _btnFitWidth = null!, _btnFitHeight = null!, _btnOriginal = null!;
        private ToolStripButton _btnZoomIn = null!, _btnZoomOut = null!;
        private ToolStripLabel _zoomLabel = null!;
        private ToolStripButton _btnBinding = null!, _btnAutoView = null!, _btnSingleView = null!, _btnSpreadView = null!;

        private StatusStrip _statusBar = null!;
        private ToolStripStatusLabel _statusLeft = null!, _statusRight = null!;

        // ホバープレビュー
        private HoverPreviewForm? _hoverPreview;
        private System.Windows.Forms.Timer? _hoverTimer;
        private FileItem? _hoverItem;
        private bool _hoverPreviewEnabled;

        // 状態キャッシュ
        private AppState? _cachedState;

        // 設定
        private AppSettings _appSettings = AppSettings.Load();
        private readonly ShortcutManager _shortcutManager = new();

        // 本棚
        private readonly BookshelfService _bookshelfService = new();
        private TreeView _bookshelfTree = null!;
        private bool _isBookshelfMode;
        private bool _bookshelfDirty;
        private ToolStripButton _btnBookshelf = null!;
        private Label _sidebarLabel = null!;
        private Panel _bookshelfToolbar = null!;
        private Button _treeSortBtn = null!;
        private ContextMenuStrip _treeSortMenu = null!;

        // 履歴ボタン（フィールドはMainForm.History.csで定義）
        private ToolStripButton _btnHistory = null!;

        public MainForm()
        {
            // 言語設定を適用
            i18n.Localization.SetLanguage(_appSettings.Language);

            // メモリ上限からキャッシュ枚数を計算（1枚≒4MB）
            _imageCache = new ImageCache(Math.Max(16, _appSettings.MemoryLimitMB / 4));
            _imageCache.SetMaxBytes((long)_appSettings.MemoryLimitMB * 1024 * 1024);

            InitializeComponent();
            SetupSevenZipPath();
            SetupServices();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowState();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BeginInvoke(() => LoadState());
        }

        private void ApplyTreeSort(TreeSortMode mode)
        {
            if (_treeManager == null) return;
            bool desc = _treeManager.SortMode == mode ? !_treeManager.SortDescending : false;
            _treeManager.SetSortMode(mode, desc);
            _appSettings.TreeSortMode = mode.ToString();
            _appSettings.TreeSortDescending = desc;
            _appSettings.Save();
        }

        private void ApplyTreeSortDirection(bool descending)
        {
            if (_treeManager == null) return;
            _treeManager.SetSortMode(_treeManager.SortMode, descending);
            _appSettings.TreeSortDescending = descending;
            _appSettings.Save();
        }

        private void SetupServices()
        {
            _treeManager = new FolderTreeManager(_folderTree);
            if (Enum.TryParse<TreeSortMode>(_appSettings.TreeSortMode, out var treeSortMode))
                _treeManager.SetSortMode(treeSortMode, _appSettings.TreeSortDescending);
            _treeManager.FolderSelected += (path) =>
            {
                if (_isNavigating) return;
                // ツリーからの選択時は再度SelectPathを呼ぶ必要なし
                _skipSelectPath = true;
                NavigateTo(path);
                _skipSelectPath = false;
            };
            _treeManager.FavoritesChanged += (favorites) =>
            {
                var state = _cachedState ?? PersistenceService.LoadState() ?? new AppState();
                _cachedState = state;
                state.Favorites = favorites;
                SaveCurrentState();
            };

            _fileListManager = new FileListManager(_fileList);
            _fileListManager.FileSelected += OnFileSelected;
            _fileListManager.FileDoubleClicked += OnFileDoubleClicked;
            _fileListManager.SetFileStreamProvider(GetFileStream);
            _fileListManager.RecursiveMode = _appSettings.RecursiveMedia;
            _fileListManager.SortChanged += () =>
            {
                // ソート後にviewableFilesをファイルリストの表示順で再構築
                string? currentPath = null;
                if (_currentFileIndex >= 0 && _currentFileIndex < _viewableFiles.Count)
                    currentPath = _viewableFiles[_currentFileIndex].FullPath;

                UpdateViewableFiles();

                // 現在のファイルのインデックスを復元
                if (currentPath != null)
                {
                    _currentFileIndex = _viewableFiles.FindIndex(f => f.FullPath == currentPath);
                    if (_currentFileIndex >= 0)
                        SyncFileListSelection(_currentFileIndex);
                    UpdatePageLabel();
                }
            };

            _folderWatcher = new FolderWatcherService((path) =>
            {
                if (path == _nav.CurrentPath) NavigateTo(path);
            });

            _virtualGrid.ItemSelected += OnFileSelected;
            _virtualGrid.ItemDoubleClicked += OnFileDoubleClicked;
            _virtualGrid.ItemRightClicked += (item, screenPt) =>
            {
                _fileList.ContextMenuStrip?.Show(screenPt);
            };

            _bookshelfService.Load();
            _historyService.Load();
            SetupHoverPreview();
            SetupBookshelf();
            SetupHistory();
        }

        private void SetupSevenZipPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "7z.dll"),
                Path.Combine(baseDir, "x64", "7z.dll"),
                Path.Combine(baseDir, "runtimes", "win-x64", "native", "7z.dll")
            };

            foreach (var c in candidates)
            {
                if (File.Exists(c)) { _sevenZipLibPath = c; return; }
            }

            var nugetPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", "7z.libs");
            if (Directory.Exists(nugetPath))
            {
                try
                {
                    var latest = Directory.GetDirectories(nugetPath).OrderByDescending(d => d).FirstOrDefault();
                    if (latest != null)
                    {
                        var dll = Path.Combine(latest, "x64", "7z.dll");
                        if (File.Exists(dll)) { _sevenZipLibPath = dll; return; }
                    }
                }
                catch (Exception ex) { Logger.Log($"Failed to find 7z.dll from NuGet: {ex.Message}"); }
            }

            Logger.Log("WARNING: 7z.dll not found");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            _autoSaveTimer?.Stop();
            _hoverTimer?.Stop();
            _hoverPreview?.Close();
            _mediaPlayer.Stop();
            CleanupTempMedia();
            SaveCurrentState();
            _historyService.Save();
            _historyService.Dispose();
            _imageCache.Dispose();
            _folderWatcher?.Dispose();
            _archiveDebounce?.Dispose();
            _debounceTimer?.Dispose();
            _prefetchCts?.Dispose();
            ArchiveService.CloseCache();
        }

        // ── 設定・ヘルプ ──
        private void ShowSettings()
        {
            var prevLang = _appSettings.Language;
            using var dlg = new SettingsDialog(_appSettings, _shortcutManager);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // 言語変更時にUIテキストを更新
                if (_appSettings.Language != prevLang)
                    ApplyLanguageToUI();
                // 設定を適用（古いFontをDisposeしてからセット）
                var newFont = new System.Drawing.Font("Yu Gothic UI", _appSettings.SidebarFontSize);
                var oldFonts = new HashSet<Font> { _folderTree.Font, _fileList.Font, _bookshelfTree.Font, _historyList.Font };
                _folderTree.Font = newFont;
                _fileList.Font = newFont;
                _bookshelfTree.Font = newFont;
                _historyList.Font = newFont;
                foreach (var f in oldFonts)
                    if (f != null && f != newFont) f.Dispose();
                _fileListManager!.RecursiveMode = _appSettings.RecursiveMedia;
                _imageCache.SetMaxEntries(Math.Max(16, _appSettings.MemoryLimitMB / 4));
                _imageCache.SetMaxBytes((long)_appSettings.MemoryLimitMB * 1024 * 1024);
                _virtualGrid.SetThumbnailSize(_appSettings.ThumbnailSize);
                // 再読み込み
                if (!string.IsNullOrEmpty(_nav.CurrentPath))
                    Refresh();
                Invalidate(true);
            }
        }

        private void ApplyLanguageToUI()
        {
            // サイドバーラベル
            if (!_isHistoryMode && !_isBookshelfMode)
                _sidebarLabel.Text = i18n.Localization.Get("sidebar.folder");

            // ナビゲーションバー
            _btnBack.ToolTipText = i18n.Localization.Get("nav.back");
            _btnForward.ToolTipText = i18n.Localization.Get("nav.forward");
            _btnUp.ToolTipText = i18n.Localization.Get("nav.up");
            _btnRefresh.ToolTipText = i18n.Localization.Get("nav.refresh");
            _btnHoverPreview.ToolTipText = i18n.Localization.Get("nav.hover");
            _btnBookshelf.ToolTipText = i18n.Localization.Get("nav.bookshelf");
            _btnHistory.ToolTipText = i18n.Localization.Get("history.label");
            _btnListView.ToolTipText = i18n.Localization.Get("nav.list");
            _btnGridView.ToolTipText = i18n.Localization.Get("nav.grid");
            _btnSettings.ToolTipText = i18n.Localization.Get("nav.settings");
            _btnHelp.ToolTipText = i18n.Localization.Get("nav.help");

            // アドレスラベル
            _addressLabel.Text = i18n.Localization.Get("sidebar.address");

            // フィルターのプレースホルダー
            SetPlaceholder(_filterBox, i18n.Localization.Get("sidebar.filter"));
            SetPlaceholder(_historyFilterBox, i18n.Localization.Get("sidebar.filter"));

            // 履歴パネル
            _historyLabel.Text = i18n.Localization.Get("history.label");
            _historyBtnClear.Text = i18n.Localization.Get("history.clear");

            // ツリーソートメニュー
            _treeSortMenu.Items[0].Text = i18n.Localization.Get("sort.name");
            _treeSortMenu.Items[1].Text = i18n.Localization.Get("sort.modified");
            _treeSortMenu.Items[2].Text = i18n.Localization.Get("sort.size");
            _treeSortMenu.Items[3].Text = i18n.Localization.Get("sort.type");
            _treeSortMenu.Items[5].Text = i18n.Localization.Get("sort.asc");
            _treeSortMenu.Items[6].Text = i18n.Localization.Get("sort.desc");

            // 履歴コンテキストメニュー再構築
            if (_historyList.ContextMenuStrip != null)
            {
                var ctx = _historyList.ContextMenuStrip;
                ctx.Items.Clear();
                ctx.Items.Add(i18n.Localization.Get("ctx.openwith"), null, (s, e) => OpenHistoryWithAssociation());
                ctx.Items.Add(i18n.Localization.Get("ctx.explorer"), null, (s, e) => ShowHistoryInExplorer());
                ctx.Items.Add(i18n.Localization.Get("ctx.copypath"), null, (s, e) => CopyHistoryPath());
                ctx.Items.Add(new ToolStripSeparator());
                ctx.Items.Add(i18n.Localization.Get("ctx.addfav"), null, (s, e) => AddHistoryToFavorites());
                ctx.Items.Add(i18n.Localization.Get("ctx.addshelf"), null, (s, e) => AddHistoryToBookshelf());
                ctx.Items.Add(new ToolStripSeparator());
                ctx.Items.Add(i18n.Localization.Get("history.deleteentry"), null, (s, e) => DeleteHistoryEntry());
            }

            // ビューアコンテキストメニュー
            if (_imageViewer.ContextMenuStrip != null)
            {
                var ctx = _imageViewer.ContextMenuStrip;
                ctx.Items[0].Text = i18n.Localization.Get("menu.fullscreen");
                ctx.Items[1].Text = i18n.Localization.Get("menu.openwith");
                ctx.Items[2].Text = i18n.Localization.Get("menu.copypath");
                ctx.Items[3].Text = i18n.Localization.Get("menu.copyparentpath");
                ctx.Items[4].Text = i18n.Localization.Get("menu.openexplorer");
            }

            // ファイルリストコンテキストメニュー
            if (_fileList.ContextMenuStrip != null)
            {
                var ctx = _fileList.ContextMenuStrip;
                ctx.Items[0].Text = i18n.Localization.Get("menu.openwith");
                ctx.Items[1].Text = i18n.Localization.Get("menu.openexplorer");
                ctx.Items[2].Text = i18n.Localization.Get("menu.copypath");
                ctx.Items[3].Text = i18n.Localization.Get("menu.copyfilename");
                // [4] separator
                ctx.Items[5].Text = i18n.Localization.Get("menu.addfavorite");
                ctx.Items[6].Text = i18n.Localization.Get("menu.addbookshelf");
                // [7] separator
                ctx.Items[8].Text = i18n.Localization.Get("menu.rename");
                ctx.Items[9].Text = i18n.Localization.Get("menu.delete");
            }

            // ツリーコンテキストメニュー
            if (_folderTree.ContextMenuStrip != null)
            {
                var ctx = _folderTree.ContextMenuStrip;
                ctx.Items[0].Text = i18n.Localization.Get("menu.openwith");
                ctx.Items[1].Text = i18n.Localization.Get("menu.openexplorer");
                ctx.Items[2].Text = i18n.Localization.Get("menu.copypath");
                // [3] separator
                ctx.Items[4].Text = i18n.Localization.Get("menu.addfavorite");
                ctx.Items[5].Text = i18n.Localization.Get("menu.removefavorite");
                ctx.Items[6].Text = i18n.Localization.Get("menu.addbookshelf");
                // [7] separator
                ctx.Items[8].Text = i18n.Localization.Get("menu.rename");
                ctx.Items[9].Text = i18n.Localization.Get("menu.delete");
            }

            // 本棚コンテキストメニュー
            if (_bookshelfTree.ContextMenuStrip != null)
            {
                var ctx = _bookshelfTree.ContextMenuStrip;
                ctx.Items[0].Text = i18n.Localization.Get("menu.open");
                ctx.Items[1].Text = i18n.Localization.Get("menu.openwith");
                ctx.Items[2].Text = i18n.Localization.Get("menu.openexplorer");
                // [3] separator
                ctx.Items[4].Text = i18n.Localization.Get("menu.rename");
                ctx.Items[5].Text = i18n.Localization.Get("menu.removebookshelf");
            }
        }

        private void ShowHelp()
        {
            using var dlg = new HelpDialog();
            dlg.ShowDialog(this);
        }

        // ── リスト/グリッド切替 ──
        private void SwitchToListView()
        {
            _isGridMode = false;
            _virtualGrid.Visible = false;
            _fileList.Visible = true;
            _fileList.OwnerDraw = true;
            _fileList.View = View.Details;
        }

        private bool _isGridMode;

        private void SwitchToGridView()
        {
            _isGridMode = true;
            _fileList.Visible = false;
            _virtualGrid.Visible = true;
            _virtualGrid.BringToFront();
            _virtualGrid.SetThumbnailSize(_appSettings.ThumbnailSize);
            _virtualGrid.SetFileStreamProvider(GetFileStream);
            _virtualGrid.SetThumbStreamProvider(GetThumbStream);
            RefreshGridItems();
        }

        /// <summary>グリッド表示中ならアイテムを再設定</summary>
        private void RefreshGridItems()
        {
            if (!_isGridMode || !_virtualGrid.Visible) return;
            _virtualGrid.SetFileStreamProvider(GetFileStream);
            _virtualGrid.SetThumbStreamProvider(GetThumbStream);
            if (_fileListManager != null)
                _virtualGrid.SetItems(_fileListManager.Items);
            if (_currentFileIndex >= 0 && _currentFileIndex < _viewableFiles.Count)
                _virtualGrid.SelectByPath(_viewableFiles[_currentFileIndex].FullPath);
        }

        private (Stream? stream, string ext) GetThumbStream(FileItem item)
        {
            if (item.IsImage)
                return (GetFileStream(item), item.Ext);

            if (item.IsArchiveExt && File.Exists(item.FullPath))
            {
                try
                {
                    var entries = GetArchiveEntries(item.FullPath);
                    var firstImage = entries.FirstOrDefault(e => !e.IsFolder
                        && FileExtensions.IsImage(FileExtensions.GetExt(e.FileName)));
                    if (firstImage != null)
                    {
                        var stream = ArchiveService.GetEntryStream(item.FullPath, firstImage.FileName, _sevenZipLibPath);
                        return (stream, FileExtensions.GetExt(firstImage.FileName));
                    }
                }
                catch { }
            }

            if (item.IsDirectory && Directory.Exists(item.FullPath))
            {
                try
                {
                    string? firstImage = null;
                    foreach (var pattern in new[] { "*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.gif", "*.avif" })
                    {
                        firstImage = Directory.EnumerateFiles(item.FullPath, pattern).FirstOrDefault();
                        if (firstImage != null) break;
                    }
                    if (firstImage != null)
                        return (new FileStream(firstImage, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), FileExtensions.GetExt(firstImage));
                }
                catch { }
            }

            return (null, "");
        }

        // ── ヘルパー ──
        private ToolStripButton CreateNavButton(string text, string tooltip, string fontName, float fontSize)
        {
            return new ToolStripButton(text)
            {
                ToolTipText = tooltip, AutoSize = false, Size = new Size(48, 44),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font(fontName, fontSize),
                ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(2), Margin = new Padding(1, 0, 1, 0)
            };
        }

        private ToolStripButton CreateViewerButton(string text, string tooltip, string fontName, float fontSize = 13f)
        {
            return new ToolStripButton(text)
            {
                ToolTipText = tooltip, AutoSize = false, Size = new Size(32, 28),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font(fontName, fontSize),
                ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(0), Margin = new Padding(1, 2, 1, 2)
            };
        }
        /// <summary>幅/高さ合わせアイコンを描画（四角+境界線）</summary>
        private static Bitmap DrawFitIcon(bool fitWidth)
        {
            var bmp = new Bitmap(24, 24);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var col = Color.FromArgb(60, 60, 60);
            using var pen = new Pen(col, 1.8f);
            using var boldPen = new Pen(col, 2.5f);

            if (fitWidth)
            {
                // 幅合わせ: 左右の太線 + 中央の四角が幅いっぱい
                g.DrawLine(boldPen, 2, 3, 2, 21);   // 左境界線
                g.DrawLine(boldPen, 22, 3, 22, 21);  // 右境界線
                g.DrawRectangle(pen, 5, 6, 14, 12);  // 中央の四角
            }
            else
            {
                // 高さ合わせ: 上下の太線 + 中央の四角が高さいっぱい
                g.DrawLine(boldPen, 3, 2, 21, 2);    // 上境界線
                g.DrawLine(boldPen, 3, 22, 21, 22);   // 下境界線
                g.DrawRectangle(pen, 6, 5, 12, 14);   // 中央の四角
            }
            return bmp;
        }
    }
}
