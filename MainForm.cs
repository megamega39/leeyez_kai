using System;
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
        // 書庫内ファイルのStreamキャッシュ（展開済みバイト列）
        private readonly Dictionary<string, byte[]> _archiveStreamCache = new();

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

        // 設定
        private AppSettings _appSettings = AppSettings.Load();
        private readonly ShortcutManager _shortcutManager = new();

        // 本棚
        private readonly BookshelfService _bookshelfService = new();
        private TreeView _bookshelfTree = null!;
        private bool _isBookshelfMode;
        private ToolStripButton _btnBookshelf = null!;
        private Label _sidebarLabel = null!;
        private Panel _bookshelfToolbar = null!;

        public MainForm()
        {
            // メモリ上限からキャッシュ枚数を計算（1枚≒4MB）
            _imageCache = new ImageCache(Math.Max(16, _appSettings.MemoryLimitMB / 4));

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

        private void SetupServices()
        {
            _treeManager = new FolderTreeManager(_folderTree);
            _treeManager.FolderSelected += (path) =>
            {
                if (_isNavigating) return;
                // お気に入り配下からの選択ならSelectPathをスキップ
                _skipSelectPath = _treeManager.IsSelectedUnderFavorites();
                NavigateTo(path);
                _skipSelectPath = false;
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

            _bookshelfService.Load();
            SetupHoverPreview();
            SetupBookshelf();
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
                catch { }
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
            _imageCache.Dispose();
            _folderWatcher?.Dispose();
        }

        // ── 設定・ヘルプ ──
        private void ShowSettings()
        {
            using var dlg = new SettingsDialog(_appSettings, _shortcutManager);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                // 設定を適用
                _folderTree.Font = new System.Drawing.Font("Yu Gothic UI", _appSettings.SidebarFontSize);
                _fileList.Font = new System.Drawing.Font("Yu Gothic UI", _appSettings.SidebarFontSize);
                _bookshelfTree.Font = new System.Drawing.Font("Yu Gothic UI", _appSettings.SidebarFontSize);
                _fileListManager!.RecursiveMode = _appSettings.RecursiveMedia;
                _imageCache.SetMaxEntries(Math.Max(16, _appSettings.MemoryLimitMB / 4));
                // 再読み込み
                if (!string.IsNullOrEmpty(_nav.CurrentPath))
                    Refresh();
                Invalidate(true);
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
            _virtualGrid.SetFileStreamProvider(GetFileStream);
            RefreshGridItems();
        }

        /// <summary>グリッド表示中ならアイテムを再設定</summary>
        private void RefreshGridItems()
        {
            if (!_isGridMode || !_virtualGrid.Visible) return;
            _virtualGrid.SetFileStreamProvider(GetFileStream);
            if (_fileListManager != null)
                _virtualGrid.SetItems(_fileListManager.Items);
            if (_currentFileIndex >= 0 && _currentFileIndex < _viewableFiles.Count)
                _virtualGrid.SelectByPath(_viewableFiles[_currentFileIndex].FullPath);
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
