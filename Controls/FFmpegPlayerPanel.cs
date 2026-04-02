using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;

namespace leeyez_kai.Controls
{
    /// <summary>
    /// FFmpegプロセスパイプ方式の動画/音声プレイヤー
    /// ffmpeg.exe を使って動画フレーム(rawvideo)と音声(PCM)を取得
    /// </summary>
    public class FFmpegPlayerPanel : Panel
    {
        private static string? _ffmpegPath;
        private Process? _videoProcess;
        private Process? _audioProcess;
        private CancellationTokenSource? _playCts;
        private bool _isPlaying;
        private bool _isPaused;

        // 映像
        private string? _currentFilePath;
        private int _videoWidth, _videoHeight;
        private double _fps = 30;
        private Bitmap? _currentFrame;
        private Bitmap? _backBuffer; // 再利用バッファ
        private readonly object _frameLock = new();
        private readonly Panel _renderPanel;

        // 音声
        private WaveOutEvent? _waveOut;
        private BufferedWaveProvider? _waveProvider;
        private float _volumeLevel = 1.0f;
        private bool _isMuted;

        // タイミング
        private TimeSpan _duration;
        private TimeSpan _currentTime;
        private readonly System.Windows.Forms.Timer _updateTimer;
        private DateTime _playStartTime;

        // UI
        private readonly Panel _controlBar;
        private readonly Button _playPauseBtn;
        private readonly Label _timeLabel;
        private readonly Button _volumeBtn;
        private readonly Button _fullscreenBtn;
        private readonly Button _loopBtn;
        private readonly Button _speedBtn;
        private readonly Panel _seekPanel;
        private readonly Panel _volumePanel;
        private float _seekPosition;
        private bool _isSeeking;

        // ループ・速度
        private bool _isLoop;
        private double _playbackSpeed = 1.0;
        private static readonly double[] Speeds = { 0.25, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0 };

        public event Action? FullscreenRequested;
        public event Action<int>? WheelNavigate;

        public FFmpegPlayerPanel()
        {
            BackColor = Color.Black;

            _renderPanel = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Black };
            _renderPanel.Paint += RenderPanel_Paint;
            _renderPanel.MouseWheel += OnWheel;

            // シングルクリック=再生/停止、ダブルクリック=全画面（両方発火しないようタイマー制御）
            var clickTimer = new System.Windows.Forms.Timer { Interval = SystemInformation.DoubleClickTime };
            bool waitingDoubleClick = false;
            clickTimer.Tick += (s, e) => { clickTimer.Stop(); waitingDoubleClick = false; TogglePlayPause(); };
            _renderPanel.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (e.Clicks == 2) { clickTimer.Stop(); waitingDoubleClick = false; FullscreenRequested?.Invoke(); }
                else { waitingDoubleClick = true; clickTimer.Start(); }
            };
            Controls.Add(_renderPanel);

            _controlBar = new Panel
            {
                Dock = DockStyle.Bottom, Height = 50,
                BackColor = Color.FromArgb(220, 24, 24, 24)
            };

