using System.Drawing;
using System.Windows.Forms;

namespace leeyez_kai.Controls
{
    public class HelpDialog : Form
    {
        public HelpDialog()
        {
            Text = "ヘルプ - Leeyez Kai の使い方";
            Size = new Size(520, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            Font = new Font("Yu Gothic UI", 9.5f);

            var rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Padding = new Padding(16)
            };

            rtb.Rtf = BuildHelpRtf();

            var btnClose = new Button { Text = "閉じる", Width = 90, Dock = DockStyle.Bottom, DialogResult = DialogResult.OK };
            AcceptButton = btnClose;

            Controls.Add(rtb);
            Controls.Add(btnClose);
        }

        private static string BuildHelpRtf()
        {
            // シンプルなRTFで使い方を記述
            return @"{\rtf1\ansi\deff0{\fonttbl{\f0 Yu Gothic UI;}}
\f0\fs20

{\b\fs28 Leeyez Kai}\line
画像・動画・書庫ビューア\line\line

{\b\fs22 基本操作}\line
\bullet  左のツリーからフォルダや書庫ファイルを選択\line
\bullet  ファイルリストから画像をクリックで表示\line
\bullet  書庫ファイルをクリックすると中身の画像を表示\line
\bullet  ホイールや矢印キーで画像を切り替え\line
\bullet  ダブルクリックで全画面表示\line\line

{\b\fs22 表示モード}\line
\bullet  {\b A} (自動) - 縦長画像は自動で見開き表示\line
\bullet  {\b 1} (単頁) - 常に1ページずつ表示\line
\bullet  {\b 2} (見開き) - 常に2ページ並べて表示\line
\bullet  綴じ方向ボタンで左綴じ/右綴じを切替\line\line

{\b\fs22 お気に入り}\line
\bullet  ファイルやフォルダを右クリック \u8594  「お気に入りに追加」\line
\bullet  ツリーのお気に入りからワンクリックでアクセス\line
\bullet  書庫ファイルも登録可能\line\line

{\b\fs22 本棚}\line
\bullet  ナビバーの本棚アイコンでツリーを本棚モードに切替\line
\bullet  右クリック \u8594  「本棚に追加」でカテゴリ管理\line
\bullet  本棚ツールバーから新規カテゴリ作成・編集・削除\line\line

{\b\fs22 動画・音声再生}\line
\bullet  画面クリックで再生/一時停止\line
\bullet  シークバーで再生位置を変更\line
\bullet  速度ボタンで再生速度を変更\line
\bullet  ループボタンで繰り返し再生\line\line

{\b\fs22 設定}\line
\bullet  ナビバーの歯車アイコンから設定画面を開く\line
\bullet  ショートカットキーのカスタマイズが可能\line
\bullet  見開き判定の閾値やフォントサイズなどを調整\line

}";
        }
    }
}
