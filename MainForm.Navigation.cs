using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm
    {
        public void NavigateTo(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            _isNavigating = true;
            try
            {
                // 前回の書庫デバウンスをキャンセル（通過時の誤発火防止）
                _archiveDebounce?.Stop();

                _nav.NavigateTo(path);
                _addressBox.Text = path;
                UpdateBreadcrumb(path);
                UpdateNavButtons();
                AutoSaveState();
                RecordHistory(path);
                if (!_skipSelectPath && !_isBookshelfMode && !_isHistoryMode) _treeManager?.SelectPath(path);
                if (_isBookshelfMode) SelectBookshelfNode(path);

                var archiveSplit = SplitArchivePath(path);
                if (archiveSplit != null)
                {
                    NavigateToArchiveSplit(path, archiveSplit.Value);
                    return;
                }

                var ext = FileExtensions.GetExt(path);
                if (FileExtensions.IsArchive(ext) && File.Exists(path))
                {
                    NavigateToArchiveFile(path);
                    return;
                }

                if (File.Exists(path) && FileExtensions.IsViewable(ext))
                {
                    NavigateToViewableFile(path);
                    return;
                }

                if (Directory.Exists(path))
                {
                    NavigateToFolder(path);
                }

            }
            finally { _isNavigating = false; }
        }

        /// <summary>書庫内パス（archive!entry）へのナビゲーション</summary>
        private void NavigateToArchiveSplit(string path, (string archive, string entry) split)
        {
            LoadArchive(split.archive, split.entry.TrimStart('/'));
        }

        /// <summary>書庫ファイルへのナビゲーション（デバウンス付き）</summary>
        private void NavigateToArchiveFile(string path)
        {
            _pendingArchivePath = path;
            _pendingSkipSelectPath = _skipSelectPath;
            if (_archiveDebounce == null)
            {
                _archiveDebounce = new System.Windows.Forms.Timer { Interval = 200 };
                _archiveDebounce.Tick += (s2, e2) =>
                {
                    _archiveDebounce.Stop();
                    if (_pendingArchivePath != null)
                    {
                        _skipSelectPath = _pendingSkipSelectPath;
                        OpenArchiveInline(_pendingArchivePath);
                        _skipSelectPath = false;
                    }
                };
            }
            _archiveDebounce.Stop();
            _archiveDebounce.Start();
        }

        /// <summary>画像/動画/音声ファイルを直接開く</summary>
        private void NavigateToViewableFile(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (dir == null) return;

            _currentArchivePath = null;
            _archiveEntries = null;
            _fileListManager?.LoadFolder(dir);
            _folderWatcher?.Watch(dir);
            UpdateViewableFiles();
            _statusLeft.Text = path;

            int idx = _viewableFiles.FindIndex(f => f.FullPath.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _currentFileIndex = idx;
                UpdatePageLabel();
                ShowCurrentFile();
                SyncFileListSelection(idx);
            }
        }

        /// <summary>フォルダへのナビゲーション</summary>
        private void NavigateToFolder(string path)
        {
            _currentArchivePath = null;
            _archiveEntries = null;
            ClearStreamCache();
            _fileListManager?.LoadFolder(path);
            _folderWatcher?.Watch(path);
            UpdateViewableFiles();
            _statusLeft.Text = path;

            if (_viewableFiles.Count == 0)
            {
                _imageViewer.Clear();
                _mediaPlayer.Stop();
                _mediaPlayer.Visible = false;
                _imageViewer.Visible = true;
            }
        }

        private void GoBack()
        {
            var path = _nav.GoBack();
            if (path != null) { _addressBox.Text = path; UpdateBreadcrumb(path); LoadPath(path); UpdateNavButtons(); }
        }

        private void GoForward()
        {
            var path = _nav.GoForward();
            if (path != null) { _addressBox.Text = path; UpdateBreadcrumb(path); LoadPath(path); UpdateNavButtons(); }
        }

        private void GoUp()
        {
            var currentPath = _nav.CurrentPath;
            if (string.IsNullOrEmpty(currentPath)) return;

            var upSplit = SplitArchivePath(currentPath);
            if (upSplit != null)
            {
                var innerPath = upSplit.Value.entry.TrimStart('/');
                if (string.IsNullOrEmpty(innerPath))
                    NavigateTo(Path.GetDirectoryName(upSplit.Value.archive) ?? "");
                else
                {
                    var parentInner = Path.GetDirectoryName(innerPath)?.Replace('\\', '/');
                    NavigateTo(upSplit.Value.archive + "!" + (parentInner ?? ""));
                }
            }
            else
            {
                var parent = Path.GetDirectoryName(currentPath);
                if (parent != null) NavigateTo(parent);
            }
        }

        private void LoadPath(string path)
        {
            var split = SplitArchivePath(path);
            if (split != null)
            {
                LoadArchive(split.Value.archive, split.Value.entry.TrimStart('/'));
                if (!_skipSelectPath) _treeManager?.SelectPath(path);
                return;
            }
            var ext = FileExtensions.GetExt(path);
            if (FileExtensions.IsArchive(ext) && File.Exists(path))
            {
                LoadArchive(path, "");
                if (!_skipSelectPath) _treeManager?.SelectPath(path);
                return;
            }
            if (Directory.Exists(path))
            {
                _currentArchivePath = null;
                _archiveEntries = null;
                ClearStreamCache();
                _fileListManager?.LoadFolder(path);
                _folderWatcher?.Watch(path);
                if (!_skipSelectPath) _treeManager?.SelectPath(path);
                UpdateViewableFiles();
                _statusLeft.Text = path;
            }
        }

        private new void Refresh()
        {
            if (!string.IsNullOrEmpty(_nav.CurrentPath)) LoadPath(_nav.CurrentPath);
        }

        private void UpdateNavButtons()
        {
            _btnBack.Enabled = _nav.CanGoBack;
            _btnForward.Enabled = _nav.CanGoForward;
        }

        private static readonly Font _historyFont = new Font("Yu Gothic UI", 8.5f);

        private void PopulateHistoryDropdown(ToolStripSplitButton button, bool isBack)
        {
            button.DropDownItems.Clear();
            var items = isBack ? _nav.BackHistory : _nav.ForwardHistory;
            if (items.Count == 0) return;

            foreach (var path in items.Take(20))
            {
                var p = path;
                var name = Path.GetFileName(p);
                if (string.IsNullOrEmpty(name)) name = p;
                var item = button.DropDownItems.Add(name, null, (s, e) =>
                {
                    _addressBox.Text = p;
                    UpdateBreadcrumb(p);
                    LoadPath(p);
                    UpdateNavButtons();
                });
                item.Font = _historyFont;
            }
        }

        // ── ブレッドクラム ──

        private void UpdateBreadcrumb(string path)
        {
            _breadcrumbPanel.SuspendLayout();
            // 古いコントロールをDispose（メモリリーク防止）
            while (_breadcrumbPanel.Controls.Count > 0)
            {
                var c = _breadcrumbPanel.Controls[0];
                _breadcrumbPanel.Controls.RemoveAt(0);
                c.Dispose();
            }

            if (string.IsNullOrEmpty(path))
            {
                _breadcrumbPanel.ResumeLayout();
                return;
            }

            // 書庫パスの分離: archive.zip!entry/path
            string? archivePart = null;
            var bangIdx = path.IndexOf('!');
            var fsPath = path;
            if (bangIdx >= 0)
            {
                fsPath = path.Substring(0, bangIdx);
                archivePart = path.Substring(bangIdx + 1);
            }

            // ファイルシステム部分を分割
            var parts = fsPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            // ドライブルート（E:\）の場合、partsは ["E:"] になる
            var accumulated = fsPath.StartsWith("\\\\") ? "\\\\" : "";

            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    var sep = new Label
                    {
                        Text = ">", AutoSize = true, Margin = new Padding(0, 4, 0, 0),
                        ForeColor = Color.FromArgb(150, 150, 150)
                    };
                    _breadcrumbPanel.Controls.Add(sep);
                }

                accumulated += (i == 0 ? "" : "\\") + parts[i];
                var segmentPath = accumulated;
                // ドライブ文字の場合はバックスラッシュ付加 (E: → E:\)
                if (segmentPath.Length == 2 && segmentPath[1] == ':')
                    segmentPath += "\\";

                var btn = new LinkLabel
                {
                    Text = parts[i], AutoSize = true,
                    LinkColor = Color.FromArgb(30, 30, 30),
                    ActiveLinkColor = Color.FromArgb(0, 0x78, 0xD4),
                    LinkBehavior = LinkBehavior.HoverUnderline,
                    Margin = new Padding(0, 3, 0, 0), Padding = new Padding(2, 0, 2, 0)
                };
                var navPath = segmentPath;
                btn.LinkClicked += (s, e) => NavigateTo(navPath);
                _breadcrumbPanel.Controls.Add(btn);
            }

            // 書庫内パス
            if (archivePart != null)
            {
                var archiveParts = archivePart.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                var archiveAccum = fsPath + "!";

                for (int i = 0; i < archiveParts.Length; i++)
                {
                    var sep = new Label
                    {
                        Text = ">", AutoSize = true, Margin = new Padding(0, 4, 0, 0),
                        ForeColor = Color.FromArgb(150, 150, 150)
                    };
                    _breadcrumbPanel.Controls.Add(sep);

                    archiveAccum += (i == 0 ? "" : "/") + archiveParts[i];
                    var navPath = archiveAccum;
                    var btn = new LinkLabel
                    {
                        Text = archiveParts[i], AutoSize = true,
                        LinkColor = Color.FromArgb(30, 30, 30),
                        ActiveLinkColor = Color.FromArgb(0, 0x78, 0xD4),
                        LinkBehavior = LinkBehavior.HoverUnderline,
                        Margin = new Padding(0, 3, 0, 0), Padding = new Padding(2, 0, 2, 0)
                    };
                    btn.LinkClicked += (s, e) => NavigateTo(navPath);
                    _breadcrumbPanel.Controls.Add(btn);
                }
            }

            _breadcrumbPanel.ResumeLayout(true);

            // パネル幅を超える場合、先頭のセグメントを「...」に省略
            TrimBreadcrumb();
        }

        private void TrimBreadcrumb()
        {
            if (_breadcrumbPanel.Controls.Count <= 2) return;

            int totalWidth = 0;
            foreach (Control c in _breadcrumbPanel.Controls)
                totalWidth += c.Width + c.Margin.Horizontal;

            if (totalWidth <= _breadcrumbPanel.Width) return;

            // 先頭から削除して幅に収める（最低1セグメントは残す）
            bool trimmed = false;
            while (totalWidth > _breadcrumbPanel.Width && _breadcrumbPanel.Controls.Count > 2)
            {
                var first = _breadcrumbPanel.Controls[0];
                totalWidth -= first.Width + first.Margin.Horizontal;
                _breadcrumbPanel.Controls.RemoveAt(0);
                first.Dispose();
                trimmed = true;
            }

            // 省略した場合、先頭に「... >」を追加
            if (trimmed && _breadcrumbPanel.Controls.Count > 0)
            {
                var ellipsis = new Label
                {
                    Text = "... >", AutoSize = true, Margin = new Padding(0, 4, 2, 0),
                    ForeColor = Color.FromArgb(150, 150, 150)
                };
                _breadcrumbPanel.Controls.Add(ellipsis);
                _breadcrumbPanel.Controls.SetChildIndex(ellipsis, 0);
            }
        }

        private void ShowAddressEdit()
        {
            _breadcrumbPanel.Visible = false;
            _addressBox.Visible = true;
            _addressBox.Focus();
            _addressBox.SelectAll();
        }

        private void ShowBreadcrumb()
        {
            _addressBox.Visible = false;
            _breadcrumbPanel.Visible = true;
        }
    }
}
