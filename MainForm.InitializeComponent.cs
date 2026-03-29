using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using leeyez_kai.Controls;

namespace leeyez_kai
{
    public partial class MainForm
    {
        private void InitializeComponent()
        {
            SuspendLayout();

            Text = "Leeyez Kai";
            Size = new Size(1200, 800);
            MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Yu Gothic UI", 9f);
            KeyPreview = true;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            var iconFont = FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons")
                ? "Segoe Fluent Icons"
                : FontFamily.Families.Any(f => f.Name == "Segoe MDL2 Assets")
                    ? "Segoe MDL2 Assets"
                    : "Segoe UI Symbol";

            // ── ナビゲーションバー ──
            _navBar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.White,
                Padding = new Padding(4), RenderMode = ToolStripRenderMode.System,
                AutoSize = false, Height = 56, ImageScalingSize = new Size(24, 24)
            };

            _btnBack = new ToolStripSplitButton("\uE72B")
            {
                ToolTipText = "戻る (Alt+←)", AutoSize = false, Size = new Size(56, 44),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font(iconFont, 18f), ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(2), Margin = new Padding(1, 0, 1, 0)
            };
            _btnForward = new ToolStripSplitButton("\uE72A")
            {
                ToolTipText = "進む (Alt+→)", AutoSize = false, Size = new Size(56, 44),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font(iconFont, 18f), ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(2), Margin = new Padding(1, 0, 1, 0)
            };
            _btnUp = CreateNavButton("\uE74A", "上へ (Alt+↑)", iconFont, 18f);
            _btnRefresh = CreateNavButton("\uE72C", "更新 (F5)", iconFont, 18f);
            _btnHoverPreview = CreateNavButton("\uE7B3", "ホバープレビュー", iconFont, 18f);
            _btnBookshelf = CreateNavButton("\uE82D", "本棚", iconFont, 18f);
            _btnHistory = CreateNavButton("\uE81C", "履歴", iconFont, 18f);
            _btnListView = CreateNavButton("\uE8FD", "リスト表示", iconFont, 18f);
            _btnGridView = CreateNavButton("\uE80A", "グリッド表示", iconFont, 18f);
            _btnSettings = CreateNavButton("\uE713", "設定", iconFont, 18f);
            _btnHelp = CreateNavButton("\uE897", "ヘルプ (F1)", iconFont, 18f);

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

