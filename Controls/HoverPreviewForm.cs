using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using SkiaSharp;
using leeyez_kai.Services;

namespace leeyez_kai.Controls
{
    /// <summary>
    /// ファイルリスト上でホバー時に表示するプレビューウィンドウ
    /// </summary>
    public class HoverPreviewForm : Form
    {
        private SKBitmap? _skImage;
        private readonly PictureBox _pictureBox;
        private static readonly Pen BorderPen = new(Color.FromArgb(100, 100, 100), 1);

        public HoverPreviewForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(30, 30, 30);
            Size = new Size(320, 320);
            TopMost = true;
            Opacity = 0.95;

            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            Controls.Add(_pictureBox);
        }

        protected override bool ShowWithoutActivation => true;

        // フォーカスを奪わない
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT (マウス透過)
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE (アクティブ化しない)
                return cp;
            }
        }

        public void ShowPreview(SKBitmap skBmp, Point screenPos)
        {
            // Dispose old GDI+ image and SKBitmap
            _pictureBox.Image?.Dispose();
            _skImage?.Dispose();
            _skImage = skBmp;

            // Convert SKBitmap → GDI+ Bitmap for PictureBox display
            var gdiBmp = SKBitmapHelper.ToGdiBitmap(skBmp);
            _pictureBox.Image = gdiBmp;

            // 画像アスペクトに合わせてウィンドウサイズ調整
            if (skBmp != null)
            {
                float aspect = (float)skBmp.Width / skBmp.Height;
                int maxSize = 320;
                int w, h;
                if (aspect > 1)
                {
                    w = maxSize;
                    h = Math.Max(64, (int)(maxSize / aspect));
                }
                else
                {
                    h = maxSize;
                    w = Math.Max(64, (int)(maxSize * aspect));
                }
                Size = new Size(w + 4, h + 4); // 2pxボーダー
            }

            // 画面外にはみ出さない
            var screen = Screen.FromPoint(screenPos).WorkingArea;
            int x = screenPos.X + 16;
            int y = screenPos.Y + 16;
            if (x + Width > screen.Right) x = screenPos.X - Width - 8;
            if (y + Height > screen.Bottom) y = screen.Bottom - Height;

            Location = new Point(x, y);
            if (!Visible) Show();
        }

        public void HidePreview()
        {
            Hide();
            _pictureBox.Image?.Dispose();
            _pictureBox.Image = null;
            _skImage?.Dispose();
            _skImage = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawRectangle(BorderPen, 0, 0, Width - 1, Height - 1);
        }
    }
}
