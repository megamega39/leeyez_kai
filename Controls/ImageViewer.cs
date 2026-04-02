using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkiaSharp;
using leeyez_kai.Services;

namespace leeyez_kai.Controls
{
    /// <summary>
    /// 超高速画像ビューア — Win32 StretchDIBits + GDI+直接デコード + バッファ再利用
    /// </summary>
    public class ImageViewer : Panel
    {
        private SKBitmap? _displayBitmap;
        private SKBitmap? _spreadDisplay1;
        private SKBitmap? _spreadDisplay2;
        private int _origW, _origH;
        private readonly object _renderLock = new();

        // アニメーション
        private SKCodec? _animCodec;
        private int _animFrameCount;
        private int _animCurrentFrame;
        private System.Windows.Forms.Timer? _animTimer;
        private SKBitmap?[]? _animSkFrames;

        // ズーム・スクロール
        private float _zoom = 1.0f;
        private PointF _scrollOffset;
        private bool _isPanning;
        private Point _panStart;
        private PointF _panScrollStart;

        // スケールモード
        public enum ScaleMode { FitWindow, FitWidth, FitHeight, Original }
        private ScaleMode _scaleMode = ScaleMode.FitWindow;
        private bool _isSpreadMode;

        // イベント
        public event Action<string>? StatusChanged;
        public event Action<int, int>? ImageSizeChanged;
        public event Action<int>? WheelNavigate;
        public event Action? DoubleClickToggleFullscreen;

        // Win32 API
        [DllImport("gdi32.dll")]
        private static extern int StretchDIBits(IntPtr hdc, int xDest, int yDest, int wDest, int hDest,
            int xSrc, int ySrc, int wSrc, int hSrc, IntPtr lpBits, ref BITMAPINFO lpBmi, uint iUsage, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern int SetStretchBltMode(IntPtr hdc, int mode);
        private const int HALFTONE = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
        }

        private const uint DIB_RGB_COLORS = 0;
        private const uint SRCCOPY = 0x00CC0020;

        public ImageViewer()
        {
            DoubleBuffered = true;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            MouseWheel += OnMouseWheel;
            MouseDown += OnMouseDown;
            MouseUp += OnMouseUp;
            MouseMove += OnMouseMove;
            MouseDoubleClick += OnMouseDoubleClick;
            Resize += (s, e) => Invalidate();
        }

        public float Zoom
        {
            get => _zoom;
            set { _zoom = Math.Clamp(value, 0.05f, 20.0f); Invalidate(); }
        }

        public ScaleMode CurrentScaleMode
        {
            get => _scaleMode;
            set { _scaleMode = value; _zoom = 1.0f; _scrollOffset = PointF.Empty; Invalidate(); }
        }

        public int ImageWidth => _origW;
        public int ImageHeight => _origH;

        /// <summary>
        /// SKBitmapを直接表示（キャッシュヒット時、最速）
        /// </summary>
        public void ShowBitmap(SKBitmap? bmp, int origW, int origH)
        {
            lock (_renderLock)
            {
                StopAnimation();
                _spreadDisplay1 = null;
                _spreadDisplay2 = null;
                _displayBitmap = bmp;
                _origW = origW;
                _origH = origH;
                _isSpreadMode = false;
                _scrollOffset = PointF.Empty;
            }
            if (bmp != null) ImageSizeChanged?.Invoke(origW, origH);
            Invalidate();
        }

        public void ShowSpread(SKBitmap? left, SKBitmap? right)
        {
            lock (_renderLock)
            {
                StopAnimation();
                _displayBitmap = null;
                _spreadDisplay1 = left;
                _spreadDisplay2 = right;
                _isSpreadMode = true;
                _scrollOffset = PointF.Empty;
            }
            Invalidate();
        }

        public void Clear()
        {
            lock (_renderLock)
            {
                StopAnimation();
                _displayBitmap = null;
                _spreadDisplay1 = null;
                _spreadDisplay2 = null;
                _scrollOffset = PointF.Empty;
            }
            Invalidate();
        }

        #region Fast Decode (delegated to ImageDecoder)

        /// <summary>ImageDecoder委譲</summary>
        public static SKBitmap? FastDecode(Stream stream, string ext, int maxW, int maxH, out int origW, out int origH)
            => ImageDecoder.FastDecode(stream, ext, maxW, maxH, out origW, out origH);

        // アニメーション用にStreamのコピーを保持（SKCodecがStreamを参照し続けるため）
        private MemoryStream? _animStream;

