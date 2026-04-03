using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using leeyez_kai.Controls;
using leeyez_kai.i18n;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm
    {
        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "Leeyez Kai";
            var icoPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(icoPath)) try { Icon = new Icon(icoPath); } catch { }
            Size = new Size(1200, 800);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterScreen;
            Font = _fontUI9;
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            var iconFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons")
                ? "Segoe Fluent Icons"
                : FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets")
                    ? "Segoe MDL2 Assets"
                    : "Segoe UI Symbol";
            _fontIcon9 = new Font(iconFont, 9f);
            _fontIcon10 = new Font(iconFont, 10f);
            _fontIcon18 = new Font(iconFont, 18f);

            // ── ナビゲーションバー ──
            _navBar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.White,
                Padding = new Padding(4), RenderMode = ToolStripRenderMode.System,
                AutoSize = false, Height = 56, ImageScalingSize = new Size(24, 24)
            };

            _btnBack = new ToolStripSplitButton("\uE72B")
            {
                ToolTipText = Localization.Get("nav.back"), AutoSize = false, Size = new Size(56, 44),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = _fontIcon18, ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(2), Margin = new Padding(1, 0, 1, 0)
            };
            _btnForward = new ToolStripSplitButton("\uE72A")
            {
                ToolTipText = Localization.Get("nav.forward"), AutoSize = false, Size = new Size(56, 44),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = _fontIcon18, ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(2), Margin = new Padding(1, 0, 1, 0)
            };
            _btnUp = CreateNavButton("\uE74A", Localization.Get("nav.up"), iconFont, 18f);
            _btnRefresh = CreateNavButton("\uE72C", Localization.Get("nav.refresh"), iconFont, 18f);
            _btnHoverPreview = CreateNavButton("\uE7B3", Localization.Get("nav.hover"), iconFont, 18f);
            _btnBookshelf = CreateNavButton("\uE82D", Localization.Get("nav.bookshelf"), iconFont, 18f);
            _btnHistory = CreateNavButton("\uE81C", Localization.Get("history.label"), iconFont, 18f);
            _btnListView = CreateNavButton("\uE8FD", Localization.Get("nav.list"), iconFont, 18f);
            _btnGridView = CreateNavButton("\uE80A", Localization.Get("nav.grid"), iconFont, 18f);
            _btnSettings = CreateNavButton("\uE713", Localization.Get("nav.settings"), iconFont, 18f);
            _btnHelp = CreateNavButton("\uE897", Localization.Get("nav.help"), iconFont, 18f);

            _navBar.Items.AddRange(new ToolStripItem[] {
                _btnBack, _btnForward, _btnUp, _btnRefresh,
                new ToolStripSeparator(),
                _btnHoverPreview, _btnBookshelf, _btnHistory,
                new ToolStripSeparator(),
                _btnListView, _btnGridView,
                new ToolStripSeparator(),
                _btnSettings, _btnHelp
            });

            _btnBack.ButtonClick += (s, e) => GoBack();
            _btnBack.DropDownOpening += (s, e) => PopulateHistoryDropdown(_btnBack, true);
            _btnForward.ButtonClick += (s, e) => GoForward();
            _btnForward.DropDownOpening += (s, e) => PopulateHistoryDropdown(_btnForward, false);
            _btnUp.Click += (s, e) => GoUp();
            _btnRefresh.Click += (s, e) => Refresh();
            _btnListView.Click += (s, e) => SwitchToListView();
            _btnGridView.Click += (s, e) => SwitchToGridView();
            _btnSettings.Click += (s, e) => ShowSettings();
            _btnHelp.Click += (s, e) => ShowHelp();

            // ── アドレスバー（ブレッドクラム + テキスト編集切替） ──
            _addressBarPanel = new Panel
            {
                Dock = DockStyle.Top, Height = 30,
                BackColor = Color.White, Padding = new Padding(4, 2, 4, 2)
            };
            _addressLabel = new Label
            {
                Text = Localization.Get("sidebar.address"), AutoSize = true, Dock = DockStyle.Left,
                ForeColor = Color.FromArgb(100, 100, 100), Font = _fontUI9,
                Padding = new Padding(2, 4, 4, 0)
            };
            _addressBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = _fontUI9, BorderStyle = BorderStyle.FixedSingle,
                Visible = false
            };
            _addressBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; NavigateTo(_addressBox.Text); ShowBreadcrumb(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; ShowBreadcrumb(); }
            };
            _addressBox.LostFocus += (s, e) => ShowBreadcrumb();

            _breadcrumbPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = false,
                WrapContents = false, BackColor = Color.White,
                Font = _fontUI9,
                Padding = new Padding(0), Margin = new Padding(0)
            };
            _breadcrumbPanel.MouseDown += (s, e) =>
            {
                // リンクラベル上のクリックは無視（リンクのNavigateToと競合防止）
                if (_breadcrumbPanel.GetChildAtPoint(e.Location) == null)
                    ShowAddressEdit();
            };

            _addressBarPanel.Controls.Add(_addressBox);
            _addressBarPanel.Controls.Add(_breadcrumbPanel);
            _addressBarPanel.Controls.Add(_addressLabel);

            // ── メインスプリッター ──
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
                SplitterDistance = 300, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1
            };

            // ── サイドバー ──
            _sidebarSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 4 };
            _folderTree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = _fontUI9 };
            _bookshelfTree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = _fontUI9, Visible = false };

            // 履歴ツールバー（履歴モード時に表示）
            _historyToolbar = new Panel
            {
                Dock = DockStyle.Top, Height = 28, Visible = false,
                BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0), Padding = new Padding(2)
            };
            var histIcon = new Label { Text = "\uE81C", AutoSize = true, Location = new Point(4, 3), Font = _fontIcon10 };
            _historyLabel = new Label { Text = Localization.Get("history.label"), AutoSize = true, Location = new Point(30, 5), Font = _fontUI9 };
            _historyBtnClear = new Button { Text = Localization.Get("history.clear"), FlatStyle = FlatStyle.Flat, Size = new Size(52, 22), Location = new Point(72, 3), Font = _fontUI8 };
            _historyBtnClear.FlatAppearance.BorderSize = 1;
            _historyBtnClear.Click += (s, e) => ClearAllHistory();
            _historyFilterBox = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Location = new Point(130, 3), Height = 22, Width = 120,
                Font = _fontUI85, BorderStyle = BorderStyle.FixedSingle
            };
            _historyFilterBox.TextChanged += (s, e) => { _historyFilter = _historyFilterBox.Text; BuildHistoryList(); };
            _historyFilterBox.HandleCreated += (s, e) => SetPlaceholder(_historyFilterBox, Localization.Get("sidebar.filter"));
            _historyToolbar.Resize += (s, e) => { _historyFilterBox.Width = _historyToolbar.Width - 155; };
            _historyToolbar.Controls.AddRange(new Control[] { histIcon, _historyLabel, _historyBtnClear, _historyFilterBox });

            _historyList = new ListView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = _fontUI9, Visible = false };

            _btnHistory.Click += (s, e) => ToggleHistory();

            // 本棚ツールバー（本棚モード時に表示）
            _bookshelfToolbar = new Panel
            {
                Dock = DockStyle.Top, Height = 28, Visible = false,
                BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0), Padding = new Padding(2)
            };
            var bsIcon = new Label { Text = "📚", AutoSize = true, Location = new Point(4, 3), Font = _fontSegoe10 };
            var bsLabel = new Label { Text = Localization.Get("sidebar.bookshelf"), AutoSize = true, Location = new Point(30, 5), Font = _fontUI9 };
            var bsBtnNew = new Button { Text = Localization.Get("sidebar.new"), FlatStyle = FlatStyle.Flat, Size = new Size(44, 22), Location = new Point(72, 3), Font = _fontUI8 };
            bsBtnNew.FlatAppearance.BorderSize = 1;
            bsBtnNew.Click += (s, e) => BookshelfNewCategory();
            _bookshelfToolbar.Controls.AddRange(new Control[] { bsIcon, bsLabel, bsBtnNew });
            _filterPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(4, 4, 20, 4) };
            _filterBox = new TextBox { Dock = DockStyle.Fill, Font = _fontUI9, BorderStyle = BorderStyle.FixedSingle };
            _filterBox.TextChanged += (s, e) => _fileListManager?.SetFilter(_filterBox.Text);
            _filterBox.HandleCreated += (s, e) => SetPlaceholder(_filterBox, Localization.Get("sidebar.filter"));
            _filterPanel.Controls.Add(_filterBox);
            _fileList = new ListView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
            _virtualGrid = new VirtualGridPanel { Dock = DockStyle.Fill, Visible = false };

            var folderLabel = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0) };
            _sidebarLabel = new Label
            {
                Text = Localization.Get("sidebar.folder"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0), Font = _fontUI9
            };
            // ツリーソートボタン + ドロップダウン
            _treeSortMenu = new ContextMenuStrip();
            var sortByName = new ToolStripMenuItem(Localization.Get("sort.name"));
            var sortByDate = new ToolStripMenuItem(Localization.Get("sort.modified"));
            var sortBySize = new ToolStripMenuItem(Localization.Get("sort.size"));
            var sortByType = new ToolStripMenuItem(Localization.Get("sort.type"));
            var sortAsc = new ToolStripMenuItem(Localization.Get("sort.asc"));
            var sortDesc = new ToolStripMenuItem(Localization.Get("sort.desc"));
            sortByName.Click += (s, e) => ApplyTreeSort(TreeSortMode.Name);
            sortByDate.Click += (s, e) => ApplyTreeSort(TreeSortMode.LastModified);
            sortBySize.Click += (s, e) => ApplyTreeSort(TreeSortMode.Size);
            sortByType.Click += (s, e) => ApplyTreeSort(TreeSortMode.Type);
            sortAsc.Click += (s, e) => ApplyTreeSortDirection(false);
            sortDesc.Click += (s, e) => ApplyTreeSortDirection(true);
            _treeSortMenu.Items.AddRange(new ToolStripItem[] {
                sortByName, sortByDate, sortBySize, sortByType, new ToolStripSeparator(), sortAsc, sortDesc
            });
            _treeSortMenu.Opening += (s, e) =>
            {
                var mode = _treeManager?.SortMode ?? TreeSortMode.Name;
                var desc = _treeManager?.SortDescending ?? false;
                sortByName.Checked = mode == TreeSortMode.Name;
                sortByDate.Checked = mode == TreeSortMode.LastModified;
                sortBySize.Checked = mode == TreeSortMode.Size;
                sortByType.Checked = mode == TreeSortMode.Type;
                sortAsc.Checked = !desc;
                sortDesc.Checked = desc;
            };

            _treeSortBtn = new Button
            {
                Text = "\uE8CB", Font = _fontIcon9,
                Dock = DockStyle.Right, Width = 24, FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            _treeSortBtn.FlatAppearance.BorderSize = 0;
            _treeSortBtn.Click += (s, e) => _treeSortMenu.Show(_treeSortBtn, new Point(0, _treeSortBtn.Height));

            folderLabel.Controls.Add(_sidebarLabel);
            folderLabel.Controls.Add(_treeSortBtn);

            // WinForms Dock順: 最後に追加したものが先にDock領域を確保
            // Fill は最初に追加、Top は後に追加
            _sidebarSplit.Panel1.Controls.Add(_folderTree);      // Fill（通常時表示）
            _sidebarSplit.Panel1.Controls.Add(_bookshelfTree);   // Fill（本棚時表示）
            _sidebarSplit.Panel1.Controls.Add(_historyList);     // Fill（履歴時表示）
            _sidebarSplit.Panel1.Controls.Add(_bookshelfToolbar); // Top（本棚時表示）
            _sidebarSplit.Panel1.Controls.Add(_historyToolbar);  // Top（履歴時表示）
            _sidebarSplit.Panel1.Controls.Add(folderLabel);       // Top（常に表示）
            _sidebarSplit.Panel2.Controls.Add(_fileList);
            _sidebarSplit.Panel2.Controls.Add(_virtualGrid);
            _sidebarSplit.Panel2.Controls.Add(_filterPanel);
            _mainSplit.Panel1.Controls.Add(_sidebarSplit);

            // ── ビューアーエリア ──
            _viewerPanel = new Panel { Dock = DockStyle.Fill };

            _viewerToolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System,
                BackColor = Color.White, AutoSize = false, Height = 36
            };

            _btnFirst = CreateViewerButton("\uE892", Localization.Get("viewer.first"), iconFont);
            _btnPrev = CreateViewerButton("\uE76B", Localization.Get("viewer.prev"), iconFont);
            _pageLabel = new ToolStripLabel("0 / 0") { Font = _fontUI10, AutoSize = false, Size = new Size(90, 28), TextAlign = ContentAlignment.MiddleCenter };
            _btnNext = CreateViewerButton("\uE76C", Localization.Get("viewer.next"), iconFont);
            _btnLast = CreateViewerButton("\uE893", Localization.Get("viewer.last"), iconFont);
            _btnFitWindow = CreateViewerButton("\uE740", Localization.Get("viewer.fitwindow"), iconFont);
            _btnFitWidth = new ToolStripButton { ToolTipText = Localization.Get("viewer.fitwidth"), AutoSize = false, Size = new Size(32, 28), DisplayStyle = ToolStripItemDisplayStyle.Image, Image = DrawFitIcon(true), Margin = new Padding(1, 2, 1, 2) };
            _btnFitHeight = new ToolStripButton { ToolTipText = Localization.Get("viewer.fitheight"), AutoSize = false, Size = new Size(32, 28), DisplayStyle = ToolStripItemDisplayStyle.Image, Image = DrawFitIcon(false), Margin = new Padding(1, 2, 1, 2) };
            _btnZoomIn = CreateViewerButton("\uE8A3", Localization.Get("viewer.zoomin"), iconFont);
            _btnZoomOut = CreateViewerButton("\uE71F", Localization.Get("viewer.zoomout"), iconFont);
            _zoomLabel = new ToolStripLabel("100%")
            {
                Font = _fontUI10B,
                ForeColor = Color.FromArgb(0x00, 0x78, 0xD4), AutoSize = true,
                IsLink = false
            };
            _zoomLabel.Click += (s, e) => { _imageViewer.Zoom = 1.0f; UpdateZoomLabel(); };
            _btnOriginal = CreateViewerButton("1:1", Localization.Get("viewer.original"), "Yu Gothic UI", 11f);
            _btnBinding = new ToolStripButton("←")
            {
                ToolTipText = Localization.Get("viewer.binding"), AutoSize = false, Size = new Size(36, 30),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = _fontUI15B,
                ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(0, 0, 0, 4), Margin = new Padding(1, 0, 1, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnAutoView = CreateViewerButton("A", Localization.Get("viewer.auto"), "Yu Gothic UI", 11f);
            _btnSingleView = CreateViewerButton("1", Localization.Get("viewer.single"), "Yu Gothic UI", 11f);
            _btnSpreadView = CreateViewerButton("2", Localization.Get("viewer.spread"), "Yu Gothic UI", 11f);

            _viewerToolbar.Items.AddRange(new ToolStripItem[] {
                _btnFirst, _btnPrev, _pageLabel, _btnNext, _btnLast,
            });

            _btnFitWindow.Alignment = ToolStripItemAlignment.Right;
            _btnFitWidth.Alignment = ToolStripItemAlignment.Right;
            _btnFitHeight.Alignment = ToolStripItemAlignment.Right;
            _btnOriginal.Alignment = ToolStripItemAlignment.Right;
            _btnZoomIn.Alignment = ToolStripItemAlignment.Right;
            _zoomLabel.Alignment = ToolStripItemAlignment.Right;
            _btnZoomOut.Alignment = ToolStripItemAlignment.Right;
            _btnBinding.Alignment = ToolStripItemAlignment.Right;
            _btnAutoView.Alignment = ToolStripItemAlignment.Right;
            _btnSingleView.Alignment = ToolStripItemAlignment.Right;
            _btnSpreadView.Alignment = ToolStripItemAlignment.Right;

            var sepR1 = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };
            var sepR2 = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };
            var sepR3 = new ToolStripSeparator { Alignment = ToolStripItemAlignment.Right };

            _viewerToolbar.Items.AddRange(new ToolStripItem[] {
                _btnSpreadView, _btnSingleView, _btnAutoView,
                sepR1, _btnBinding,
                sepR2, _btnZoomOut, _zoomLabel, _btnZoomIn,
                sepR3, _btnOriginal, _btnFitHeight, _btnFitWidth, _btnFitWindow,
            });

            _btnFirst.Click += (s, e) => GoToFile(0);
            _btnPrev.Click += (s, e) => GoToFile(_currentFileIndex - GetPagesPerView());
            _btnNext.Click += (s, e) => GoToFile(_currentFileIndex + GetPagesPerView());
            _btnLast.Click += (s, e) => GoToFile(_viewableFiles.Count - 1);
            _btnFitWindow.Click += (s, e) => SetScaleMode(ImageViewer.ScaleMode.FitWindow);
            _btnFitWidth.Click += (s, e) => SetScaleMode(ImageViewer.ScaleMode.FitWidth);
            _btnFitHeight.Click += (s, e) => SetScaleMode(ImageViewer.ScaleMode.FitHeight);
            _btnOriginal.Click += (s, e) => SetScaleMode(ImageViewer.ScaleMode.Original);
            _btnZoomIn.Click += (s, e) => ZoomStep(AppConstants.ZoomStepPercent);
            _btnZoomOut.Click += (s, e) => ZoomStep(-AppConstants.ZoomStepPercent);
            _btnBinding.Click += (s, e) => { _isRTL = !_isRTL; _btnBinding.Text = _isRTL ? "←" : "→"; ShowCurrentFile(); };
            _btnAutoView.Click += (s, e) => SetViewMode(0);
            _btnSingleView.Click += (s, e) => SetViewMode(1);
            _btnSpreadView.Click += (s, e) => SetViewMode(2);

            // ImageViewer
            _imageViewer = new ImageViewer { Dock = DockStyle.Fill };
            _imageViewer.StatusChanged += (msg) => _statusRight.Text = msg;
            _imageViewer.ImageSizeChanged += (w, h) =>
            {
                if (_currentFileIndex >= 0 && _currentFileIndex < _viewableFiles.Count)
                {
                    var fi = _viewableFiles[_currentFileIndex];
                    _statusLeft.Text = fi.FullPath;
                    var dimLabel = _statusBar.Items["statusDimension"];
                    var sizeLabel = _statusBar.Items["statusFileSize"];
                    if (dimLabel != null) dimLabel.Text = $"{w} × {h}";
                    if (sizeLabel != null) sizeLabel.Text = fi.SizeString;
                    _statusRight.Text = Localization.Get("viewer.fitwindow");
                }
            };
            _imageViewer.WheelNavigate += (delta) => GoToFile(_currentFileIndex + delta * GetPagesPerView());
            _imageViewer.DoubleClickToggleFullscreen += () => ToggleFullscreen();

            // MediaPlayer
            _mediaPlayer = new FFmpegPlayerPanel { Dock = DockStyle.Fill, Visible = false };
            _mediaPlayer.FullscreenRequested += () => ToggleFullscreen();
            _mediaPlayer.WheelNavigate += (delta) => GoToFile(_currentFileIndex + delta * GetPagesPerView());

            _viewerPanel.Controls.Add(_imageViewer);
            _viewerPanel.Controls.Add(_mediaPlayer);
            _viewerPanel.Controls.Add(_viewerToolbar);
            _mainSplit.Panel2.Controls.Add(_viewerPanel);

            // ── ステータスバー ──
            _statusBar = new StatusStrip { BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0), SizingGrip = true };
            _statusLeft = new ToolStripStatusLabel
            {
                Spring = true, TextAlign = ContentAlignment.MiddleLeft,
                BorderSides = ToolStripStatusLabelBorderSides.Right, BorderStyle = Border3DStyle.Etched
            };
            _statusBar.Items.Add(_statusLeft);
            _statusBar.Items.Add(new ToolStripStatusLabel
            {
                AutoSize = false, Width = 120, TextAlign = ContentAlignment.MiddleCenter,
                BorderSides = ToolStripStatusLabelBorderSides.Right, BorderStyle = Border3DStyle.Etched,
                Name = "statusDimension"
            });
            _statusBar.Items.Add(new ToolStripStatusLabel
            {
                AutoSize = false, Width = 80, TextAlign = ContentAlignment.MiddleCenter,
                BorderSides = ToolStripStatusLabelBorderSides.Right, BorderStyle = Border3DStyle.Etched,
                Name = "statusFileSize"
            });
            _statusRight = new ToolStripStatusLabel { AutoSize = false, Width = 150, TextAlign = ContentAlignment.MiddleCenter };
            _statusBar.Items.Add(_statusRight);

            // ── レイアウト ──
            Controls.Add(_mainSplit);
            Controls.Add(_addressBarPanel);
            Controls.Add(_navBar);
            Controls.Add(_statusBar);

            // コンテキストメニュー
            var viewerCtx = new ContextMenuStrip();
            viewerCtx.Items.Add(Localization.Get("menu.fullscreen"), null, (s, e) => ToggleFullscreen());
            var viewerOpenWith = viewerCtx.Items.Add(Localization.Get("menu.openwith"), null, (s, e) => OpenWithAssociation());
            viewerCtx.Items.Add(Localization.Get("menu.copypath"), null, (s, e) => CopyCurrentPath());
            viewerCtx.Items.Add(Localization.Get("menu.copyparentpath"), null, (s, e) => CopyParentPath());
            var viewerExplorer = viewerCtx.Items.Add(Localization.Get("menu.openexplorer"), null, (s, e) => ShowInExplorer());
            viewerCtx.Opening += (s, e) =>
            {
                bool inArchive = _currentArchivePath != null;
                viewerOpenWith.Visible = !inArchive;
                viewerExplorer.Visible = !inArchive;
            };
            _imageViewer.ContextMenuStrip = viewerCtx;

            // ファイルリスト: 右クリックで該当アイテムを選択してからメニュー表示
            var fileCtx = new ContextMenuStrip();
            var fileOpenWith = fileCtx.Items.Add(Localization.Get("menu.openwith"), null, (s, e) => OpenSelectedWithAssociation());
            var fileExplorer = fileCtx.Items.Add(Localization.Get("menu.openexplorer"), null, (s, e) => ShowSelectedInExplorer());
            fileCtx.Items.Add(Localization.Get("menu.copypath"), null, (s, e) => CopySelectedPath());
            fileCtx.Items.Add(Localization.Get("menu.copyfilename"), null, (s, e) => CopySelectedFileName());
            var fileSep1 = new ToolStripSeparator(); fileCtx.Items.Add(fileSep1);
            var fileFav = fileCtx.Items.Add(Localization.Get("menu.addfavorite"), null, (s, e) => AddSelectedToFavorites());
            var fileShelf = fileCtx.Items.Add(Localization.Get("menu.addbookshelf"), null, (s, e) => AddSelectedToBookshelf());
            var fileSep2 = new ToolStripSeparator(); fileCtx.Items.Add(fileSep2);
            var fileRename = fileCtx.Items.Add(Localization.Get("menu.rename"), null, (s, e) => RenameSelected());
            var fileDelete = fileCtx.Items.Add(Localization.Get("menu.delete"), null, (s, e) => DeleteSelected());
            fileCtx.Opening += (s, e) =>
            {
                bool inArchive = _currentArchivePath != null;
                fileOpenWith.Visible = !inArchive;
                fileExplorer.Visible = !inArchive;
                fileSep1.Visible = !inArchive;
                fileFav.Visible = !inArchive;
                fileShelf.Visible = !inArchive;
                fileSep2.Visible = !inArchive;
                fileRename.Visible = !inArchive;
                fileDelete.Visible = !inArchive;
            };

            _fileList.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var hit = _fileList.HitTest(e.Location);
                    if (hit.Item != null)
                    {
                        hit.Item.Selected = true;
                        hit.Item.Focused = true;
                    }
                }
            };
            _fileList.ContextMenuStrip = fileCtx;

            // ツリー: 右クリックメニュー
            var treeCtx = new ContextMenuStrip();
            treeCtx.Items.Add(Localization.Get("menu.openwith"), null, (s, e) => OpenTreeSelectedWithAssociation());
            treeCtx.Items.Add(Localization.Get("menu.openexplorer"), null, (s, e) => ShowTreeSelectedInExplorer());
            treeCtx.Items.Add(Localization.Get("menu.copypath"), null, (s, e) => CopyTreeSelectedPath());
            treeCtx.Items.Add(new ToolStripSeparator());
            var treeFavAddItem = treeCtx.Items.Add(Localization.Get("menu.addfavorite"), null, (s, e) => AddTreeSelectedToFavorites());
            var treeFavRemoveItem = treeCtx.Items.Add(Localization.Get("menu.removefavorite"), null, (s, e) => RemoveTreeSelectedFromFavorites());
            treeCtx.Items.Add(Localization.Get("menu.addbookshelf"), null, (s, e) => AddTreeSelectedToBookshelf());
            treeCtx.Items.Add(new ToolStripSeparator());
            var treeRenameItem = treeCtx.Items.Add(Localization.Get("menu.rename"), null, (s, e) => _treeManager?.BeginRenameNode());
            var treeDeleteItem = treeCtx.Items.Add(Localization.Get("menu.delete"), null, (s, e) => DeleteTreeSelected());

            treeCtx.Opening += (s, e) =>
            {
                bool canRename = _treeManager?.CanRenameNode(_folderTree.SelectedNode) == true;
                bool isFav = _treeManager?.IsSelectedUnderFavorites() == true;
                treeRenameItem.Visible = canRename;
                treeFavAddItem.Visible = !isFav;
                treeFavRemoveItem.Visible = isFav;
                // 削除: 名前変更可能かつお気に入り配下でないノードのみ
                treeDeleteItem.Visible = canRename && !isFav;
            };

            _folderTree.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var node = _folderTree.GetNodeAt(e.Location);
                    if (node != null) _folderTree.SelectedNode = node;
                }
            };
            _folderTree.ContextMenuStrip = treeCtx;

            ResumeLayout(true);

            // 初期ハイライト
            UpdateScaleModeHighlight(ImageViewer.ScaleMode.FitWindow);
            UpdateViewModeHighlight();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
        private const uint EM_SETCUEBANNER = 0x1501;

        private static void SetPlaceholder(TextBox textBox, string text)
        {
            SendMessage(textBox.Handle, EM_SETCUEBANNER, (IntPtr)1, text);
        }
    }
}
