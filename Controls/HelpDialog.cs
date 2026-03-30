using System.Drawing;
using System.Windows.Forms;
using leeyez_kai.i18n;

namespace leeyez_kai.Controls
{
    public class HelpDialog : Form
    {
        public HelpDialog()
        {
            Text = Localization.Get("help.title");
            Size = new Size(560, 580);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Yu Gothic UI", 9.5f);

            var tabs = new TabControl { Dock = DockStyle.Fill };

            tabs.TabPages.Add(BuildTab(Localization.Get("help.tab.basic"), new[] {
                ("help.basic.show", "help.basic.show.how"),
                ("help.basic.switch", "help.basic.switch.how"),
                ("help.basic.fullscreen", "help.basic.fullscreen.how"),
                ("help.basic.exitfull", "help.basic.exitfull.how"),
                ("help.basic.zoom", "help.basic.zoom.how"),
                ("help.basic.pan", "help.basic.pan.how"),
                ("help.basic.rotate", "help.basic.rotate.how"),
                ("help.basic.copy", "help.basic.copy.how"),
            }));

            tabs.TabPages.Add(BuildTab(Localization.Get("help.tab.nav"), new[] {
                ("help.nav.back", "help.nav.back.how"),
                ("help.nav.history", "help.nav.history.how"),
                ("help.nav.up", "help.nav.up.how"),
                ("help.nav.sibling", "help.nav.sibling.how"),
                ("help.nav.refresh", "help.nav.refresh.how"),
                ("help.nav.breadcrumb", "help.nav.breadcrumb.how"),
                ("help.nav.address", "help.nav.address.how"),
            }));

            tabs.TabPages.Add(BuildTab(Localization.Get("help.tab.view"), new[] {
                ("help.view.auto", "help.view.auto.how"),
                ("help.view.single", "help.view.single.how"),
                ("help.view.spread", "help.view.spread.how"),
                ("help.view.binding", "help.view.binding.how"),
                ("help.view.fit", "help.view.fit.how"),
                ("help.view.original", "help.view.original.how"),
            }));

            tabs.TabPages.Add(BuildTab(Localization.Get("help.tab.fav"), new[] {
                ("help.fav.add", "help.fav.add.how"),
                ("help.fav.remove", "help.fav.remove.how"),
                ("help.fav.shelf", "help.fav.shelf.how"),
                ("help.fav.addshelf", "help.fav.addshelf.how"),
                ("help.fav.newcat", "help.fav.newcat.how"),
                ("help.fav.rename", "help.fav.rename.how"),
                ("help.fav.removeshelf", "help.fav.removeshelf.how"),
            }));

            tabs.TabPages.Add(BuildTab(Localization.Get("help.tab.media"), new[] {
                ("help.media.play", "help.media.play.how"),
                ("help.media.full", "help.media.full.how"),
                ("help.media.seek", "help.media.seek.how"),
                ("help.media.speed", "help.media.speed.how"),
                ("help.media.loop", "help.media.loop.how"),
                ("help.media.wheel", "help.media.wheel.how"),
                ("help.media.archive", "help.media.archive.how"),
            }));

            tabs.TabPages.Add(BuildTab(Localization.Get("help.tab.file"), new[] {
                ("help.file.rename", "help.file.rename.how"),
                ("help.file.delete", "help.file.delete.how"),
                ("help.file.openwith", "help.file.openwith.how"),
                ("help.file.explorer", "help.file.explorer.how"),
                ("help.file.copypath", "help.file.copypath.how"),
                ("help.file.grid", "help.file.grid.how"),
                ("help.file.hover", "help.file.hover.how"),
            }));

            var btnClose = new Button { Text = Localization.Get("help.close"), Width = 90, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
            AcceptButton = btnClose;

            Controls.Add(tabs);
            Controls.Add(btnClose);
        }

        private static TabPage BuildTab(string title, (string actionKey, string howKey)[] items)
        {
            var tab = new TabPage(title);
            var lv = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                Font = new Font("Yu Gothic UI", 9.5f)
            };
            lv.Columns.Add(Localization.Get("help.col.action"), 200);
            lv.Columns.Add(Localization.Get("help.col.howto"), 320);

            foreach (var (actionKey, howKey) in items)
            {
                lv.Items.Add(new ListViewItem(new[] { Localization.Get(actionKey), Localization.Get(howKey) }));
            }

            tab.Controls.Add(lv);
            return tab;
        }
    }
}
