using System.Collections.Generic;
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
                _nav.NavigateTo(path);
                _addressBox.Text = path;
                UpdateNavButtons();
                AutoSaveState();
                RecordHistory(path);
                if (!_skipSelectPath) _treeManager?.SelectPath(path);

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
            if (path != null) { _addressBox.Text = path; LoadPath(path); UpdateNavButtons(); }
        }

        private void GoForward()
        {
            var path = _nav.GoForward();
            if (path != null) { _addressBox.Text = path; LoadPath(path); UpdateNavButtons(); }
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
                    LoadPath(p);
                    UpdateNavButtons();
                });
                item.Font = _historyFont;
            }
        }
    }
}
