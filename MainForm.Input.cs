using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using leeyez_kai.Controls;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm
    {
        // ── 全画面 ──
        private FormWindowState _savedWindowState;
        private Rectangle _savedBounds;
        private bool _isFullscreen;
        private DateTime _lastFullscreenToggle;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_STYLE = -16;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOZORDER = 0x0004;
        private static readonly IntPtr HWND_TOP = IntPtr.Zero;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Escapeは常にハードコード
            if (keyData == Keys.Escape && _isFullscreen) { ToggleFullscreen(); return true; }

            // F2: 名前変更（フォーカスに応じて対象を切替）
            if (keyData == Keys.F2)
            {
                if (_isBookshelfMode && _bookshelfTree.Focused)
                {
                    var node = _bookshelfTree.SelectedNode;
                    if (node != null && node.Tag?.ToString() != "BOOKSHELF_ROOT")
                        node.BeginEdit();
                    return true;
                }
                if (_folderTree.Focused && _treeManager?.CanRenameNode(_folderTree.SelectedNode) == true)
                {
                    _treeManager?.BeginRenameNode();
                    return true;
                }
                if (_fileList.Focused && _fileList.SelectedItems.Count == 1)
                {
                    _fileList.SelectedItems[0].BeginEdit();
                    return true;
                }
            }

            // テキスト入力中は単独キーショートカットを無効化
            bool textFocused = _filterBox.Focused || _addressBox.Focused;

            // ショートカットマネージャーで検索
            var action = _shortcutManager.FindAction(keyData);

            // ZoomIn/Outの代替キー
            if (action == null && keyData == (Keys.Control | Keys.Add)) action = "ZoomIn";
            if (action == null && keyData == (Keys.Control | Keys.Subtract)) action = "ZoomOut";

            if (action != null)
            {
                // テキスト入力中に単独キー（修飾なし）は無効
                if (textFocused && (keyData & Keys.Modifiers) == 0) return base.ProcessCmdKey(ref msg, keyData);

                return ExecuteAction(action);
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private bool ExecuteAction(string actionId)
        {
            switch (actionId)
            {
                case "PrevPage": GoToFile(_currentFileIndex - GetPagesPerView()); return true;
                case "NextPage": GoToFile(_currentFileIndex + GetPagesPerView()); return true;
                case "FirstPage": GoToFile(0); return true;
                case "LastPage": GoToFile(_viewableFiles.Count - 1); return true;
                case "GoBack": GoBack(); return true;
                case "GoForward": GoForward(); return true;
                case "GoUp": GoUp(); return true;
                case "Refresh": Refresh(); return true;
                case "Help": ShowHelp(); return true;
                case "Fullscreen": ToggleFullscreen(); return true;
                case "FitWindow": SetScaleMode(ImageViewer.ScaleMode.FitWindow); return true;
                case "ZoomIn": ZoomStep(AppConstants.ZoomStepPercent); return true;
                case "ZoomOut": ZoomStep(-AppConstants.ZoomStepPercent); return true;
                case "ZoomReset": _imageViewer.Zoom = 1.0f; UpdateZoomLabel(); return true;
                case "Binding": _isRTL = !_isRTL; _btnBinding.Text = _isRTL ? "←" : "→"; ShowCurrentFile(); return true;
                case "SingleView": SetViewMode(1); return true;
                case "SpreadView": SetViewMode(2); return true;
                case "AutoView": SetViewMode(0); return true;
                default: return false;
            }
        }

        private int _savedStyle;

        private void ToggleFullscreen()
        {
            // 連打防止: 150ms以内の再トグルを無視
            var now = DateTime.UtcNow;
            if ((now - _lastFullscreenToggle).TotalMilliseconds < 150) return;
            _lastFullscreenToggle = now;

            if (_isFullscreen)
            {
                // 全画面解除: Win32でスタイルと位置を復元（WindowState不使用）
                _isFullscreen = false;
                SetWindowLong(Handle, GWL_STYLE, _savedStyle);
                SetWindowPos(Handle, HWND_TOP, _savedBounds.X, _savedBounds.Y,
                    _savedBounds.Width, _savedBounds.Height, SWP_FRAMECHANGED | SWP_NOZORDER);

                _navBar.Visible = true;
                _addressBarPanel.Visible = true;
                _statusBar.Visible = true;
                _viewerToolbar.Visible = true;
                _mainSplit.Panel1Collapsed = false;
            }
            else
            {
                // 全画面化: Win32でスタイル除去+画面全体に配置（WindowState不使用）
                _isFullscreen = true;
                _savedStyle = GetWindowLong(Handle, GWL_STYLE);
                _savedBounds = new Rectangle(Left, Top, Width, Height);

                _navBar.Visible = false;
                _addressBarPanel.Visible = false;
                _statusBar.Visible = false;
                _viewerToolbar.Visible = false;
                _mainSplit.Panel1Collapsed = true;

                SetWindowLong(Handle, GWL_STYLE, _savedStyle & ~WS_CAPTION & ~WS_THICKFRAME);
                var screen = Screen.FromControl(this);
                SetWindowPos(Handle, HWND_TOP, screen.Bounds.X, screen.Bounds.Y,
                    screen.Bounds.Width, screen.Bounds.Height, SWP_FRAMECHANGED);
            }
        }

        // ── コンテキストメニューアクション ──
        private void OpenWithAssociation()
        {
            if (_currentFileIndex < 0 || _currentFileIndex >= _viewableFiles.Count) return;
            var file = _viewableFiles[_currentFileIndex];
            if (!file.FullPath.Contains('!'))
                try { Process.Start(new ProcessStartInfo(file.FullPath) { UseShellExecute = true }); } catch { }
        }

        private void CopyCurrentPath()
        {
            if (_currentFileIndex >= 0 && _currentFileIndex < _viewableFiles.Count)
                Clipboard.SetText(_viewableFiles[_currentFileIndex].FullPath);
        }

        private void CopyParentPath()
        {
            if (_currentFileIndex < 0 || _currentFileIndex >= _viewableFiles.Count) return;
            var dir = System.IO.Path.GetDirectoryName(_viewableFiles[_currentFileIndex].FullPath);
            if (dir != null) Clipboard.SetText(dir);
        }

        private void ShowInExplorer()
        {
            if (_currentFileIndex < 0 || _currentFileIndex >= _viewableFiles.Count) return;
            var file = _viewableFiles[_currentFileIndex];
            if (!file.FullPath.Contains('!'))
                try { Process.Start("explorer.exe", $"/select,\"{file.FullPath}\""); } catch { }
        }

        private void OpenSelectedFile()
        {
            if (_fileList.SelectedItems.Count == 1)
            {
                var item = _fileList.SelectedItems[0].Tag as FileItem;
                if (item != null) OnFileDoubleClicked(item);
            }
        }

        private void ShowSelectedInExplorer()
        {
            if (_fileList.SelectedItems.Count != 1) return;
            var item = _fileList.SelectedItems[0].Tag as FileItem;
            if (item != null && !item.FullPath.Contains('!'))
                try { Process.Start("explorer.exe", $"/select,\"{item.FullPath}\""); } catch { }
        }

        private void CopySelectedPath()
        {
            if (_fileList.SelectedItems.Count == 1)
            {
                var item = _fileList.SelectedItems[0].Tag as FileItem;
                if (item != null) Clipboard.SetText(item.FullPath);
            }
        }

        private void RenameSelected()
        {
            if (_fileList.SelectedItems.Count == 1)
                _fileList.SelectedItems[0].BeginEdit();
        }

        /// <summary>ツリーから存在しないノードだけ除去（スクロール位置維持）</summary>
        private void RemoveDeletedTreeNodes()
        {
            var nodesToRemove = new List<TreeNode>();
            CollectDeletedNodes(_folderTree.Nodes, nodesToRemove);
            foreach (var node in nodesToRemove)
                node.Remove();
        }

        private void CollectDeletedNodes(TreeNodeCollection nodes, List<TreeNode> toRemove)
        {
            foreach (TreeNode node in nodes)
            {
                var tag = node.Tag?.ToString();
                if (tag == null || tag == "FAVORITES" || tag == "PC" || tag == "DUMMY" || tag == "DUMMY_EMPTY")
                {
                    CollectDeletedNodes(node.Nodes, toRemove);
                    continue;
                }
                // ファイル/フォルダが存在しなければ削除対象
                if (!System.IO.File.Exists(tag) && !System.IO.Directory.Exists(tag))
                    toRemove.Add(node);
                else
                    CollectDeletedNodes(node.Nodes, toRemove);
            }
        }

        /// <summary>ファイルリストだけ更新（ツリーの位置は動かさない）</summary>
        private void RefreshWithoutTreeMove()
        {
            var currentPath = _nav.CurrentPath;
            if (string.IsNullOrEmpty(currentPath)) return;

            _skipSelectPath = true;
            try
            {
                var split = SplitArchivePath(currentPath);
                if (split != null)
                {
                    // 書庫内: エントリキャッシュをクリアして再読み込み
                    _archiveEntryCache.Remove(split.Value.archive);
                    LoadArchive(split.Value.archive, split.Value.entry.TrimStart('/'));
                }
                else if (FileExtensions.IsArchive(FileExtensions.GetExt(currentPath)) && System.IO.File.Exists(currentPath))
                {
                    _archiveEntryCache.Remove(currentPath);
                    LoadArchive(currentPath, "");
                }
                else if (System.IO.Directory.Exists(currentPath))
                {
                    _fileListManager?.LoadFolder(currentPath);
                    UpdateViewableFiles();
                }

                // ツリーから削除されたノードだけ除去（スクロール位置を維持）
                RemoveDeletedTreeNodes();
            }
            finally { _skipSelectPath = false; }
        }

        /// <summary>削除前にファイルのロックを解放</summary>
        private void ReleaseFileLocks()
        {
            _mediaPlayer.Stop();
            _prefetchCts?.Cancel();
            _archiveStreamCache.Clear();
            _imageCache.Clear();
            ArchiveService.CloseCache();
        }

        private void DeleteSelected()
        {
            if (_fileList.SelectedItems.Count == 0) return;
            var items = _fileList.SelectedItems.Cast<ListViewItem>()
                .Select(lvi => lvi.Tag as FileItem)
                .Where(fi => fi != null && SplitArchivePath(fi.FullPath) == null)
                .ToList();

            if (items.Count == 0) return;
            var result = MessageBox.Show(
                $"{items.Count} 個のアイテムをごみ箱に移動しますか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                ReleaseFileLocks();
                foreach (var item in items) NativeMethods.MoveToRecycleBin(item!.FullPath);
                RefreshWithoutTreeMove();
            }
        }

        // ── ファイルリスト追加アクション ──

        private void OpenSelectedWithAssociation()
        {
            if (_fileList.SelectedItems.Count != 1) return;
            var item = _fileList.SelectedItems[0].Tag as FileItem;
            if (item != null && SplitArchivePath(item.FullPath) == null)
                try { Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true }); } catch { }
        }

        private void CopySelectedFileName()
        {
            if (_fileList.SelectedItems.Count == 1)
            {
                var item = _fileList.SelectedItems[0].Tag as FileItem;
                if (item != null) Clipboard.SetText(item.Name);
            }
        }

        private void AddSelectedToFavorites()
        {
            if (_fileList.SelectedItems.Count != 1) return;
            var item = _fileList.SelectedItems[0].Tag as FileItem;
            if (item == null) return;
            _treeManager?.AddFavorite(item.FullPath);
        }

        private void AddSelectedToBookshelf()
        {
            if (_fileList.SelectedItems.Count != 1) return;
            var item = _fileList.SelectedItems[0].Tag as FileItem;
            if (item == null) return;
            var lvi = _fileList.SelectedItems[0];
            var pt = _fileList.PointToScreen(lvi.Bounds.Location);
            ShowAddToBookshelfMenu(item.FullPath, item.Name, _fileList, _fileList.PointToClient(pt));
        }

        private void DeleteTreeSelected()
        {
            var path = GetTreeSelectedPath();
            if (path == null) return;
            if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path)) return;

            var name = System.IO.Path.GetFileName(path);
            var result = MessageBox.Show(
                $"「{name}」をごみ箱に移動しますか？",
                "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                ReleaseFileLocks();
                NativeMethods.MoveToRecycleBin(path);
                RefreshWithoutTreeMove();
            }
        }

        private void AddTreeSelectedToBookshelf()
        {
            var path = GetTreeSelectedPath();
            if (path == null) return;
            var name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            var node = _folderTree.SelectedNode;
            if (node == null) return;
            var pt = _folderTree.PointToScreen(node.Bounds.Location);
            ShowAddToBookshelfMenu(path, name, _folderTree, _folderTree.PointToClient(pt));
        }

        // ── ツリーアクション ──

        private string? GetTreeSelectedPath()
        {
            var node = _folderTree.SelectedNode;
            if (node == null) return null;
            var tag = node.Tag?.ToString();
            if (tag == "FAVORITES" || tag == "PC" || tag == "DUMMY") return null;
            return tag;
        }

        private void OpenTreeSelectedWithAssociation()
        {
            var path = GetTreeSelectedPath();
            if (path != null && SplitArchivePath(path) == null && System.IO.File.Exists(path))
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        private void OpenTreeSelectedFolder()
        {
            var path = GetTreeSelectedPath();
            if (path != null) NavigateTo(path);
        }

        private void ShowTreeSelectedInExplorer()
        {
            var path = GetTreeSelectedPath();
            if (path == null) return;
            if (System.IO.File.Exists(path))
                try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
            else if (System.IO.Directory.Exists(path))
                try { Process.Start("explorer.exe", $"\"{path}\""); } catch { }
        }

        private void CopyTreeSelectedPath()
        {
            var path = GetTreeSelectedPath();
            if (path != null) Clipboard.SetText(path);
        }

        private void AddTreeSelectedToFavorites()
        {
            var path = GetTreeSelectedPath();
            if (path != null)
                _treeManager?.AddFavorite(path);
        }

        private void RemoveTreeSelectedFromFavorites()
        {
            var node = _folderTree.SelectedNode;
            if (node == null) return;

            // お気に入り配下のノードを探す（直下でなくても祖先にFAVORITESがあればOK）
            var target = node;
            while (target != null)
            {
                if (target.Parent?.Tag?.ToString() == "FAVORITES")
                {
                    var path = target.Tag?.ToString();
                    if (path != null) _treeManager?.RemoveFavorite(path);
                    return;
                }
                target = target.Parent;
            }

            // 選択ノード自体がお気に入り直下ならそれを削除
            if (node.Parent?.Tag?.ToString() == "FAVORITES")
            {
                var path = node.Tag?.ToString();
                if (path != null) _treeManager?.RemoveFavorite(path);
            }
        }

        private void CreateNewFolderInTree()
        {
            var path = GetTreeSelectedPath();
            if (path == null || !System.IO.Directory.Exists(path)) return;

            string newName = "新しいフォルダ";
            string newPath = System.IO.Path.Combine(path, newName);
            int i = 1;
            while (System.IO.Directory.Exists(newPath))
            {
                newName = $"新しいフォルダ ({i++})";
                newPath = System.IO.Path.Combine(path, newName);
            }
            try
            {
                System.IO.Directory.CreateDirectory(newPath);
                // ツリーを更新
                RefreshTreeNode();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"フォルダの作成に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshTreeNode()
        {
            var node = _folderTree.SelectedNode;
            if (node == null) return;
            var path = node.Tag?.ToString();
            if (path != null && path != "FAVORITES" && path != "PC")
            {
                _treeManager?.RefreshNode(node);
            }
        }
    }
}