        /// <summary>アニメーション判定付きデコード（ImageViewer内部用）</summary>
        public SKBitmap? LoadFromStream(Stream stream, string ext, int maxW, int maxH, out int origW, out int origH)
        {
            origW = 0; origH = 0;
            try
            {
                StopAnimation();

                // 常にMemoryStreamにコピー（書庫Streamはシーク不可の場合がある）
                MemoryStream ms;
                if (stream is MemoryStream existing)
                {
                    ms = existing;
                }
                else
                {
                    ms = new MemoryStream();
                    stream.CopyTo(ms);
                    stream.Dispose();
                }
                ms.Position = 0;

                if (ext == ".gif" || ext == ".webp")
                {
                    SKCodec? codec = null;
                    try { codec = SKCodec.Create(ms); } catch (Exception ex) { Logger.Log($"Failed to create SKCodec: {ex.Message}"); }

                    if (codec != null && codec.FrameCount > 1)
                    {
                        // アニメーション: msの所有権をStartAnimationに移譲（_animStreamで保持）
                        _animStream = ms;
                        origW = codec.Info.Width;
                        origH = codec.Info.Height;
                        StartAnimation(codec);
                        return null;
                    }
                    // アニメーションではない: codecを閉じて静止画パスへ（msはcodecが閉じた後に再利用可能）
                    codec?.Dispose();
                    ms.Position = 0;
                }

                // 静止画デコード
                var bmp = FastDecode(ms, ext, maxW, maxH, out origW, out origH);
                ms.Dispose();
                if (bmp != null)
                {
                    lock (_renderLock)
                    {
                        _displayBitmap = bmp;
                        _origW = origW;
                        _origH = origH;
                        _isSpreadMode = false;
                        _scrollOffset = PointF.Empty;
                    }
                    ImageSizeChanged?.Invoke(origW, origH);
                    Invalidate();
                }
                return bmp;
            }
            catch (Exception ex)
            {
                Logger.Log($"LoadFromStream failed: {ex.Message}");
                ShowBitmap(null, 0, 0);
                return null;
            }
        }

        #endregion

        #region Animation

        // アニメーションの最大フレーム数（メモリ保護）
        private const int MaxAnimFrames = 50;
        // 1フレームあたりの最大ピクセル数（メモリ保護: 800x800=640K pixels ≈ 2.5MB/frame）
        private const int MaxAnimPixelsPerFrame = 640_000;

        private void StartAnimation(SKCodec codec)
        {
            _animCodec = codec;
            _animFrameCount = Math.Min(codec.FrameCount, MaxAnimFrames);
            _animCurrentFrame = 0;
            _animSkFrames = new SKBitmap?[_animFrameCount];

            // 表示サイズに縮小（メモリ節約＋変換高速化）
            int maxW = Math.Max(Width, 640);
            int maxH = Math.Max(Height, 480);
            float scale = Math.Min(1.0f, Math.Min((float)maxW / codec.Info.Width, (float)maxH / codec.Info.Height));

            // さらにピクセル数上限で制限
            int decW = Math.Max(1, (int)(codec.Info.Width * scale));
            int decH = Math.Max(1, (int)(codec.Info.Height * scale));
            if (decW * decH > MaxAnimPixelsPerFrame)
            {
                float pixScale = (float)Math.Sqrt((double)MaxAnimPixelsPerFrame / (decW * decH));
                decW = Math.Max(1, (int)(decW * pixScale));
                decH = Math.Max(1, (int)(decH * pixScale));
            }

            // 最初のフレームだけ即時デコード
            var firstGdi = DecodeAnimFrame(0, decW, decH);
            if (firstGdi != null)
            {
                _animSkFrames[0] = firstGdi;
                lock (_renderLock)
                {
                    _displayBitmap = firstGdi;
                    _origW = codec.Info.Width;
                    _origH = codec.Info.Height;
                    _isSpreadMode = false;
                }
                ImageSizeChanged?.Invoke(_origW, _origH);
                Invalidate();
            }

            var frameInfo = codec.FrameInfo;
            int interval = frameInfo.Length > 0 ? Math.Max(frameInfo[0].Duration, 10) : 100;
            _animTimer = new System.Windows.Forms.Timer { Interval = interval };
            _animTimer.Tick += AnimTimer_Tick;
            _animTimer.Start();

            // 全フレームをバックグラウンドでデコード（UIスレッドでは絶対にデコードしない）
            Task.Run(() =>
            {
                try
                {
                    for (int i = 1; i < _animFrameCount; i++)
                    {
                        var frames = _animSkFrames;
                        if (_animCodec == null || frames == null) break;
                        var decoded = DecodeAnimFrame(i, decW, decH);
                        Volatile.Write(ref frames[i], decoded);
                    }
                }
                catch (Exception ex) { Logger.Log($"Failed to decode animation frames: {ex.Message}"); }
            });
        }

