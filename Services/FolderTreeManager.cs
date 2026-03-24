using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace leeyez_kai.Services
{
    /// <summary>
    /// TreeViewのフォルダツリー管理
    /// </summary>
    public class FolderTreeManager
    {
        private readonly TreeView _treeView;
        private readonly ImageList _imageList;
        private bool _suppressSelect;
        private readonly HashSet<string> _registeredIconKeys = new();

        public event Action<string>? FolderSelected;

        // 特殊フォルダ定義
        private static readonly (string Name, Environment.SpecialFolder Folder)[] KnownFolders = new[]
        {
            ("デスクトップ", Environment.SpecialFolder.DesktopDirectory),
            ("ダウンロード", (Environment.SpecialFolder)(-1)), // 特殊処理
            ("ドキュメント", Environment.SpecialFolder.MyDocuments),
            ("ピクチャ",     Environment.SpecialFolder.MyPictures),
            ("ビデオ",       Environment.SpecialFolder.MyVideos),
            ("ミュージック", Environment.SpecialFolder.MyMusic),
        };

        public FolderTreeManager(TreeView treeView)
        {
            _treeView = treeView;
            _imageList = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };

            _treeView.ImageList = _imageList;
            _treeView.HideSelection = false;
            _treeView.ShowLines = true;
            _treeView.ShowPlusMinus = true;
            _treeView.ShowRootLines = true;
            _treeView.PathSeparator = "\\";
            _treeView.Font = new Font("MS UI Gothic", 9f);

            _treeView.LabelEdit = true;
            _treeView.BeforeExpand += TreeView_BeforeExpand;
            _treeView.AfterSelect += TreeView_AfterSelect;
            _treeView.AfterLabelEdit += TreeView_AfterLabelEdit;

            // デフォルトフォルダアイコンを登録
            var folderIcon = NativeMethods.GetFolderIcon(false, true);
            if (folderIcon != null)
                _imageList.Images.Add("folder", folderIcon);

            var openFolderIcon = NativeMethods.GetFolderIcon(true, true);
            if (openFolderIcon != null)
                _imageList.Images.Add("folder_open", openFolderIcon);

            // 書庫ファイル用アイコン（zip代表で1回だけ取得）
            var archiveIcon = NativeMethods.GetExtensionIcon(".zip", true);
            if (archiveIcon != null)
                _imageList.Images.Add("archive", archiveIcon);

            // ハートアイコン（お気に入り用）
            var heartBmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(heartBmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(Color.FromArgb(220, 30, 30));
                // 左半円 + 右半円 + 下三角でハート形
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddArc(1, 1, 7, 7, 180, 180);   // 左上の半円
                path.AddArc(8, 1, 7, 7, 180, 180);   // 右上の半円
                path.AddLine(15, 5, 8, 14);           // 右下→底
                path.AddLine(8, 14, 1, 5);            // 底→左下
                path.CloseFigure();
                g.FillPath(brush, path);
            }
            _imageList.Images.Add("heart", heartBmp);

            // PCアイコン（マイコンピュータ CSIDL経由）
            var pcIcon = NativeMethods.GetSpecialFolderIcon(NativeMethods.CSIDL_DRIVES, true);
            if (pcIcon != null)
                _imageList.Images.Add("pc", pcIcon);
        }

        /// <summary>
        /// ツリー全体を構築: お気に入り → 特殊フォルダ → PC(ドライブ)
        /// </summary>
        public void Initialize(List<string> favorites)
        {
            _treeView.BeginUpdate();
            _treeView.Nodes.Clear();

            // 1. お気に入り
            AddFavorites(favorites);

            // 2. 特殊フォルダ (デスクトップ, ダウンロード, ドキュメント, ピクチャ, ビデオ, ミュージック)
            foreach (var (name, folder) in KnownFolders)
            {
                string path;
                if ((int)folder == -1)
                {
                    // ダウンロードフォルダ
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                }
                else
                {
                    path = Environment.GetFolderPath(folder);
                }

                if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;

                var node = CreateFolderNode(name, path);
                // 実際のフォルダアイコンを取得
                SetNodeIcon(node, path);
                _treeView.Nodes.Add(node);
            }

            // 3. PC (ドライブ一覧)
            AddPC();

            _treeView.EndUpdate();
        }

        private void AddFavorites(List<string> favorites)
        {
            var favNode = new TreeNode("お気に入り")
            {
                Tag = "FAVORITES",
                ImageKey = "heart",
                SelectedImageKey = "heart"
            };

            foreach (var fav in favorites)
            {
                var name = Path.GetFileName(fav);
                if (string.IsNullOrEmpty(name)) name = fav;

                TreeNode child;
                if (Directory.Exists(fav))
                {
                    child = CreateFolderNode(name, fav);
                }
                else if (File.Exists(fav))
                {
                    var ext = Path.GetExtension(fav).ToLowerInvariant();
                    var iconKey = FileExtensions.IsArchive(ext)
                        ? (_imageList.Images.ContainsKey("archive") ? "archive" : "folder")
                        : "folder";
                    child = new TreeNode(name) { Tag = fav, ImageKey = iconKey, SelectedImageKey = iconKey };
                }
                else continue; // 存在しないパスはスキップ

                SetNodeIcon(child, fav);
                favNode.Nodes.Add(child);
            }

            // 子がなくても展開ボタンを表示するためダミーを追加
            if (favNode.Nodes.Count == 0)
                favNode.Nodes.Add(new TreeNode("(なし)") { ForeColor = Color.Gray, Tag = "DUMMY_EMPTY" });

            _treeView.Nodes.Add(favNode);
            favNode.Expand();
        }

        private void AddPC()
        {
            // PCノード
            var pcKey = _imageList.Images.ContainsKey("pc") ? "pc" : "folder";
            var pcNode = new TreeNode("PC")
            {
                Tag = "PC",
                ImageKey = pcKey,
                SelectedImageKey = pcKey
            };

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var label = string.IsNullOrEmpty(drive.VolumeLabel)
                    ? $"({drive.Name.TrimEnd('\\')})"
                    : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";

                var driveNode = new TreeNode(label)
                {
                    Tag = drive.RootDirectory.FullName,
                    ImageKey = "folder",
                    SelectedImageKey = "folder"
                };

                // ドライブアイコン
                var driveIcon = NativeMethods.GetFileIcon(drive.RootDirectory.FullName, true);
                if (driveIcon != null)
                {
                    var key = "drive_" + drive.Name.Replace("\\", "");
                    if (!_imageList.Images.ContainsKey(key))
                        _imageList.Images.Add(key, driveIcon);
                    driveNode.ImageKey = key;
                    driveNode.SelectedImageKey = key;
                }

                driveNode.Nodes.Add(new TreeNode("...") { Tag = "DUMMY" });
                pcNode.Nodes.Add(driveNode);
            }

            _treeView.Nodes.Add(pcNode);
            pcNode.Expand();
        }

        /// <summary>
        /// フォルダノードを作成（ダミー子ノード付き）
        /// </summary>
        private TreeNode CreateFolderNode(string name, string path)
        {
            var node = new TreeNode(name)
            {
                Tag = path,
                ImageKey = "folder",
                SelectedImageKey = "folder_open"
            };

            // 常にダミーノードを追加（展開時に実際のサブフォルダを列挙）
            // 起動時のDirectory.GetDirectories呼び出しを排除して高速化
            node.Nodes.Add(new TreeNode("...") { Tag = "DUMMY" });

            return node;
        }

        /// <summary>
        /// ノードにWindows標準アイコンを設定
        /// </summary>
        private void SetNodeIcon(TreeNode node, string path)
        {
            var key = "path_" + path.GetHashCode().ToString("X");
            if (_registeredIconKeys.Contains(key))
            {
                node.ImageKey = key;
                node.SelectedImageKey = key;
                return;
            }

            var icon = NativeMethods.GetFileIcon(path, true);
            if (icon != null)
            {
                if (!_imageList.Images.ContainsKey(key))
                    _imageList.Images.Add(key, icon);
                _registeredIconKeys.Add(key);
                node.ImageKey = key;
                node.SelectedImageKey = key;
            }
        }

        /// <summary>
        /// 指定パスのノードを選択状態にする（特殊フォルダ・書庫パス対応）
        /// </summary>
        public void SelectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            // パスの正規化
            string folderPath = path;
            bool isArchiveInternalPath = false;

            // 書庫内パス判定（後ろから!を探して書庫ファイル存在確認）
            int idx = path.Length;
            while (idx > 0 && (idx = path.LastIndexOf('!', idx - 1)) >= 0)
            {
                var possibleArchive = path.Substring(0, idx);
                if (FileExtensions.IsArchive(FileExtensions.GetExt(possibleArchive)) && File.Exists(possibleArchive))
                {
                    folderPath = Path.GetDirectoryName(possibleArchive) ?? path;
                    isArchiveInternalPath = true;
                    break;
                }
                if (idx == 0) break;
            }

            if (!isArchiveInternalPath)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                bool isArchive = FileExtensions.Archive.Contains(ext);

                if (isArchive && File.Exists(path))
                {
                    // 書庫ファイル → 親フォルダを展開して書庫ノードを選択
                    folderPath = Path.GetDirectoryName(path) ?? path;
                    string archiveFileName = Path.GetFileName(path);

                    _suppressSelect = true;
                    try
                    {
                        var archiveNode = FindArchiveNode(folderPath, archiveFileName);
                        if (archiveNode != null)
                        {
                            _treeView.SelectedNode = archiveNode;
                            archiveNode.EnsureVisible();
                            return;
                        }
                    }
                    finally { _suppressSelect = false; }
                    // 見つからなければ親フォルダを選択（以下にフォールスルー）
                }
                else if (File.Exists(path) && !isArchive)
                {
                    // 通常ファイル → 親フォルダ
                    folderPath = Path.GetDirectoryName(path) ?? path;
                }
            }

            // AfterSelectイベントを一時的に抑制
            _suppressSelect = true;
            try
            {
                // 0. 現在選択中のノードがお気に入り配下なら、お気に入り内を優先検索
                var currentNode = _treeView.SelectedNode;
                if (currentNode != null && IsUnderFavorites(currentNode))
                {
                    var favNode = FindNodeByTag("FAVORITES");
                    if (favNode != null)
                    {
                        foreach (TreeNode child in favNode.Nodes)
                        {
                            var tag = child.Tag?.ToString();
                            if (tag != null && folderPath.Equals(tag, StringComparison.OrdinalIgnoreCase))
                            {
                                _treeView.SelectedNode = child;
                                child.EnsureVisible();
                                return;
                            }
                            if (tag != null && folderPath.StartsWith(tag + "\\", StringComparison.OrdinalIgnoreCase))
                            {
                                string relative = folderPath.Substring(tag.Length).TrimStart('\\', '/');
                                var result = DrillDown(child, relative);
                                if (result != null)
                                {
                                    _treeView.SelectedNode = result;
                                    result.EnsureVisible();
                                    return;
                                }
                            }
                        }
                    }
                }

                // 1. 特殊フォルダノード（ルートレベル）に完全一致するか
                foreach (TreeNode rootNode in _treeView.Nodes)
                {
                    var tag = rootNode.Tag?.ToString();
                    if (tag != null && !tag.StartsWith("FAVOR") && tag != "PC"
                        && folderPath.Equals(tag, StringComparison.OrdinalIgnoreCase))
                    {
                        _treeView.SelectedNode = rootNode;
                        rootNode.EnsureVisible();
                        return;
                    }
                }

                // 2. 特殊フォルダのサブパスか確認
                foreach (TreeNode rootNode in _treeView.Nodes)
                {
                    var tag = rootNode.Tag?.ToString();
                    if (tag != null && !tag.StartsWith("FAVOR") && tag != "PC"
                        && folderPath.StartsWith(tag + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        // 特殊フォルダからの相対パスで追跡
                        string relative = folderPath.Substring(tag.Length).TrimStart('\\', '/');
                        var result = DrillDown(rootNode, relative);
                        if (result != null)
                        {
                            _treeView.SelectedNode = result;
                            result.EnsureVisible();
                            return;
                        }
                    }
                }

                // 3. PC配下のドライブから探す
                TreeNode? pcNode = FindNodeByTag("PC");
                if (pcNode == null) return;

                var parts = folderPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) return;
                string drivePart = parts[0] + "\\";

                TreeNode? driveNode = null;
                foreach (TreeNode child in pcNode.Nodes)
                {
                    var tag = child.Tag?.ToString();
                    if (tag != null && tag.Equals(drivePart, StringComparison.OrdinalIgnoreCase))
                    {
                        driveNode = child;
                        break;
                    }
                }
                if (driveNode == null) return;

                string remainingPath = string.Join("\\", parts.Skip(1));
                var target = DrillDown(driveNode, remainingPath);
                if (target != null)
                {
                    _treeView.SelectedNode = target;
                    target.EnsureVisible();
                }
            }
            finally
            {
                _suppressSelect = false;
            }
        }

        /// <summary>
        /// ノードからrelativePathを辿って子ノードを展開・選択
        /// </summary>
        private TreeNode? DrillDown(TreeNode startNode, string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return startNode;

            var parts = relativePath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            TreeNode current = startNode;

            foreach (var part in parts)
            {
                ExpandNode(current);
                TreeNode? found = null;
                foreach (TreeNode child in current.Nodes)
                {
                    if (child.Text.Equals(part, StringComparison.OrdinalIgnoreCase))
                    {
                        found = child;
                        break;
                    }
                }
                if (found == null) return current; // 途中で見つからなければそこまで
                current = found;
            }
            return current;
        }

        public bool IsSelectedUnderFavorites() => _treeView.SelectedNode != null && IsUnderFavorites(_treeView.SelectedNode);

        private bool IsUnderFavorites(TreeNode node)
        {
            var n = node;
            while (n != null)
            {
                if (n.Tag?.ToString() == "FAVORITES") return true;
                n = n.Parent;
            }
            return false;
        }

        private TreeNode? FindNodeByTag(string tag)
        {
            foreach (TreeNode node in _treeView.Nodes)
            {
                if (node.Tag?.ToString() == tag) return node;
            }
            return null;
        }

        /// <summary>
        /// 指定フォルダ内の書庫ファイルノードを探す（ツリーを展開して検索）
        /// </summary>
        private TreeNode? FindArchiveNode(string parentFolderPath, string archiveFileName)
        {
            // まず親フォルダのノードを見つける
            var parentNode = FindFolderNode(parentFolderPath);
            if (parentNode == null) return null;

            // 親を展開して書庫ノードを探す
            ExpandNode(parentNode);
            foreach (TreeNode child in parentNode.Nodes)
            {
                if (child.Text.Equals(archiveFileName, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }

        /// <summary>
        /// フォルダパスに対応するツリーノードを探す
        /// </summary>
        private TreeNode? FindFolderNode(string folderPath)
        {
            // 特殊フォルダに完全一致
            foreach (TreeNode rootNode in _treeView.Nodes)
            {
                var tag = rootNode.Tag?.ToString();
                if (tag != null && tag != "FAVORITES" && tag != "PC"
                    && folderPath.Equals(tag, StringComparison.OrdinalIgnoreCase))
                    return rootNode;
            }

            // 特殊フォルダのサブパス
            foreach (TreeNode rootNode in _treeView.Nodes)
            {
                var tag = rootNode.Tag?.ToString();
                if (tag != null && tag != "FAVORITES" && tag != "PC"
                    && folderPath.StartsWith(tag + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    string relative = folderPath.Substring(tag.Length).TrimStart('\\', '/');
                    return DrillDown(rootNode, relative);
                }
            }

            // PC配下のドライブから
            var pcNode = FindNodeByTag("PC");
            if (pcNode == null) return null;

            var parts = folderPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            string drivePart = parts[0] + "\\";

            foreach (TreeNode child in pcNode.Nodes)
            {
                var tag = child.Tag?.ToString();
                if (tag != null && tag.Equals(drivePart, StringComparison.OrdinalIgnoreCase))
                {
                    string remaining = string.Join("\\", parts.Skip(1));
                    return DrillDown(child, remaining);
                }
            }
            return null;
        }

        private void TreeView_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
        {
            if (e.Node == null) return;
            ExpandNode(e.Node);
        }

        private void ExpandNode(TreeNode node)
        {
            var nodeTag = node.Tag?.ToString();
            if (nodeTag == "FAVORITES" || nodeTag == "PC") return;

            if (node.Nodes.Count == 1 &&
                (node.Nodes[0].Tag?.ToString() == "DUMMY" || node.Nodes[0].Tag?.ToString() == "DUMMY_EMPTY"))
            {
                node.Nodes.Clear();
                var path = nodeTag;
                if (string.IsNullOrEmpty(path)) return;

                try
                {
                    // EnumerateDirectories: 遅延列挙で高速化（全取得してからソートしない）
                    var dirNodes = new List<TreeNode>();
                    var archiveNodes = new List<TreeNode>();
                    var archiveKey = _imageList.Images.ContainsKey("archive") ? "archive" : "folder";

                    foreach (var dir in Directory.EnumerateDirectories(path))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (string.IsNullOrEmpty(dirName)) continue;
                        dirNodes.Add(CreateFolderNode(dirName, dir));
                    }

                    foreach (var file in Directory.EnumerateFiles(path))
                    {
                        if (!FileExtensions.Archive.Contains(Path.GetExtension(file).ToLowerInvariant()))
                            continue;
                        archiveNodes.Add(new TreeNode(Path.GetFileName(file))
                        {
                            Tag = file,
                            ImageKey = archiveKey,
                            SelectedImageKey = archiveKey
                        });
                    }

                    // 名前順ソートしてまとめて追加
                    dirNodes.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));
                    archiveNodes.Sort((a, b) => string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase));

                    node.Nodes.AddRange(dirNodes.ToArray());
                    node.Nodes.AddRange(archiveNodes.ToArray());
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }

        private void TreeView_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (_suppressSelect) return;
            var path = e.Node?.Tag?.ToString();
            if (!string.IsNullOrEmpty(path) && path != "DUMMY" && path != "FAVORITES" && path != "PC")
            {
                FolderSelected?.Invoke(path);
            }
        }

        private void TreeView_AfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
        {
            if (e.Label == null || e.CancelEdit) return;
            var newName = e.Label.Trim();
            if (string.IsNullOrEmpty(newName)) { e.CancelEdit = true; return; }

            var node = e.Node;
            if (node == null) { e.CancelEdit = true; return; }

            if (!CanRenameNode(node)) { e.CancelEdit = true; return; }

            var path = node.Tag?.ToString();
            if (path == null) { e.CancelEdit = true; return; }

            // ルートレベルの特殊フォルダ（デスクトップ等）は変更不可（二重ガード）
            if (node.Parent == null && path != "FAVORITES" && path != "PC")
            { e.CancelEdit = true; return; }

            try
            {
                var parentDir = Path.GetDirectoryName(path);
                if (parentDir == null) { e.CancelEdit = true; return; }

                var newPath = Path.Combine(parentDir, newName);
                if (newPath == path) { e.CancelEdit = true; return; }

                if (Directory.Exists(path))
                    Directory.Move(path, newPath);
                else if (File.Exists(path))
                    File.Move(path, newPath);
                else
                { e.CancelEdit = true; return; }

                node.Tag = newPath;
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

        /// <summary>選択ノードの名前変更を開始</summary>
        public void BeginRenameNode()
        {
            var node = _treeView.SelectedNode;
            if (node == null) return;
            if (!CanRenameNode(node)) return;
            node.BeginEdit();
        }

        /// <summary>ノードが名前変更可能か判定</summary>
        public bool CanRenameNode(TreeNode? node)
        {
            if (node == null) return false;
            var tag = node.Tag?.ToString();
            // 特殊ノードは不可
            if (tag == "FAVORITES" || tag == "PC" || tag == "DUMMY" || tag == "DUMMY_EMPTY") return false;
            // ルートレベルの特殊フォルダ（デスクトップ等）は不可
            if (node.Parent == null) return false;
            // PCの直下（ドライブ）は不可
            if (node.Parent?.Tag?.ToString() == "PC") return false;
            return true;
        }

        /// <summary>お気に入りにフォルダを追加</summary>
        public void AddFavorite(string path)
        {
            var favNode = FindNodeByTag("FAVORITES");
            if (favNode == null) return;

            // 既に存在するか確認
            foreach (TreeNode child in favNode.Nodes)
            {
                if (child.Tag?.ToString()?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
                    return; // 重複
            }

            // ダミーノード削除
            if (favNode.Nodes.Count == 1 && favNode.Nodes[0].Tag?.ToString() == "DUMMY_EMPTY")
                favNode.Nodes.Clear();

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;

            TreeNode node;
            if (Directory.Exists(path))
            {
                // フォルダ: 展開可能
                node = CreateFolderNode(name, path);
            }
            else
            {
                // ファイル: 展開なし
                var ext = Path.GetExtension(path).ToLowerInvariant();
                var iconKey = FileExtensions.IsArchive(ext)
                    ? (_imageList.Images.ContainsKey("archive") ? "archive" : "folder")
                    : "folder";
                node = new TreeNode(name) { Tag = path, ImageKey = iconKey, SelectedImageKey = iconKey };
            }

            SetNodeIcon(node, path);
            favNode.Nodes.Add(node);
            favNode.Expand();

            SaveFavorites();
        }

        /// <summary>お気に入りからフォルダを削除</summary>
        public void RemoveFavorite(string path)
        {
            var favNode = FindNodeByTag("FAVORITES");
            if (favNode == null) return;

            for (int i = favNode.Nodes.Count - 1; i >= 0; i--)
            {
                if (favNode.Nodes[i].Tag?.ToString()?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
                {
                    favNode.Nodes.RemoveAt(i);
                    break;
                }
            }

            if (favNode.Nodes.Count == 0)
                favNode.Nodes.Add(new TreeNode("(なし)") { ForeColor = Color.Gray, Tag = "DUMMY_EMPTY" });

            SaveFavorites();
        }

        /// <summary>お気に入りリストを保存</summary>
        private void SaveFavorites()
        {
            var favNode = FindNodeByTag("FAVORITES");
            if (favNode == null) return;

            var favorites = new List<string>();
            foreach (TreeNode child in favNode.Nodes)
            {
                var tag = child.Tag?.ToString();
                if (tag != null && tag != "DUMMY_EMPTY")
                    favorites.Add(tag);
            }

            var state = PersistenceService.LoadState() ?? new Models.AppState();
            state.Favorites = favorites;
            PersistenceService.SaveState(state);
        }

        /// <summary>ツリーノードを更新（子ノード再読み込み）</summary>
        public void RefreshNode(TreeNode node)
        {
            var path = node.Tag?.ToString();
            if (string.IsNullOrEmpty(path)) return;

            bool wasExpanded = node.IsExpanded;
            node.Nodes.Clear();
            node.Nodes.Add(new TreeNode("...") { Tag = "DUMMY" });

            if (wasExpanded)
            {
                ExpandNode(node);
                node.Expand();
            }
        }
    }
}