            // ── アドレスバー ──
            _addressBarPanel = new Panel
            {
                Dock = DockStyle.Top, Height = 30,
                BackColor = Color.White, Padding = new Padding(4, 2, 4, 2)
            };
            _addressLabel = new Label
            {
                Text = "アドレス(A)", AutoSize = true, Location = new Point(6, 6),
                ForeColor = Color.FromArgb(100, 100, 100), Font = new Font("Yu Gothic UI", 9f)
            };
            _addressBox = new TextBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Location = new Point(80, 3), Height = 24,
                Font = new Font("Yu Gothic UI", 9f), BorderStyle = BorderStyle.FixedSingle
            };
            _addressBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; NavigateTo(_addressBox.Text); }
            };
            _addressBarPanel.Controls.Add(_addressLabel);
            _addressBarPanel.Controls.Add(_addressBox);
            _addressBarPanel.Resize += (s, e) => { _addressBox.Width = _addressBarPanel.Width - 90; };

            // ── メインスプリッター ──
            _mainSplit = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
                SplitterDistance = 300, SplitterWidth = 6, FixedPanel = FixedPanel.Panel1
            };

            // ── サイドバー ──
            _sidebarSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterWidth = 4 };
            _folderTree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Yu Gothic UI", 9f) };
            _bookshelfTree = new TreeView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Yu Gothic UI", 9f), Visible = false };

            // 履歴ツールバー（履歴モード時に表示）
            _historyToolbar = new Panel
            {
                Dock = DockStyle.Top, Height = 28, Visible = false,
                BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0), Padding = new Padding(2)
            };
            var histIcon = new Label { Text = "\uE81C", AutoSize = true, Location = new Point(4, 3), Font = new Font(iconFont, 10f) };
            var histLabel = new Label { Text = "履歴", AutoSize = true, Location = new Point(30, 5), Font = new Font("Yu Gothic UI", 9f) };
            var histBtnClear = new Button { Text = "全削除", FlatStyle = FlatStyle.Flat, Size = new Size(52, 22), Location = new Point(72, 3), Font = new Font("Yu Gothic UI", 8f) };
            histBtnClear.FlatAppearance.BorderSize = 1;
            histBtnClear.Click += (s, e) => ClearAllHistory();
            _historyToolbar.Controls.AddRange(new Control[] { histIcon, histLabel, histBtnClear });

            _historyList = new ListView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Yu Gothic UI", 9f), Visible = false };

            _btnHistory.Click += (s, e) => ToggleHistory();

            // 本棚ツールバー（本棚モード時に表示）
            _bookshelfToolbar = new Panel
            {
                Dock = DockStyle.Top, Height = 28, Visible = false,
                BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0), Padding = new Padding(2)
            };
            var bsIcon = new Label { Text = "📚", AutoSize = true, Location = new Point(4, 3), Font = new Font("Segoe UI", 10f) };
            var bsLabel = new Label { Text = "本棚", AutoSize = true, Location = new Point(30, 5), Font = new Font("Yu Gothic UI", 9f) };
            var bsBtnNew = new Button { Text = "新規", FlatStyle = FlatStyle.Flat, Size = new Size(44, 22), Location = new Point(72, 3), Font = new Font("Yu Gothic UI", 8f) };
            bsBtnNew.FlatAppearance.BorderSize = 1;
            bsBtnNew.Click += (s, e) => BookshelfNewCategory();
            _bookshelfToolbar.Controls.AddRange(new Control[] { bsIcon, bsLabel, bsBtnNew });
            _filterPanel = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(4) };
            _filterBox = new TextBox { Dock = DockStyle.Fill, Font = new Font("Yu Gothic UI", 9f), BorderStyle = BorderStyle.FixedSingle };
            _filterBox.TextChanged += (s, e) => _fileListManager?.SetFilter(_filterBox.Text);
            _filterPanel.Controls.Add(_filterBox);
            _fileList = new ListView { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
            _virtualGrid = new VirtualGridPanel { Dock = DockStyle.Fill, Visible = false };

            var folderLabel = new Panel { Dock = DockStyle.Top, Height = 24, BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0) };
            _sidebarLabel = new Label
            {
                Text = "フォルダ", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 0, 0), Font = new Font("Yu Gothic UI", 9f)
            };
            folderLabel.Controls.Add(_sidebarLabel);

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

            _btnFirst = CreateViewerButton("\uE892", "最初 (Home)", iconFont);
            _btnPrev = CreateViewerButton("\uE76B", "前 (←)", iconFont);
            _pageLabel = new ToolStripLabel("0 / 0") { Font = new Font("Yu Gothic UI", 10f), AutoSize = false, Size = new Size(90, 28), TextAlign = ContentAlignment.MiddleCenter };
            _btnNext = CreateViewerButton("\uE76C", "次 (→)", iconFont);
            _btnLast = CreateViewerButton("\uE893", "最後 (End)", iconFont);
            _btnFitWindow = CreateViewerButton("\uE740", "ウィンドウに合わせる (W)", iconFont);
            _btnFitWidth = new ToolStripButton { ToolTipText = "幅に合わせる", AutoSize = false, Size = new Size(32, 28), DisplayStyle = ToolStripItemDisplayStyle.Image, Image = DrawFitIcon(true), Margin = new Padding(1, 2, 1, 2) };
            _btnFitHeight = new ToolStripButton { ToolTipText = "高さに合わせる", AutoSize = false, Size = new Size(32, 28), DisplayStyle = ToolStripItemDisplayStyle.Image, Image = DrawFitIcon(false), Margin = new Padding(1, 2, 1, 2) };
            _btnZoomIn = CreateViewerButton("\uE8A3", "拡大 (Ctrl++)", iconFont);
            _btnZoomOut = CreateViewerButton("\uE71F", "縮小 (Ctrl+-)", iconFont);
            _zoomLabel = new ToolStripLabel("100%")
            {
                Font = new Font("Yu Gothic UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0x00, 0x78, 0xD4), AutoSize = true
            };
            _btnOriginal = CreateViewerButton("1:1", "原寸", "Yu Gothic UI", 11f);
            _btnBinding = new ToolStripButton("←")
            {
                ToolTipText = "綴じ方向 (B)", AutoSize = false, Size = new Size(36, 30),
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font = new Font("Yu Gothic UI", 15f, FontStyle.Bold),
                ForeColor = Color.FromArgb(60, 60, 60),
                Padding = new Padding(0, 0, 0, 4), Margin = new Padding(1, 0, 1, 0),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _btnAutoView = CreateViewerButton("A", "自動 (3)", "Yu Gothic UI", 11f);
            _btnSingleView = CreateViewerButton("1", "単頁 (1)", "Yu Gothic UI", 11f);
            _btnSpreadView = CreateViewerButton("2", "見開き (2)", "Yu Gothic UI", 11f);

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
                    _statusRight.Text = "ウィンドウ合わせ";
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
            viewerCtx.Items.Add("全画面切替", null, (s, e) => ToggleFullscreen());
            viewerCtx.Items.Add("関連付けで開く", null, (s, e) => OpenWithAssociation());
            viewerCtx.Items.Add("パスをコピー", null, (s, e) => CopyCurrentPath());
            viewerCtx.Items.Add("親フォルダのパスをコピー", null, (s, e) => CopyParentPath());
            viewerCtx.Items.Add("エクスプローラで表示", null, (s, e) => ShowInExplorer());
            _imageViewer.ContextMenuStrip = viewerCtx;

            // ファイルリスト: 右クリックで該当アイテムを選択してからメニュー表示
            var fileCtx = new ContextMenuStrip();
            fileCtx.Items.Add("関連付けで開く", null, (s, e) => OpenSelectedWithAssociation());
            fileCtx.Items.Add("エクスプローラで開く", null, (s, e) => ShowSelectedInExplorer());
            fileCtx.Items.Add("パスをコピー", null, (s, e) => CopySelectedPath());
            fileCtx.Items.Add("ファイル名をコピー", null, (s, e) => CopySelectedFileName());
            fileCtx.Items.Add(new ToolStripSeparator());
            fileCtx.Items.Add("お気に入りに追加", null, (s, e) => AddSelectedToFavorites());
            fileCtx.Items.Add("本棚に追加", null, (s, e) => AddSelectedToBookshelf());
            fileCtx.Items.Add(new ToolStripSeparator());
            fileCtx.Items.Add("名前の変更", null, (s, e) => RenameSelected());
            fileCtx.Items.Add("削除", null, (s, e) => DeleteSelected());

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
            treeCtx.Items.Add("関連付けで開く", null, (s, e) => OpenTreeSelectedWithAssociation());
            treeCtx.Items.Add("エクスプローラで開く", null, (s, e) => ShowTreeSelectedInExplorer());
            treeCtx.Items.Add("パスをコピー", null, (s, e) => CopyTreeSelectedPath());
            treeCtx.Items.Add(new ToolStripSeparator());
            var treeFavAddItem = treeCtx.Items.Add("お気に入りに追加", null, (s, e) => AddTreeSelectedToFavorites());
            var treeFavRemoveItem = treeCtx.Items.Add("お気に入りから削除", null, (s, e) => RemoveTreeSelectedFromFavorites());
            treeCtx.Items.Add("本棚に追加", null, (s, e) => AddTreeSelectedToBookshelf());
            treeCtx.Items.Add(new ToolStripSeparator());
            var treeRenameItem = treeCtx.Items.Add("名前の変更", null, (s, e) => _treeManager?.BeginRenameNode());
            var treeDeleteItem = treeCtx.Items.Add("削除", null, (s, e) => DeleteTreeSelected());

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
    }
}