        private SKBitmap? DecodeAnimFrame(int index, int targetW, int targetH)
        {
            if (_animCodec == null) return null;
            try
            {
                var info = new SKImageInfo(_animCodec.Info.Width, _animCodec.Info.Height);
                using var skBmp = new SKBitmap(info);
                _animCodec.GetPixels(info, skBmp.GetPixels(), new SKCodecOptions(index));

                // 表示サイズに縮小（メモリ節約）
                if (targetW < skBmp.Width || targetH < skBmp.Height)
                {
                    var resizeInfo = new SKImageInfo(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
                    var resized = skBmp.Resize(resizeInfo, SKFilterQuality.Low);
                    if (resized != null) return resized;
                }
                return skBmp.Copy(SKColorType.Bgra8888);
            }
            catch { return null; }
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_animSkFrames == null || _animCodec == null) return;
                _animCurrentFrame = (_animCurrentFrame + 1) % _animFrameCount;

                var frame = Volatile.Read(ref _animSkFrames[_animCurrentFrame]);
                if (frame != null)
                {
                    lock (_renderLock) { _displayBitmap = frame; }
                    Invalidate();
                }

                var frameInfo = _animCodec?.FrameInfo;
                if (frameInfo != null && _animCurrentFrame < frameInfo.Length && _animTimer != null)
                    _animTimer.Interval = Math.Max(frameInfo[_animCurrentFrame].Duration, 10);
            }
            catch (Exception ex)
            {
                Logger.Log($"AnimTimer error: {ex.Message}");
                StopAnimation();
            }
        }

        private void StopAnimation()
        {
            _animTimer?.Stop();
            _animTimer?.Dispose();
            _animTimer = null;
            if (_animSkFrames != null)
            {
                foreach (var f in _animSkFrames) f?.Dispose();
                _animSkFrames = null;
            }
            _animCodec?.Dispose();
            _animCodec = null;
            _animStream?.Dispose();
            _animStream = null;
        }

        #endregion

