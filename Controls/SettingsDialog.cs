using System;
using System.Drawing;
using System.Windows.Forms;
using leeyez_kai.i18n;
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
        private ComboBox _cmbLanguage = null!;
        private ListView _shortcutList = null!;

        public SettingsDialog(AppSettings settings, ShortcutManager shortcuts)
        {
            _settings = settings;
            _shortcuts = shortcuts;
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = Localization.Get("settings.title");
            Size = new Size(500, 660);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Yu Gothic UI", 9f);

            var tabs = new TabControl { Dock = DockStyle.Fill };

            // ── 一般タブ ──
            var generalTab = new TabPage(Localization.Get("settings.general"));
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 20, Padding = new Padding(16), AutoScroll = true
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            for (int i = 0; i < 20; i++)
                panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int row = 0;
            // Language（他のNumeric項目と同じレイアウト）
            panel.Controls.Add(new Label { Text = "Language", AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); // 全言語共通で英語表記
            _cmbLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            foreach (var (code, name) in Localization.AvailableLanguages)
                _cmbLanguage.Items.Add(name);
            var langIdx = Array.FindIndex(Localization.AvailableLanguages, l => l.code == _settings.Language);
            _cmbLanguage.SelectedIndex = langIdx >= 0 ? langIdx : 0;
            _cmbLanguage.SelectedIndexChanged += (s, e) =>
            {
                if (_cmbLanguage.SelectedIndex >= 0)
                {
                    var code = Localization.AvailableLanguages[_cmbLanguage.SelectedIndex].code;
                    _settings.Language = code;
                    Localization.SetLanguage(code);
                    SuspendLayout();
                    Controls.Clear();
                    InitializeComponent();
                    ResumeLayout(true);
                }
            };
            panel.Controls.Add(_cmbLanguage, 1, row);
            row++;

            AddSectionLabel(panel, Localization.Get("settings.nav"), ref row);
            _chkWrapNav = AddCheckBox(panel, Localization.Get("settings.wrap"), _settings.WrapNavigation, ref row);
            _chkAutoSpreadCover = AddCheckBox(panel, Localization.Get("settings.spreadcover"), _settings.AutoSpreadCover, ref row);
            AddSectionLabel(panel, Localization.Get("settings.fileload"), ref row);
            _chkRecursiveMedia = AddCheckBox(panel, Localization.Get("settings.recursive"), _settings.RecursiveMedia, ref row);
            AddSectionLabel(panel, Localization.Get("settings.media"), ref row);
            _chkAutoPlay = AddCheckBox(panel, Localization.Get("settings.autoplay"), _settings.AutoPlay, ref row);
            AddSectionLabel(panel, Localization.Get("settings.perf"), ref row);
            _numMemoryLimit = AddNumeric(panel, Localization.Get("settings.memlimit"), 128, 2048, 128, _settings.MemoryLimitMB, 0, ref row);
            AddSectionLabel(panel, Localization.Get("settings.display"), ref row);
            _numThreshold = AddNumeric(panel, Localization.Get("settings.threshold"), 0.5m, 2.0m, 0.05m, (decimal)_settings.SpreadThreshold, 2, ref row);
            _numThumbSize = AddNumeric(panel, Localization.Get("settings.thumbsize"), 80, 500, 16, _settings.ThumbnailSize, 0, ref row);
            _numPreviewSize = AddNumeric(panel, Localization.Get("settings.previewsize"), 160, 500, 16, _settings.HoverPreviewSize, 0, ref row);
            _numFontSize = AddNumeric(panel, Localization.Get("settings.fontsize"), 8, 18, 1, _settings.SidebarFontSize, 0, ref row);

            generalTab.Controls.Add(panel);
            tabs.TabPages.Add(generalTab);

            // ── ショートカットタブ ──
            var shortcutTab = new TabPage(Localization.Get("settings.shortcuts"));

            _shortcutList = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _shortcutList.Columns.Add(Localization.Get("settings.action"), 200);
            _shortcutList.Columns.Add(Localization.Get("settings.shortcut"), 200);

            foreach (var (id, labelKey, _) in ShortcutManager.AllActions)
            {
                var key = _shortcuts.GetKey(id);
                var lvi = new ListViewItem(new[] { Localization.Get(labelKey), ShortcutManager.KeyToString(key) });
                lvi.Tag = id;
                _shortcutList.Items.Add(lvi);
            }

            _shortcutList.DoubleClick += ShortcutList_DoubleClick;

            var shortcutBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(4) };
            var btnReset = new Button { Text = Localization.Get("settings.reset"), Width = 120 };
            btnReset.Click += (s, e) =>
            {
                _shortcuts.ResetToDefault();
                RefreshShortcutList();
            };
            shortcutBtnPanel.Controls.Add(btnReset);

            var shortcutHint = new Label
            {
                Text = Localization.Get("settings.shorthint"),
                Dock = DockStyle.Top, Height = 24, Padding = new Padding(8, 4, 0, 0),
                ForeColor = Color.Gray, Font = new Font("Yu Gothic UI", 8.5f)
            };
            shortcutTab.Controls.Add(_shortcutList);
            shortcutTab.Controls.Add(shortcutHint);
            shortcutTab.Controls.Add(shortcutBtnPanel);
            tabs.TabPages.Add(shortcutTab);

            // ── ボタン ──
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var btnCancel = new Button { Text = Localization.Get("settings.cancel"), Width = 90, DialogResult = DialogResult.Cancel };
            var btnOk = new Button { Text = Localization.Get("settings.ok"), Width = 90, DialogResult = DialogResult.OK };
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
            if (_cmbLanguage.SelectedIndex >= 0)
            {
                _settings.Language = Localization.AvailableLanguages[_cmbLanguage.SelectedIndex].code;
                Localization.SetLanguage(_settings.Language);
            }
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
            Text = Localization.Get("dlg.shortcutchange");
            Size = new Size(350, 150);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            Font = new Font("Yu Gothic UI", 10f);

            var lbl = new Label
            {
                Text = string.Format(Localization.Get("dlg.capturekey"), actionName),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(lbl);

            var btnClear = new Button { Text = Localization.Get("dlg.clear"), Width = 80, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
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

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var keyCode = keyData & Keys.KeyCode;
            if (keyCode == Keys.Up || keyCode == Keys.Down || keyCode == Keys.Left || keyCode == Keys.Right
                || keyCode == Keys.Tab)
            {
                // 矢印キー・Tabを通常キー入力としてKeyDownに渡す
                OnKeyDown(new KeyEventArgs(keyData));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
