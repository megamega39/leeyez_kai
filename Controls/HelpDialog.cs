using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using leeyez_kai.i18n;

namespace leeyez_kai.Controls
{
    public class HelpDialog : Form
    {
        private static readonly HttpClient _httpClient = new();
        private const string GitHubRepo = "megamega39/leeyez_kai";
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

            tabs.TabPages.Add(BuildAboutTab());

            var btnClose = new Button { Text = Localization.Get("help.close"), Width = 90, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
            AcceptButton = btnClose;

            Controls.Add(tabs);
            Controls.Add(btnClose);
        }

        private TabPage BuildAboutTab()
        {
            var tab = new TabPage(Localization.Get("help.tab.about"));
            var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(24, 16, 24, 16) };

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";

            var lblName = new Label
            {
                Text = "Leeyez Kai",
                Font = new Font("Yu Gothic UI", 18f, FontStyle.Bold),
                AutoSize = true, Location = new Point(24, 16)
            };

            var lblVersion = new Label
            {
                Text = $"v{versionStr}",
                Font = new Font("Yu Gothic UI", 11f),
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = true, Location = new Point(24, 52)
            };

            var lblDesc = new Label
            {
                Text = Localization.Get("about.description"),
                Font = new Font("Yu Gothic UI", 9.5f),
                AutoSize = true, Location = new Point(24, 82)
            };

            var lblAuthor = new Label
            {
                Text = Localization.Get("about.author") + ": shimao",
                Font = new Font("Yu Gothic UI", 9.5f),
                AutoSize = true, Location = new Point(24, 116)
            };

            var lblLicense = new Label
            {
                Text = Localization.Get("about.license") + ": MIT",
                Font = new Font("Yu Gothic UI", 9.5f),
                AutoSize = true, Location = new Point(24, 140)
            };

            var lblRuntime = new Label
            {
                Text = $".NET {Environment.Version.Major}.{Environment.Version.Minor}",
                Font = new Font("Yu Gothic UI", 9.5f),
                ForeColor = Color.FromArgb(100, 100, 100),
                AutoSize = true, Location = new Point(24, 164)
            };

            var lnkGitHub = new LinkLabel
            {
                Text = "GitHub: megamega39/leeyez_kai",
                Font = new Font("Yu Gothic UI", 9.5f),
                AutoSize = true, Location = new Point(24, 196)
            };
            lnkGitHub.LinkClicked += (s, e) =>
            {
                try { Process.Start(new ProcessStartInfo($"https://github.com/{GitHubRepo}") { UseShellExecute = true }); } catch { }
            };

            // 更新チェック
            var btnCheck = new Button
            {
                Text = Localization.Get("about.checkupdate"),
                AutoSize = true, Location = new Point(24, 228)
            };
            var lblUpdateResult = new Label
            {
                Font = new Font("Yu Gothic UI", 9f),
                AutoSize = true, Location = new Point(btnCheck.Right + 8, 232),
                Visible = false
            };
            var lnkUpdate = new LinkLabel
            {
                Font = new Font("Yu Gothic UI", 9f),
                AutoSize = true, Location = new Point(btnCheck.Right + 8, 232),
                Visible = false
            };
            lnkUpdate.LinkClicked += (s, e) =>
            {
                var url = lnkUpdate.Tag as string;
                if (url != null)
                    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            };
            btnCheck.Click += async (s, e) =>
            {
                btnCheck.Enabled = false;
                lblUpdateResult.Visible = false;
                lnkUpdate.Visible = false;
                btnCheck.Text = Localization.Get("about.checking");
                try
                {
                    var (latestTag, releaseUrl) = await CheckLatestRelease();
                    var latestVersion = ParseVersion(latestTag);
                    if (latestVersion != null && version != null && latestVersion > version)
                    {
                        lnkUpdate.Text = string.Format(Localization.Get("about.newversion"), latestTag);
                        lnkUpdate.Tag = releaseUrl;
                        lnkUpdate.Location = new Point(btnCheck.Right + 8, 232);
                        lnkUpdate.Visible = true;
                    }
                    else
                    {
                        lblUpdateResult.Text = Localization.Get("about.uptodate");
                        lblUpdateResult.ForeColor = Color.FromArgb(0, 128, 0);
                        lblUpdateResult.Location = new Point(btnCheck.Right + 8, 232);
                        lblUpdateResult.Visible = true;
                    }
                }
                catch
                {
                    lblUpdateResult.Text = Localization.Get("about.checkfailed");
                    lblUpdateResult.ForeColor = Color.FromArgb(200, 0, 0);
                    lblUpdateResult.Location = new Point(btnCheck.Right + 8, 232);
                    lblUpdateResult.Visible = true;
                }
                btnCheck.Text = Localization.Get("about.checkupdate");
                btnCheck.Enabled = true;
            };

            var lblLibHeader = new Label
            {
                Text = Localization.Get("about.libraries"),
                Font = new Font("Yu Gothic UI", 9.5f, FontStyle.Bold),
                AutoSize = true, Location = new Point(24, 268)
            };

            var libs = new[] { "SkiaSharp", "NAudio", "SharpCompress", "SevenZipExtractor", "7z.Libs" };
            var lblLibs = new Label
            {
                Text = string.Join("\n", libs.Select(l => $"  - {l}")),
                Font = new Font("Yu Gothic UI", 9f),
                ForeColor = Color.FromArgb(80, 80, 80),
                AutoSize = true, Location = new Point(24, 292)
            };

            panel.Controls.AddRange(new Control[] { lblName, lblVersion, lblDesc, lblAuthor, lblLicense, lblRuntime, lnkGitHub, btnCheck, lblUpdateResult, lnkUpdate, lblLibHeader, lblLibs });
            tab.Controls.Add(panel);
            return tab;
        }

        private static async Task<(string tag, string url)> CheckLatestRelease()
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "LeeyezKai-UpdateChecker");

            var json = await _httpClient.GetStringAsync($"https://api.github.com/repos/{GitHubRepo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            var url = doc.RootElement.GetProperty("html_url").GetString() ?? $"https://github.com/{GitHubRepo}/releases";
            return (tag, url);
        }

        private static Version? ParseVersion(string tag)
        {
            var s = tag.TrimStart('v', 'V');
            return Version.TryParse(s, out var v) ? v : null;
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
