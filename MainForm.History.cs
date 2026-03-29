using System;
using System.Diagnostics;
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
        private readonly HistoryService _historyService = new();
        private ListView _historyList = null!;
        private Panel _historyToolbar = null!;
        private bool _isHistoryMode;
        private bool _historyListUpdating;

        private void SetupHistory()
        {
            _historyList.View = View.Details;
            _historyList.FullRowSelect = true;
            _historyList.HeaderStyle = ColumnHeaderStyle.None;
            _historyList.MultiSelect = false;
            _historyList.HideSelection = false;
            _historyList.ShowGroups = true;
            _historyList.Columns.Add("Name", -1);
            _historyList.OwnerDraw = true;
            _historyList.SmallImageList = _folderTree.ImageList;

            _historyList.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            _historyList.DrawItem += HistoryList_DrawItem;
            _historyList.DrawSubItem += (s, e) => { }; // DrawItemで処理

            _historyList.SelectedIndexChanged += HistoryList_SelectedIndexChanged;
            _historyList.Resize += (s, e) =>
            {
                if (_historyList.Columns.Count > 0)
                    _historyList.Columns[0].Width = _historyList.ClientSize.Width;
            };

            // 右クリックメニュー
            var ctx = new ContextMenuStrip();
            ctx.Items.Add("関連付けで開く", null, (s, e) => OpenHistoryWithAssociation());
            ctx.Items.Add("エクスプローラで開く", null, (s, e) => ShowHistoryInExplorer());
            ctx.Items.Add("パスをコピー", null, (s, e) => CopyHistoryPath());
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("お気に入りに追加", null, (s, e) => AddHistoryToFavorites());
            ctx.Items.Add("本棚に追加", null, (s, e) => AddHistoryToBookshelf());
            ctx.Items.Add(new ToolStripSeparator());
            ctx.Items.Add("この履歴を削除", null, (s, e) => DeleteHistoryEntry());

            _historyList.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var hit = _historyList.HitTest(e.Location);
                    if (hit.Item != null)
                    {
                        hit.Item.Selected = true;
                        hit.Item.Focused = true;
                    }
                }
            };
            _historyList.ContextMenuStrip = ctx;
        }

        private void ToggleHistory()
        {
            _isHistoryMode = !_isHistoryMode;
            if (_isHistoryMode) _isBookshelfMode = false;

            var panel = _sidebarSplit.Panel1;
            panel.SuspendLayout();

            if (_isHistoryMode)
            {
                BuildHistoryList();
                _folderTree.Visible = false;
                _bookshelfTree.Visible = false;
                _bookshelfToolbar.Visible = false;
                _historyToolbar.Visible = true;
                _historyList.Visible = true;

                panel.Controls.SetChildIndex(_historyList, 0);
                panel.Controls.SetChildIndex(_historyToolbar, 1);

                _sidebarLabel.Parent.Visible = false;
                _btnHistory.BackColor = Color.FromArgb(0xD0, 0xD0, 0xD0);
                _btnBookshelf.BackColor = Color.Transparent;
            }
            else
            {
                _historyList.Visible = false;
                _historyToolbar.Visible = false;
                _folderTree.Visible = true;

                panel.Controls.SetChildIndex(_folderTree, 0);

                _sidebarLabel.Parent.Visible = true;
                _sidebarLabel.Text = "フォルダ";
                _btnHistory.BackColor = Color.Transparent;
            }

            panel.ResumeLayout(true);
        }

        private void BuildHistoryList()
        {
            _historyListUpdating = true;
            _historyList.BeginUpdate();
            _historyList.Items.Clear();
            _historyList.Groups.Clear();

            var groupToday = new ListViewGroup("today", "今日");
            var groupYesterday = new ListViewGroup("yesterday", "昨日");
            var groupThisWeek = new ListViewGroup("thisWeek", "今週");
            var groupLastWeek = new ListViewGroup("lastWeek", "先週");
            var groupOlder = new ListViewGroup("older", "それ以前");
            _historyList.Groups.AddRange(new[] { groupToday, groupYesterday, groupThisWeek, groupLastWeek, groupOlder });

            var entries = _historyService.Entries;

            foreach (var entry in entries)
            {
                var groupKey = GetDateGroup(entry.LastAccessedTicks);
                var group = groupKey switch
                {
                    "今日" => groupToday,
                    "昨日" => groupYesterday,
                    "今週" => groupThisWeek,
                    "先週" => groupLastWeek,
                    _ => groupOlder
                };

                var item = new ListViewItem(entry.Name) { Tag = entry, Group = group };
                _historyList.Items.Add(item);
            }

            if (_historyList.Columns.Count > 0)
                _historyList.Columns[0].Width = _historyList.ClientSize.Width;

            _historyList.EndUpdate();
            _historyListUpdating = false;
        }

        private static string GetDateGroup(long ticks)
        {
            var now = DateTime.Now;
            var date = new DateTime(ticks, DateTimeKind.Utc).ToLocalTime();
            var diffDays = (now.Date - date.Date).Days;

            return diffDays switch
            {
                0 => "今日",
                1 => "昨日",
                < 7 => "今週",
                < 14 => "先週",
                _ => "それ以前"
            };
        }

        private static readonly Font _historyItemFont = new("Yu Gothic UI", 9f);
        private static readonly Font _historyTimeFont = new("Yu Gothic UI", 7.5f);
        private static readonly StringFormat _nameFmt = new() { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        private static readonly StringFormat _timeFmt = new() { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };

        private void HistoryList_DrawItem(object? sender, DrawListViewItemEventArgs e)
        {
            if (e.Item?.Tag is not HistoryEntry entry) { e.DrawDefault = true; return; }

            var isSelected = e.Item.Selected;
            var bg = isSelected ? Color.FromArgb(0x00, 0x78, 0xD4) : Color.White;
            var fgMain = isSelected ? Color.White : Color.FromArgb(30, 30, 30);
            var fgSub = isSelected ? Color.FromArgb(200, 200, 200) : Color.FromArgb(130, 130, 130);

            using var bgBrush = new SolidBrush(bg);
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            // アイコン（Windowsシステムアイコン）
            var imageList = _historyList.SmallImageList;
            if (imageList != null)
            {
                var iconKey = entry.EntryType == "archive" ? "archive" : "folder";
                if (imageList.Images.ContainsKey(iconKey))
                {
                    var img = imageList.Images[iconKey];
                    e.Graphics.DrawImage(img, e.Bounds.X + 4, e.Bounds.Y + (e.Bounds.Height - 16) / 2, 16, 16);
                }
            }

            // 名前
            var nameRect = new RectangleF(e.Bounds.X + 24, e.Bounds.Y + (e.Bounds.Height - 16) / 2f, e.Bounds.Width - 96, 18);
            using var nameBrush = new SolidBrush(fgMain);
            e.Graphics.DrawString(entry.Name, _historyItemFont, nameBrush, nameRect, _nameFmt);

            // 時刻
            var time = new DateTime(entry.LastAccessedTicks, DateTimeKind.Utc).ToLocalTime();
            var timeStr = time.Date == DateTime.Today ? time.ToString("HH:mm") : time.ToString("MM/dd HH:mm");
            var timeRect = new RectangleF(e.Bounds.Right - 70, e.Bounds.Y + (e.Bounds.Height - 14) / 2f, 66, 16);
            using var timeBrush = new SolidBrush(fgSub);
            e.Graphics.DrawString(timeStr, _historyTimeFont, timeBrush, timeRect, _timeFmt);
        }

        /// <summary>NavigateTo時に永続履歴へ記録（書庫・動画・音声のみ）</summary>
        private void RecordHistory(string path)
        {
            var ext = FileExtensions.GetExt(path);
            string entryType;

            if (path.Contains('!') || FileExtensions.IsArchive(ext))
                entryType = "archive";
            else if (FileExtensions.IsVideo(ext) || FileExtensions.IsAudio(ext))
                entryType = "media";
            else
                return; // 画像・フォルダは記録しない

            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            _historyService.AddEntry(path, name, entryType);
        }

        // ── アクション ──

        private HistoryEntry? GetSelectedHistoryEntry()
        {
            return _historyList.SelectedItems.Count > 0
                ? _historyList.SelectedItems[0].Tag as HistoryEntry
                : null;
        }

        private void HistoryList_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_historyListUpdating) return;
            var entry = GetSelectedHistoryEntry();
            if (entry != null) NavigateTo(entry.Path);
        }

        private void OpenHistoryWithAssociation()
        {
            var entry = GetSelectedHistoryEntry();
            if (entry == null) return;
            var path = entry.Path;
            if (path.Contains('!'))
                path = path.Substring(0, path.IndexOf('!'));
            if (File.Exists(path))
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
        }

        private void ShowHistoryInExplorer()
        {
            var entry = GetSelectedHistoryEntry();
            if (entry == null) return;
            var path = entry.Path;
            // 書庫パスの場合は書庫ファイル自体を表示
            if (path.Contains('!'))
                path = path.Substring(0, path.IndexOf('!'));

            if (File.Exists(path))
                try { Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
            else if (Directory.Exists(path))
                try { Process.Start("explorer.exe", $"\"{path}\""); } catch { }
        }

        private void CopyHistoryPath()
        {
            var entry = GetSelectedHistoryEntry();
            if (entry != null)
                Clipboard.SetText(entry.Path);
        }

        private void AddHistoryToFavorites()
        {
            var entry = GetSelectedHistoryEntry();
            if (entry == null) return;
            var path = entry.Path;
            if (path.Contains('!'))
                path = path.Substring(0, path.IndexOf('!'));
            _treeManager?.AddFavorite(path);
        }

        private void AddHistoryToBookshelf()
        {
            var entry = GetSelectedHistoryEntry();
            if (entry == null) return;
            var path = entry.Path;
            if (path.Contains('!'))
                path = path.Substring(0, path.IndexOf('!'));
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) name = path;
            ShowAddToBookshelfMenu(path, name, _historyList, _historyList.PointToClient(Cursor.Position));
        }

        private void DeleteHistoryEntry()
        {
            var entry = GetSelectedHistoryEntry();
            if (entry == null) return;
            _historyService.RemoveEntry(entry.Path);
            BuildHistoryList();
        }

        private void ClearAllHistory()
        {
            if (MessageBox.Show("履歴をすべて削除しますか？", "確認",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _historyService.ClearAll();
                BuildHistoryList();
            }
        }
    }
}
