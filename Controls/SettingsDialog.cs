using System;
using System.Drawing;
using System.Windows.Forms;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai.Controls
{
    public class SettingsDialog : Form
    {
        private readonly AppSettings _settings;
        private readonly ShortcutManager _shortcuts;
        private CheckBox _chkWrapNav = null!;
        private CheckBox _chkAutoSpreadCover = null!;
        private CheckBox _chkRecursiveMedia = null!;
        private CheckBox _chkAutoPlay = null!;
        private NumericUpDown _numThreshold = null!;
        private NumericUpDown _numMemoryLimit = null!;
        private NumericUpDown _numThumbSize = null!;
        private NumericUpDown _numPreviewSize = null!;
        private NumericUpDown _numFontSize = null!;
        private ListView _shortcutList = null!;

        public SettingsDialog(AppSettings settings, ShortcutManager shortcuts)
        {
            _settings = settings;
            _shortcuts = shortcuts;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "設定";
            Size = new Size(500, 480);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Yu Gothic UI", 9f);

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // ── 一般タブ ──
            var generalTab = new TabPage("一般");
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(16), AutoScroll = true
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

            int row = 0;
            AddSectionLabel(panel, "ナビゲーション", ref row);
            _chkWrapNav = AddCheckBox(panel, "端でループする（最後から最初に戻る）", _settings.WrapNavigation, ref row);
            _chkAutoSpreadCover = AddCheckBox(panel, "見開き時に最初のページを単独表示する", _settings.AutoSpreadCover, ref row);
            AddSectionLabel(panel, "ファイル読み込み", ref row);
            _chkRecursiveMedia = AddCheckBox(panel, "サブフォルダも含めて画像を表示（再帰表示）", _settings.RecursiveMedia, ref row);
            AddSectionLabel(panel, "メディア", ref row);
            _chkAutoPlay = AddCheckBox(panel, "動画・音声を自動再生", _settings.AutoPlay, ref row);
            AddSectionLabel(panel, "パフォーマンス", ref row);
            _numMemoryLimit = AddNumeric(panel, "キャッシュメモリ上限 (MB)", 128, 2048, 128, _settings.MemoryLimitMB, 0, ref row);
            AddSectionLabel(panel, "表示", ref row);
            _numThreshold = AddNumeric(panel, "見開き判定の閾値", 0.5m, 2.0m, 0.05m, (decimal)_settings.SpreadThreshold, 2, ref row);
            _numThumbSize = AddNumeric(panel, "サムネイルサイズ (px)", 80, 500, 16, _settings.ThumbnailSize, 0, ref row);
            _numPreviewSize = AddNumeric(panel, "プレビューサイズ (px)", 160, 500, 16, _settings.HoverPreviewSize, 0, ref row);
            _numFontSize = AddNumeric(panel, "フォントサイズ", 8, 18, 1, _settings.SidebarFontSize, 0, ref row);

            generalTab.Controls.Add(panel);
            tabs.TabPages.Add(generalTab);

            // ── ショートカットタブ ──
            var shortcutTab = new TabPage("ショートカット");

            _shortcutList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _shortcutList.Columns.Add("操作", 200);
            _shortcutList.Columns.Add("ショートカット", 200);

            foreach (var (id, label, _) in ShortcutManager.AllActions)
            {
                var key = _shortcuts.GetKey(id);
                var lvi = new ListViewItem(new[] { label, ShortcutManager.KeyToString(key) });
                lvi.Tag = id;
                _shortcutList.Items.Add(lvi);
            }

            _shortcutList.DoubleClick += ShortcutList_DoubleClick;

            var shortcutBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
            var btnReset = new Button { Text = "初期設定に戻す", Width = 120 };
            btnReset.Click += (s, e) =>
            {
                _shortcuts.ResetToDefault();
                RefreshShortcutList();
            };
            shortcutBtnPanel.Controls.Add(btnReset);

            var shortcutHint = new Label
            {
                Text = "※ ダブルクリックでショートカットキーを変更できます",
                Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0),
                ForeColor = Color.Gray, Font = new Font("Yu Gothic UI", 8.5f)
            };
            shortcutTab.Controls.Add(_shortcutList);
            shortcutTab.Controls.Add(shortcutHint);
            shortcutTab.Controls.Add(shortcutBtnPanel);
            tabs.TabPages.Add(shortcutTab);

            // ── ボタン ──
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var btnCancel = new Button { Text = "キャンセル", Width = 90, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = "OK", Width = 90, DialogResult = DialogResult.OK };
            btnOk.Click += (s, e) => ApplySettings();
            btnPanel.Controls.Add(btnCancel);
            btnPanel.Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(tabs);
            Controls.Add(btnPanel);
        }

        private void ShortcutList_DoubleClick(object? sender, EventArgs e)
        {
            if (_shortcutList.SelectedItems.Count != 1) return;
            var lvi = _shortcutList.SelectedItems[0];
            var actionId = lvi.Tag as string;
            if (actionId == null) return;

            // キー入力ダイアログ
            using var dlg = new KeyCaptureDialog(lvi.SubItems[0].Text);
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.CapturedKey != Keys.None)
            {
                _shortcuts.SetKey(actionId, dlg.CapturedKey);
                lvi.SubItems[1].Text = ShortcutManager.KeyToString(dlg.CapturedKey);
            }
        }

        private void RefreshShortcutList()
        {
            foreach (ListViewItem lvi in _shortcutList.Items)
            {
                var id = lvi.Tag as string;
                if (id != null)
                    lvi.SubItems[1].Text = ShortcutManager.KeyToString(_shortcuts.GetKey(id));
            }
        }

        private void ApplySettings()
        {
            _settings.WrapNavigation = _chkWrapNav.Checked;
            _settings.AutoSpreadCover = _chkAutoSpreadCover.Checked;
            _settings.RecursiveMedia = _chkRecursiveMedia.Checked;
            _settings.AutoPlay = _chkAutoPlay.Checked;
            _settings.MemoryLimitMB = (int)_numMemoryLimit.Value;
            _settings.SpreadThreshold = (float)_numThreshold.Value;
            _settings.ThumbnailSize = (int)_numThumbSize.Value;
            _settings.HoverPreviewSize = (int)_numPreviewSize.Value;
            _settings.SidebarFontSize = (int)_numFontSize.Value;
            _settings.Save();
            _shortcuts.Save();
        }

        // ── ヘルパー ──
        private static void AddSectionLabel(TableLayoutPanel panel, string text, ref int row)
        {
            var lbl = new Label { Text = text, Font = new Font("Yu Gothic UI", 10f, FontStyle.Bold), AutoSize = true, Padding = new Padding(0, 8, 0, 4) };
            panel.Controls.Add(lbl, 0, row);
            panel.SetColumnSpan(lbl, 2);
            row++;
        }

        private static CheckBox AddCheckBox(TableLayoutPanel panel, string text, bool value, ref int row)
        {
            var chk = new CheckBox { Text = text, Checked = value, AutoSize = true };
            panel.Controls.Add(chk, 0, row);
            panel.SetColumnSpan(chk, 2);
            row++;
            return chk;
        }

        private static NumericUpDown AddNumeric(TableLayoutPanel panel, string label, decimal min, decimal max, decimal inc, decimal value, int decimals, ref int row)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            var num = new NumericUpDown { Minimum = min, Maximum = max, Increment = inc, Value = value, DecimalPlaces = decimals, Width = 80 };
            panel.Controls.Add(num, 1, row);
            row++;
            return num;
        }
    }

    /// <summary>キー入力キャプチャダイアログ</summary>
    internal class KeyCaptureDialog : Form
    {
        public Keys CapturedKey { get; private set; }

        public KeyCaptureDialog(string actionName)
        {
            Text = "ショートカット変更";
            Size = new Size(350, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = new Font("Yu Gothic UI", 10f);

            var lbl = new Label
            {
                Text = $"「{actionName}」のキーを押してください...",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(lbl);

            var btnClear = new Button { Text = "クリア", Width = 80, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
            btnClear.Click += (s, e) => { CapturedKey = Keys.None; };
            Controls.Add(btnClear);

            KeyDown += (s, e) =>
            {
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return; }
                // 修飾キーだけの入力は無視
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey) return;
                CapturedKey = e.KeyData;
                lbl.Text = ShortcutManager.KeyToString(CapturedKey);
                DialogResult = DialogResult.OK;
                Close();
            };
        }
    }
}
