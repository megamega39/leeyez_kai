using System;
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
        private void SetupBookshelf()
        {
            _bookshelfTree.HideSelection = false;
            _bookshelfTree.ShowLines = true;
            _bookshelfTree.ShowPlusMinus = true;
            _bookshelfTree.ShowRootLines = true;
            _bookshelfTree.LabelEdit = true;
            _bookshelfTree.ImageList = _folderTree.ImageList;

            _bookshelfTree.AfterSelect += BookshelfTree_AfterSelect;
            _bookshelfTree.AfterLabelEdit += BookshelfTree_AfterLabelEdit;

            _btnBookshelf.Click += (s, e) => ToggleBookshelf();

            // 本棚ツリーの右クリックメニュー
            var bookshelfCtx = new ContextMenuStrip();
            bookshelfCtx.Items.Add("開く", null, (s, e) => OpenBookshelfSelected());
            bookshelfCtx.Items.Add("関連付けで開く", null, (s, e) => OpenBookshelfWithAssociation());
            bookshelfCtx.Items.Add("エクスプローラで開く", null, (s, e) => ShowBookshelfInExplorer());
            bookshelfCtx.Items.Add(new ToolStripSeparator());
            bookshelfCtx.Items.Add("名前の変更", null, (s, e) => RenameBookshelfFile());
            bookshelfCtx.Items.Add("本棚から解除", null, (s, e) => DeleteBookshelfSelected());

            _bookshelfTree.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var node = _bookshelfTree.GetNodeAt(e.Location);
                    if (node != null) _bookshelfTree.SelectedNode = node;
                }
            };
            _bookshelfTree.ContextMenuStrip = bookshelfCtx;
        }

        private void ToggleBookshelf()
        {
            _isBookshelfMode = !_isBookshelfMode;
            if (_isBookshelfMode) _isHistoryMode = false;

            // サイドバーのPanel1を操作
            var panel = _sidebarSplit.Panel1;
            panel.SuspendLayout();

            if (_isBookshelfMode)
            {
                BuildBookshelfTree();
                _folderTree.Visible = false;
                _historyList.Visible = false;
                _historyToolbar.Visible = false;
                _bookshelfToolbar.Visible = true;
                _bookshelfTree.Visible = true;
                _btnHistory.BackColor = Color.Transparent;

                // Dock順序を再設定（Fill→Top→Top の順）
                panel.Controls.SetChildIndex(_bookshelfTree, 0);   // Fill: 最背面
                panel.Controls.SetChildIndex(_bookshelfToolbar, 1); // Top: ツリーの上

                _sidebarLabel.Parent.Visible = false; // ヘッダーラベルを非表示（ツールバーが兼ねる）
                _btnBookshelf.BackColor = Color.FromArgb(0xD0, 0xD0, 0xD0);
            }
            else
            {
                _bookshelfTree.Visible = false;
                _bookshelfToolbar.Visible = false;
                _folderTree.Visible = true;

                panel.Controls.SetChildIndex(_folderTree, 0);

                _sidebarLabel.Parent.Visible = true; // ヘッダーラベルを再表示
                _sidebarLabel.Text = "フォルダ";
                _btnBookshelf.BackColor = Color.Transparent;
            }

            panel.ResumeLayout(true);
        }

        private void BuildBookshelfTree()
        {
            _bookshelfTree.BeginUpdate();
            _bookshelfTree.Nodes.Clear();

            // ルートノード「本棚」
            var rootNode = new TreeNode("本棚")
            {
                Tag = "BOOKSHELF_ROOT",
                ImageKey = "folder",
                SelectedImageKey = "folder_open"
            };

            // カテゴリ
            foreach (var cat in _bookshelfService.Categories)
            {
                var catNode = new TreeNode(cat.Name)
                {
                    Tag = "CAT:" + cat.Id,
                    ImageKey = "folder",
                    SelectedImageKey = "folder_open"
                };

                foreach (var item in cat.Items)
                {
                    catNode.Nodes.Add(CreateBookshelfItemNode(item));
                }
                rootNode.Nodes.Add(catNode);
                catNode.Expand();
            }

            // 未分類
            foreach (var item in _bookshelfService.UncategorizedItems)
            {
                rootNode.Nodes.Add(CreateBookshelfItemNode(item));
            }

            _bookshelfTree.Nodes.Add(rootNode);
            rootNode.Expand();

            _bookshelfTree.EndUpdate();
        }

        private TreeNode CreateBookshelfItemNode(BookshelfItem item)
        {
            return new TreeNode(item.Name)
            {
                Tag = "ITEM:" + item.Path,
                ImageKey = GetBookshelfItemIcon(item.Path),
                SelectedImageKey = GetBookshelfItemIcon(item.Path)
            };
        }

        private string GetBookshelfItemIcon(string path)
        {
            var ext = FileExtensions.GetExt(path);
            if (FileExtensions.IsArchive(ext))
                return _bookshelfTree.ImageList?.Images.ContainsKey("archive") == true ? "archive" : "folder";
            return "folder";
        }

        private void BookshelfTree_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            var tag = e.Node?.Tag?.ToString();
            if (tag == null) return;

            if (tag.StartsWith("ITEM:"))
            {
                var path = tag.Substring(5);
                if (File.Exists(path) || Directory.Exists(path))
                    NavigateTo(path);
            }
        }

        // ── 本棚に追加（サブメニュー付きダイアログ） ──
        public void ShowAddToBookshelfMenu(string filePath, string fileName, Control anchor, Point location)
        {
            var menu = new ContextMenuStrip();

            // 既存カテゴリ
            foreach (var cat in _bookshelfService.Categories)
            {
                var catId = cat.Id;
                var catName = cat.Name;
                menu.Items.Add(catName, null, (s, e) =>
                {
                    _bookshelfService.AddItem(catId, fileName, filePath);
                    if (_isBookshelfMode) BuildBookshelfTree();
                });
            }

            if (_bookshelfService.Categories.Count > 0)
                menu.Items.Add(new ToolStripSeparator());

            // 新しいカテゴリを作成
            menu.Items.Add("新しいカテゴリを作成...", null, (s, e) =>
            {
                var name = ShowInputDialog("新しいカテゴリ", "カテゴリ名を入力してください:");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var cat = _bookshelfService.AddCategory(name);
                    _bookshelfService.AddItem(cat.Id, fileName, filePath);
                    if (_isBookshelfMode) BuildBookshelfTree();
                }
            });

            menu.Show(anchor, location);
        }

        private string? ShowInputDialog(string title, string prompt)
        {
            var form = new Form
            {
                Text = title, Width = 350, Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false
            };
            var label = new Label { Text = prompt, Left = 10, Top = 12, Width = 310 };
            var textBox = new TextBox { Left = 10, Top = 36, Width = 310 };
            var btnOk = new Button { Text = "OK", Left = 160, Top = 70, Width = 75, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "キャンセル", Left = 245, Top = 70, Width = 75, DialogResult = DialogResult.Cancel };
            form.AcceptButton = btnOk;
            form.CancelButton = btnCancel;
            form.Controls.AddRange(new Control[] { label, textBox, btnOk, btnCancel });

            return form.ShowDialog(this) == DialogResult.OK ? textBox.Text : null;
        }

        // ── ツールバーアクション ──
        private void BookshelfNewCategory()
        {
            var name = ShowInputDialog("新しいカテゴリ", "カテゴリ名を入力してください:");
            if (!string.IsNullOrWhiteSpace(name))
            {
                _bookshelfService.AddCategory(name);
                BuildBookshelfTree();
            }
        }

        private void BookshelfAddCurrentFile()
        {
            // 現在表示中のファイルを本棚に追加
            if (_currentFileIndex >= 0 && _currentFileIndex < _viewableFiles.Count)
            {
                var file = _viewableFiles[_currentFileIndex];
                // 書庫パスの場合は書庫ファイル自体を登録
                var path = file.FullPath;
                if (path.Contains('!'))
                    path = path.Substring(0, path.IndexOf('!'));
                var name = System.IO.Path.GetFileName(path);
                ShowAddToBookshelfMenu(path, name, _bookshelfTree, _bookshelfTree.PointToClient(Cursor.Position));
            }
            else if (!string.IsNullOrEmpty(_nav.CurrentPath))
            {
                var path = _nav.CurrentPath;
                var name = System.IO.Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;
                ShowAddToBookshelfMenu(path, name, _bookshelfTree, _bookshelfTree.PointToClient(Cursor.Position));
            }
        }

        // ── 本棚ツリーのアクション ──
        private void BookshelfTree_AfterLabelEdit(object? sender, NodeLabelEditEventArgs e)
        {
            if (e.Label == null || e.CancelEdit) return;
            var newName = e.Label.Trim();
            if (string.IsNullOrEmpty(newName)) { e.CancelEdit = true; return; }

            var node = e.Node;
            var tag = node?.Tag?.ToString();
            if (tag == null) { e.CancelEdit = true; return; }

            if (tag.StartsWith("CAT:"))
            {
                // カテゴリ名の変更
                var catId = tag.Substring(4);
                _bookshelfService.RenameCategory(catId, newName);
            }
            else if (tag.StartsWith("ITEM:"))
            {
                // ファイル/フォルダの実名変更
                var path = tag.Substring(5);
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (dir == null) { e.CancelEdit = true; return; }
                    var newPath = Path.Combine(dir, newName);
                    if (newPath == path) { e.CancelEdit = true; return; }

                    if (Directory.Exists(path))
                        Directory.Move(path, newPath);
                    else if (File.Exists(path))
                        File.Move(path, newPath);
                    else { e.CancelEdit = true; return; }

                    // 本棚のパスを更新
                    string? catId = null;
                    if (node?.Parent?.Tag?.ToString()?.StartsWith("CAT:") == true)
                        catId = node.Parent.Tag.ToString()!.Substring(4);
                    _bookshelfService.RemoveItem(path);
                    _bookshelfService.AddItem(catId, newName, newPath);

                    // ノードのTagを更新
                    node!.Tag = "ITEM:" + newPath;
                }
                catch (Exception ex)
                {
                    e.CancelEdit = true;
                    MessageBox.Show($"名前の変更に失敗しました: {ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                e.CancelEdit = true;
            }
        }

        private string? GetBookshelfSelectedPath()
        {
            var tag = _bookshelfTree.SelectedNode?.Tag?.ToString();
            if (tag?.StartsWith("ITEM:") == true) return tag.Substring(5);
            return null;
        }

        private void OpenBookshelfWithAssociation()
        {
            var path = GetBookshelfSelectedPath();
            if (path != null && SplitArchivePath(path) == null && System.IO.File.Exists(path))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        private void ShowBookshelfInExplorer()
        {
            var path = GetBookshelfSelectedPath();
            if (path == null) return;
            if (System.IO.File.Exists(path))
                try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
            else if (System.IO.Directory.Exists(path))
                try { System.Diagnostics.Process.Start("explorer.exe", $"\"{path}\""); } catch { }
        }

        private void RenameBookshelfFile()
        {
            var node = _bookshelfTree.SelectedNode;
            if (node == null) return;
            var tag = node.Tag?.ToString();
            if (tag == null || tag == "BOOKSHELF_ROOT") return;
            node.BeginEdit();
        }

        private void OpenBookshelfSelected()
        {
            var tag = _bookshelfTree.SelectedNode?.Tag?.ToString();
            if (tag?.StartsWith("ITEM:") == true)
            {
                var path = tag.Substring(5);
                if (File.Exists(path) || Directory.Exists(path))
                    NavigateTo(path);
            }
        }

        private void RenameBookshelfSelected()
        {
            // カテゴリもインライン編集
            RenameBookshelfFile();
            return;

            // 以下は未使用（互換性のため残す）
            var tag = _bookshelfTree.SelectedNode?.Tag?.ToString();
            if (tag?.StartsWith("CAT:") == true)
            {
                var catId = tag.Substring(4);
                var cat = _bookshelfService.Categories.FirstOrDefault(c => c.Id == catId);
                if (cat == null) return;

                var newName = ShowInputDialog("カテゴリ名の変更", "新しい名前:");
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    _bookshelfService.RenameCategory(catId, newName);
                    BuildBookshelfTree();
                }
            }
        }

        private void DeleteBookshelfSelected()
        {
            var tag = _bookshelfTree.SelectedNode?.Tag?.ToString();
            if (tag == null) return;

            if (tag.StartsWith("CAT:"))
            {
                var catId = tag.Substring(4);
                if (MessageBox.Show("このカテゴリを本棚から解除しますか？\n（中のアイテムも解除されます。ファイル自体は削除されません）",
                    "確認", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _bookshelfService.RemoveCategory(catId);
                    BuildBookshelfTree();
                }
            }
            else if (tag.StartsWith("ITEM:"))
            {
                var path = tag.Substring(5);
                _bookshelfService.RemoveItem(path);
                BuildBookshelfTree();
            }
        }
    }
}