            _seekPanel = new DoubleBufferedPanel { Height = 16, Dock = DockStyle.Top, BackColor = Color.Transparent, Cursor = Cursors.Hand };
            _seekPanel.Paint += SeekPanel_Paint;
            _seekPanel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { _isSeeking = true; UpdateSeekBar(e.X); } };
            _seekPanel.MouseMove += (s, e) => { if (_isSeeking && e.Button == MouseButtons.Left) UpdateSeekBar(e.X); };
            _seekPanel.MouseUp += SeekPanel_MouseUp;

            _playPauseBtn = CreateBtn("▶", 14f);
            _playPauseBtn.Click += (s, e) => TogglePlayPause();
            _timeLabel = new Label { Text = "0:00 / 0:00", ForeColor = Color.FromArgb(180, 180, 180), BackColor = Color.Transparent, AutoSize = false, Size = new Size(120, 24), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9f) };
            _volumeBtn = CreateBtn("🔊", 11f);
            _volumeBtn.Click += (s, e) => { _isMuted = !_isMuted; ApplyVolume(); _volumeBtn.Text = _isMuted ? "🔇" : "🔊"; };
            _volumePanel = new Panel { Size = new Size(80, 14), BackColor = Color.Transparent, Visible = false, Cursor = Cursors.Hand };
            _volumePanel.Paint += VolumePanel_Paint;
            _volumePanel.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) UpdateVol(e.X); };
            _volumePanel.MouseMove += (s, e) => { if (e.Button == MouseButtons.Left) UpdateVol(e.X); };
            _volumeBtn.MouseEnter += (s, e) => _volumePanel.Visible = true;
            _volumePanel.MouseLeave += (s, e) => _volumePanel.Visible = false;
            // ループボタン
            _loopBtn = CreateBtn("🔁", 10f);
            _loopBtn.Size = new Size(32, 32);
            _loopBtn.Click += (s, e) => { _isLoop = !_isLoop; _loopBtn.ForeColor = _isLoop ? Color.FromArgb(0x42, 0x85, 0xF4) : Color.White; };

            // 速度変更ボタン
            _speedBtn = new Button
            {
                Text = "1×", FlatStyle = FlatStyle.Flat, ForeColor = Color.White,
                BackColor = Color.Transparent, Size = new Size(38, 22),
                Cursor = Cursors.Hand, TabStop = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _speedBtn.FlatAppearance.BorderSize = 1;
            _speedBtn.FlatAppearance.BorderColor = Color.FromArgb(80, 255, 255, 255);
            _speedBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 255, 255, 255);
            _speedBtn.Click += (s, e) => ShowSpeedMenu();

            _fullscreenBtn = CreateBtn("⛶", 13f);
            _fullscreenBtn.Click += (s, e) => FullscreenRequested?.Invoke();

            _controlBar.Controls.AddRange(new Control[] { _seekPanel, _playPauseBtn, _timeLabel, _loopBtn, _speedBtn, _volumeBtn, _volumePanel, _fullscreenBtn });
            _controlBar.Resize += (s, e) => LayoutControls();
            Controls.Add(_controlBar);
            _controlBar.BringToFront();

            _updateTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _updateTimer.Tick += (s, e) => UpdateTimerTick();
            MouseWheel += OnWheel;
            LayoutControls();
        }

        private void OnWheel(object? s, MouseEventArgs e)
        {
            WheelNavigate?.Invoke(e.Delta > 0 ? -1 : 1);
            if (e is HandledMouseEventArgs hme) hme.Handled = true;
        }

        private void LayoutControls()
        {
            int w = _controlBar.ClientSize.Width, y = _seekPanel.Bottom + 2, bh = 32;
            _playPauseBtn.SetBounds(8, y, bh, bh);
            _timeLabel.Location = new Point(44, y + 4);

            // 右端から: 全画面 → 音量 → 音量バー → 速度 → ループ
            _fullscreenBtn.SetBounds(w - bh - 8, y, bh, bh);
            _volumeBtn.SetBounds(w - bh * 2 - 16, y, bh, bh);
            _volumePanel.Location = new Point(w - bh * 2 - 100, y + 9);
            _speedBtn.Location = new Point(w - bh * 2 - 100 - 44, y + 5);
            _loopBtn.SetBounds(w - bh * 2 - 100 - 44 - bh - 4, y, bh, bh);
        }

        #region FFmpeg

        private static string FindFFmpeg()
        {
            if (_ffmpegPath != null) return _ffmpegPath;

            var candidates = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "ffmpeg.exe"),
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                "ffmpeg"
            };
            foreach (var c in candidates)
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(c, "-version") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
                    p?.WaitForExit(2000);
                    if (p?.ExitCode == 0) { _ffmpegPath = c; return c; }
                }
                catch { }
            }
            _ffmpegPath = "ffmpeg";
            return _ffmpegPath;
        }

        private (int w, int h, double fps, TimeSpan duration) ProbeMedia(string path)
        {
            int w = 0, h = 0; double fps = 30; var dur = TimeSpan.Zero;
            try
            {
                var ffprobe = FindFFmpeg().Replace("ffmpeg", "ffprobe");
                var psi = new ProcessStartInfo(ffprobe, $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -show_entries format=duration -of csv=p=0 \"{path}\"")
                { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit(5000);

                var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(',');
                    if (parts.Length >= 3 && int.TryParse(parts[0], out int pw) && int.TryParse(parts[1], out int ph))
                    {
                        w = pw; h = ph;
                        var fpsStr = parts[2].Trim();
                        if (fpsStr.Contains('/'))
                        {
                            var fp = fpsStr.Split('/');
                            if (double.TryParse(fp[0], out double n) && double.TryParse(fp[1], out double d) && d > 0)
                                fps = n / d;
                        }
                        else if (double.TryParse(fpsStr, out double f)) fps = f;
                    }
                    if (parts.Length == 1 && double.TryParse(parts[0], out double durSec))
                        dur = TimeSpan.FromSeconds(durSec);
                }
            }
            catch (Exception ex) { Logger.Log($"ProbeMedia error: {ex.Message}"); }
            if (w == 0) { w = 1280; h = 720; }
            if (fps <= 0 || fps > 120) fps = 30;
            return (w, h, fps, dur);
        }

        #endregion

        #region Playback

        public void Play(string filePath) => PlayAt(filePath, TimeSpan.Zero);

        private void PlayAt(string filePath, TimeSpan startTime)
        {
            StopInternal(resetUI: false);
            _currentFilePath = filePath;

            var (w, h, fps, dur) = ProbeMedia(filePath);
            _videoWidth = w; _videoHeight = h; _fps = fps; _duration = dur;
            _playCts = new CancellationTokenSource();
            var ct = _playCts.Token;

            try
            {
                var ffmpeg = FindFFmpeg();

                var ssArg = startTime.TotalSeconds > 0.5 ? $"-ss {startTime.TotalSeconds:F3} " : "";

                _videoProcess = new Process
                {
                    StartInfo = new ProcessStartInfo(ffmpeg,
                        $"{ssArg}-i \"{filePath}\" -an {(_playbackSpeed != 1.0 ? $"-vf \"setpts={1.0 / _playbackSpeed:F4}*PTS\" " : "")}-f rawvideo -pix_fmt bgra -v quiet -")
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }
                };
                _videoProcess.Start();

                // -vn: 映像無視、-ac 2 -ar 44100: ステレオ44.1kHz強制
                // -channel_layout stereo: チャンネルレイアウト強制（5.1ch等を2chに変換）
                // -vn: 映像無視、-ac 2: ステレオにダウンミックス（5.1ch等も自動変換）
                _audioProcess = new Process
                {
                    StartInfo = new ProcessStartInfo(ffmpeg,
                        $"{ssArg}-i \"{filePath}\" -vn {(_playbackSpeed != 1.0 ? $"-af \"atempo={_playbackSpeed:F4}\" " : "")}-ac 2 -ar 44100 -sample_fmt s16 -f s16le -v quiet -")
                    { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }
                };
                _audioProcess.Start();

                // 音声出力
                var waveFormat = new WaveFormat(44100, 16, 2);
                _waveProvider = new BufferedWaveProvider(waveFormat) { BufferDuration = TimeSpan.FromSeconds(10), DiscardOnBufferOverflow = true };
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_waveProvider);
                ApplyVolume();
                _waveOut.Play();

                _isPlaying = true; _isPaused = false;
                _playPauseBtn.Text = "⏸";
                _playStartTime = DateTime.UtcNow;
                _currentTime = startTime;
                _seekPosition = _duration.TotalSeconds > 0 ? (float)(startTime.TotalSeconds / _duration.TotalSeconds) : 0;
                _updateTimer.Start();

                // 映像デコードスレッド
                var videoStartOffset = startTime;
                Task.Run(() => VideoReadLoop(ct, videoStartOffset), ct);
                // 音声デコードスレッド
                Task.Run(() => AudioReadLoop(ct), ct);
            }
            catch (Exception ex)
            {
                Logger.Log($"FFmpeg Play failed: {ex.Message}");
                Stop();
            }
        }

        private void VideoReadLoop(CancellationToken ct, TimeSpan startOffset = default)
        {
            try
            {
                var stream = _videoProcess?.StandardOutput.BaseStream;
                if (stream == null) return;

                int frameSize = _videoWidth * _videoHeight * 4;
                var buffer = new byte[frameSize];
                double frameInterval = 1000.0 / _fps;
                int frameCount = 0;
                double offsetMs = startOffset.TotalMilliseconds;

                while (!ct.IsCancellationRequested)
                {
                    if (_isPaused) { Thread.Sleep(10); continue; }

                    int read = 0;
                    while (read < frameSize)
                    {
                        int n = stream.Read(buffer, read, frameSize - read);
                        if (n == 0) goto done; // EOF
                        read += n;
                    }

                    // Bitmapバッファ再利用（毎フレームnew/Disposeしない）
                    lock (_frameLock)
                    {
                        Bitmap bmp;
                        if (_backBuffer != null && _backBuffer.Width == _videoWidth && _backBuffer.Height == _videoHeight)
                            bmp = _backBuffer;
                        else
                        {
                            _backBuffer?.Dispose();
                            bmp = new Bitmap(_videoWidth, _videoHeight, PixelFormat.Format32bppPArgb);
                        }
                        var data = bmp.LockBits(new Rectangle(0, 0, _videoWidth, _videoHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                        Marshal.Copy(buffer, 0, data.Scan0, Math.Min(frameSize, data.Stride * _videoHeight));
                        bmp.UnlockBits(data);
                        _backBuffer = _currentFrame; // 前のフレームをバックバッファに
                        _currentFrame = bmp;
                    }
                    _renderPanel.BeginInvoke(() => _renderPanel.Invalidate());

                    frameCount++;
                    _currentTime = TimeSpan.FromMilliseconds(offsetMs + frameCount * frameInterval);

                    // フレームレート制御
                    var elapsed = (DateTime.UtcNow - _playStartTime).TotalMilliseconds;
                    var target = frameCount * frameInterval;
                    if (target > elapsed)
                    {
                        int delay = (int)(target - elapsed);
                        if (delay > 0 && delay < 1000) Thread.Sleep(delay);
                    }
                }
                done:;

                // ループ再生
                if (_isLoop && !ct.IsCancellationRequested && _currentFilePath != null)
                {
                    try { BeginInvoke(() => PlayAt(_currentFilePath!, TimeSpan.Zero)); } catch { }
                    return;
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) Logger.Log($"VideoReadLoop error: {ex.Message}");
            }
            finally
            {
                if (!ct.IsCancellationRequested && !_isLoop)
                    try { BeginInvoke(() => { _playPauseBtn.Text = "▶"; _isPlaying = false; }); } catch { }
            }
        }

        private void AudioReadLoop(CancellationToken ct)
        {
            try
            {
                var stream = _audioProcess?.StandardOutput.BaseStream;
                if (stream == null) return;

                var buffer = new byte[16384];
                while (!ct.IsCancellationRequested)
                {
                    if (_isPaused) { Thread.Sleep(10); continue; }

                    // バッファが一杯に近いときは待つ（音ズレ防止）
                    if (_waveProvider != null && _waveProvider.BufferedBytes > _waveProvider.BufferLength * 0.8)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    int n = stream.Read(buffer, 0, buffer.Length);
                    if (n == 0) break;
                    _waveProvider?.AddSamples(buffer, 0, n);
                }
            }
            catch (Exception ex)
            {
                if (!ct.IsCancellationRequested) Logger.Log($"AudioReadLoop error: {ex.Message}");
            }
        }

        public void Stop() => StopInternal(resetUI: true);

        private void StopInternal(bool resetUI)
        {
            _playCts?.Cancel();
            _isPlaying = false; _isPaused = false;
            _updateTimer.Stop();

            _waveOut?.Stop(); _waveOut?.Dispose(); _waveOut = null; _waveProvider = null;

            try { _videoProcess?.Kill(); } catch { }
            try { _audioProcess?.Kill(); } catch { }
            _videoProcess?.Dispose(); _videoProcess = null;
            _audioProcess?.Dispose(); _audioProcess = null;

            _currentTime = TimeSpan.Zero;
            lock (_frameLock) { _currentFrame?.Dispose(); _currentFrame = null; }

            if (resetUI)
            {
                _playPauseBtn.Text = "▶";
                _timeLabel.Text = "0:00 / 0:00";
                _seekPosition = 0;
                _seekPanel.Invalidate();
            }
        }

        public void TogglePlayPause()
        {
            if (!_isPlaying) return;
            _isPaused = !_isPaused;
            _playPauseBtn.Text = _isPaused ? "▶" : "⏸";
            if (_waveOut != null) { if (_isPaused) _waveOut.Pause(); else _waveOut.Play(); }
            if (!_isPaused) _playStartTime = DateTime.UtcNow - _currentTime;
        }

        #endregion

        #region Paint

        private void RenderPanel_Paint(object? sender, PaintEventArgs e)
        {
            lock (_frameLock)
            {
                if (_currentFrame == null) return;
                var r = FitRect(_currentFrame.Width, _currentFrame.Height, _renderPanel.ClientRectangle);
                e.Graphics.InterpolationMode = InterpolationMode.Low;
                e.Graphics.DrawImage(_currentFrame, r);
            }
        }

        private static Rectangle FitRect(int iw, int ih, Rectangle area)
        {
            float s = Math.Min((float)area.Width / iw, (float)area.Height / ih);
            int w = (int)(iw * s), h = (int)(ih * s);
            return new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
        }

        #endregion

        #region Seek / Volume / Timer

        private void UpdateSeekBar(int mouseX)
        {
            int margin = 8, trackW = _seekPanel.Width - margin * 2;
            _seekPosition = Math.Clamp((mouseX - margin) / (float)trackW, 0f, 1f);
            _seekPanel.Invalidate();
        }

        private void SeekPanel_MouseUp(object? sender, MouseEventArgs e)
        {
            if (!_isSeeking) return;
            _isSeeking = false;

            if (_currentFilePath != null && _duration.TotalSeconds > 0)
            {
                var target = TimeSpan.FromSeconds(_duration.TotalSeconds * _seekPosition);
                PlayAt(_currentFilePath, target);
            }
        }

        private void SeekPanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _seekPanel.Width, m = 8, tw = w - m * 2;
            using var bg = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.FillRectangle(bg, m, 7, tw, 3);
            int f = (int)(tw * _seekPosition);
            using var fb = new SolidBrush(Color.FromArgb(0x42, 0x85, 0xF4));
            g.FillRectangle(fb, m, 7, f, 3);
            g.FillEllipse(fb, m + f - 5, 4, 10, 10);
        }

        private void VolumePanel_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _volumePanel.Width, bh = 3, by = (_volumePanel.Height - bh) / 2;
            using var bg = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
            g.FillRectangle(bg, 0, by, w, bh);
            int f = (int)(w * _volumeLevel);
            using var fb = new SolidBrush(Color.White);
            g.FillRectangle(fb, 0, by, f, bh);
            g.FillEllipse(fb, f - 4, by - 3, 8, 8);
        }

        private void UpdateVol(int mouseX)
        {
            _volumeLevel = Math.Clamp(mouseX / (float)_volumePanel.Width, 0f, 1f);
            _isMuted = false; ApplyVolume();
            _volumeBtn.Text = _volumeLevel == 0 ? "🔇" : "🔊";
            _volumePanel.Invalidate();
        }

        private void ApplyVolume()
        {
            if (_waveOut != null) _waveOut.Volume = _isMuted ? 0 : _volumeLevel;
        }

        private void ShowSpeedMenu()
        {
            var menu = new ContextMenuStrip();
            foreach (var spd in Speeds)
            {
                var item = menu.Items.Add($"{spd:G}×");
                var speed = spd;
                if (spd == _playbackSpeed)
                    item.Font = new Font(item.Font, FontStyle.Bold);
                item.Click += (s, e) =>
                {
                    _playbackSpeed = speed;
                    _speedBtn.Text = speed == 1.0 ? "1×" : $"{speed:G}×";
                    _speedBtn.ForeColor = speed != 1.0 ? Color.FromArgb(0x42, 0x85, 0xF4) : Color.White;
                    if (_currentFilePath != null && _isPlaying)
                        PlayAt(_currentFilePath, _currentTime);
                };
            }
            menu.Show(_speedBtn, new Point(0, -menu.PreferredSize.Height));
        }

        private void UpdateTimerTick()
        {
            if (_duration.TotalSeconds > 0 && !_isSeeking)
            {
                _seekPosition = (float)(_currentTime.TotalSeconds / _duration.TotalSeconds);
                _seekPanel.Invalidate();
            }
            _timeLabel.Text = $"{Fmt(_currentTime)} / {Fmt(_duration)}";
        }

        private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

        #endregion

        private Button CreateBtn(string text, float fs)
        {
            var b = new Button { Text = text, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.Transparent, Size = new Size(32, 32), Cursor = Cursors.Hand, TabStop = false, Font = new Font("Segoe UI Symbol", fs) };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(50, 255, 255, 255);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(80, 255, 255, 255);
            return b;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
                _backBuffer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>ちらつき防止のダブルバッファリングPanel</summary>
    internal class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
    }
}