        #region Paint — Win32 StretchDIBits for maximum speed

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            lock (_renderLock)
            {
                try
                {
                    // クリッピング: 描画不要な領域をスキップ
                    e.Graphics.SetClip(e.ClipRectangle);

                    if (_isSpreadMode)
                        PaintSpreadFast(e);
                    else if (_displayBitmap != null)
                        PaintSingleFast(e);
                }
                catch (Exception ex) { Logger.Log($"Failed to paint image: {ex.Message}"); }
            }
        }

        private void PaintSingleFast(PaintEventArgs e)
        {
            if (_displayBitmap == null) return;
            // 元画像サイズでスケール計算（100% = 元画像の等倍）
            int scaleW = _origW > 0 ? _origW : _displayBitmap.Width;
            int scaleH = _origH > 0 ? _origH : _displayBitmap.Height;
            var (drawRect, scale) = CalcDrawRect(scaleW, scaleH);

            BlitBitmap(e.Graphics, _displayBitmap, drawRect);

            StatusChanged?.Invoke($"{(int)(scale * _zoom * 100)}%");
        }

        private void PaintSpreadFast(PaintEventArgs e)
        {
            // 2枚の画像を隙間なく中央に配置
            int viewW = Width, viewH = Height;

            float scale1 = 0, scale2 = 0;
            int drawW1 = 0, drawH1 = 0, drawW2 = 0, drawH2 = 0;

            if (_spreadDisplay1 != null)
            {
                scale1 = Math.Min(1.0f, (float)viewH / _spreadDisplay1.Height);
                drawW1 = (int)(_spreadDisplay1.Width * scale1);
                drawH1 = (int)(_spreadDisplay1.Height * scale1);
            }
            if (_spreadDisplay2 != null)
            {
                scale2 = Math.Min(1.0f, (float)viewH / _spreadDisplay2.Height);
                drawW2 = (int)(_spreadDisplay2.Width * scale2);
                drawH2 = (int)(_spreadDisplay2.Height * scale2);
            }

            int totalW = drawW1 + drawW2;

            // 合計幅がビューア幅を超える場合は縮小
            if (totalW > viewW && totalW > 0)
            {
                float fit = (float)viewW / totalW;
                drawW1 = (int)(drawW1 * fit);
                drawH1 = (int)(drawH1 * fit);
                drawW2 = (int)(drawW2 * fit);
                drawH2 = (int)(drawH2 * fit);
                totalW = drawW1 + drawW2;
            }

            // 中央寄せ
            int startX = (viewW - totalW) / 2;
            int y1 = (viewH - drawH1) / 2;
            int y2 = (viewH - drawH2) / 2;

            if (_spreadDisplay1 != null)
                BlitBitmap(e.Graphics, _spreadDisplay1, new RectangleF(startX, y1, drawW1, drawH1));
            if (_spreadDisplay2 != null)
                BlitBitmap(e.Graphics, _spreadDisplay2, new RectangleF(startX + drawW1, y2, drawW2, drawH2));
        }

        /// <summary>
        /// Win32 StretchDIBitsで最速描画（SKBitmapから直接）
        /// </summary>
        private void BlitBitmap(Graphics g, SKBitmap skBmp, RectangleF destRect)
        {
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = skBmp.Width,
                    biHeight = -skBmp.Height, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = (uint)(skBmp.RowBytes * skBmp.Height)
                }
            };

            IntPtr hdc = g.GetHdc();
            try
            {
                SetStretchBltMode(hdc, HALFTONE);
                StretchDIBits(hdc,
                    (int)destRect.X, (int)destRect.Y, (int)destRect.Width, (int)destRect.Height,
                    0, 0, skBmp.Width, skBmp.Height,
                    skBmp.GetPixels(), ref bmi, DIB_RGB_COLORS, SRCCOPY);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        private (RectangleF rect, float scale) CalcDrawRect(int imgW, int imgH)
            => CalcDrawRectInArea(imgW, imgH, ClientRectangle);

        private (RectangleF rect, float scale) CalcDrawRectInArea(int imgW, int imgH, Rectangle area)
        {
            float scale = _scaleMode switch
            {
                ScaleMode.FitWindow => Math.Min(1.0f, Math.Min((float)area.Width / imgW, (float)area.Height / imgH)),
                ScaleMode.FitWidth => Math.Min(1.0f, (float)area.Width / imgW),
                ScaleMode.FitHeight => Math.Min(1.0f, (float)area.Height / imgH),
                ScaleMode.Original => 1.0f,
                _ => 1.0f
            };
            scale *= _zoom;
            float drawW = imgW * scale, drawH = imgH * scale;
            float x = area.X + (area.Width - drawW) / 2 + _scrollOffset.X;
            float y = area.Y + (area.Height - drawH) / 2 + _scrollOffset.Y;
            return (new RectangleF(x, y, drawW, drawH), scale);
        }

        #endregion

        #region Mouse

        private void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            if (ModifierKeys.HasFlag(Keys.Control))
            { Zoom *= e.Delta > 0 ? 1.1f : 0.9f; }
            else
            { WheelNavigate?.Invoke(e.Delta > 0 ? -1 : 1); }
            if (e is HandledMouseEventArgs hme) hme.Handled = true;
        }

        private bool _panStarted; // 実際にドラッグが始まったか

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _panStarted = false;
                _panStart = e.Location;
                _panScrollStart = _scrollOffset;
            }
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                if (_panStarted) Cursor = Cursors.Default;
                _panStarted = false;
            }
        }

        private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) DoubleClickToggleFullscreen?.Invoke();
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                int dx = e.X - _panStart.X;
                int dy = e.Y - _panStart.Y;

                // ドラッグ距離が閾値を超えたらパン開始
                if (!_panStarted && (Math.Abs(dx) > 5 || Math.Abs(dy) > 5))
                {
                    if (!IsImageOverflowing())
                    {
                        _isPanning = false;
                        return;
                    }
                    _panStarted = true;
                    Cursor = Cursors.SizeAll;
                }

                if (_panStarted)
                {
                    _scrollOffset = new PointF(_panScrollStart.X + dx, _panScrollStart.Y + dy);
                    Invalidate();
                }
            }
        }

        private bool IsImageOverflowing()
        {
            if (_isSpreadMode) return true;
            if (_displayBitmap == null) return false;
            var (rect, _) = CalcDrawRect(_displayBitmap.Width, _displayBitmap.Height);
            return rect.Width > Width || rect.Height > Height;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing) StopAnimation();
            base.Dispose(disposing);
        }
    }
}
