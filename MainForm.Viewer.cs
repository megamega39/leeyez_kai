using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SkiaSharp;
using leeyez_kai.Controls;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm
    {
        private void UpdateViewableFiles()
        {
            if (_fileListManager == null) return;
            _viewableFiles = _fileListManager.Items
                .Where(f => !f.IsDirectory && f.IsViewable)
                .ToList();
            _currentFileIndex = -1;
            UpdatePageLabel();
            RefreshGridItems();
        }

        // 書庫クリックのデバウンス
        private System.Windows.Forms.Timer? _archiveDebounce;
        private string? _pendingArchivePath;

        private void OnFileSelected(FileItem item)
        {
            if (item.IsDirectory) return;

            if (item.IsImage)
            {
                _currentFileIndex = _viewableFiles.FindIndex(f => f.FullPath == item.FullPath);
                ShowCurrentFile();
            }
            else if (item.IsMedia)
            {
                _currentFileIndex = _viewableFiles.FindIndex(f => f.FullPath == item.FullPath);
                ShowMedia(item);
            }
            else if (item.IsArchiveFile || item.IsArchiveExt)
            {
                // デバウンス: 高速クリック時は最後の書庫だけ開く
                _pendingArchivePath = item.FullPath;
                if (_archiveDebounce == null)
                {
                    _archiveDebounce = new System.Windows.Forms.Timer { Interval = 300 };
                    _archiveDebounce.Tick += (s, e) =>
                    {
                        _archiveDebounce.Stop();
                        if (_pendingArchivePath != null)
                            OpenArchiveAndShowFirstImage(_pendingArchivePath);
                    };
                }
                _archiveDebounce.Stop();
                _archiveDebounce.Start();
            }
        }

        private void OnFileDoubleClicked(FileItem item)
        {
            if (item.IsDirectory || item.IsArchiveFile)
                NavigateTo(item.FullPath);
            else
                OnFileSelected(item);
        }

        private void GoToFile(int index)
        {
            if (_viewableFiles.Count == 0) return;

            if (index < 0) index = _viewableFiles.Count - 1;
            if (index >= _viewableFiles.Count) index = 0;
            if (_currentFileIndex >= 0) _navDirection = index > _currentFileIndex ? 1 : -1;
            _currentFileIndex = index;
            UpdatePageLabel();
            AutoSaveState();

            // ファイルリスト同期（見開き判定後に行う）
            SyncFileListSelection(index);

            var item = _viewableFiles[index];
            if (item.IsMedia) { ShowMedia(item); return; }

            // キャッシュヒット → ShowCurrentFileで見開き判定を含めて表示
            if (_imageCache.Contains(item.FullPath))
            {
                ShowCurrentFile();
                return;
            }

            // キャッシュミス → デバウンスで非同期デコード
            _pendingFileIndex = index;
            if (_debounceTimer == null)
            {
                _debounceTimer = new System.Windows.Forms.Timer { Interval = AppConstants.DebounceMs };
                _debounceTimer.Tick += DebounceTimer_Tick;
            }
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void SyncFileListSelection(int index)
        {
            var item = _viewableFiles[index];

            // VirtualGridPanel対応
            if (_isGridMode && _virtualGrid.Visible)
            {
                _virtualGrid.SelectByPath(item.FullPath);
                return;
            }

            // ListView対応
            var indices = new List<int>();
            var listIndex = _fileListManager?.GetIndex(item) ?? -1;
            if (listIndex >= 0) indices.Add(listIndex);

            if (GetPagesPerView() == 2 && index + 1 < _viewableFiles.Count)
            {
                var item2 = _viewableFiles[index + 1];
                var idx2 = _fileListManager?.GetIndex(item2) ?? -1;
                if (idx2 >= 0) indices.Add(idx2);
            }
            if (indices.Count > 0) _fileListManager?.SelectItems(indices);
        }

        private void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer?.Stop();
            if (_pendingFileIndex < 0 || _pendingFileIndex >= _viewableFiles.Count) return;
            _currentFileIndex = _pendingFileIndex;

            // 非同期デコード: UIスレッドをブロックしない
            var file = _viewableFiles[_currentFileIndex];

            // GIF/WebPはアニメーション判定が必要なのでUIスレッドで
            if (file.Ext == ".gif" || file.Ext == ".webp")
            {
                ShowCurrentFile();
                return;
            }

            // キャッシュヒットならUIスレッドで即表示
            if (_imageCache.Contains(file.FullPath))
            {
                ShowCurrentFile();
                return;
            }

            // キャッシュミス: バックグラウンドでデコード（見開き時は2枚同時）
            var idx = _currentFileIndex;
            var pagesPerView = GetPagesPerView();
            FileItem? file2 = (pagesPerView == 2 && idx + 1 < _viewableFiles.Count) ? _viewableFiles[idx + 1] : null;

            Task.Run(() =>
            {
                try
                {
                    var (maxW, maxH) = GetMaxDecodeSize();

                    // 1枚目デコード
                    if (!_imageCache.Contains(file.FullPath))
                    {
                        using var stream = GetFileStream(file);
                        if (stream != null)
                        {
                            var bmp = ImageViewer.FastDecode(stream, file.Ext, maxW, maxH, out int ow, out int oh);
                            if (bmp != null && !_imageCache.Put(file.FullPath, bmp, ow, oh))
                                bmp.Dispose();
                        }
                    }

                    // 2枚目デコード（見開き時）
                    if (file2 != null && !_imageCache.Contains(file2.FullPath))
                    {
                        if (file2.Ext != ".gif")
                        {
                            using var stream2 = GetFileStream(file2);
                            if (stream2 != null)
                            {
                                var bmp2 = ImageViewer.FastDecode(stream2, file2.Ext, maxW, maxH, out int ow2, out int oh2);
                                if (bmp2 != null && !_imageCache.Put(file2.FullPath, bmp2, ow2, oh2))
                                    bmp2.Dispose();
                            }
                        }
                    }

                    BeginInvoke(() =>
                    {
                        if (_currentFileIndex == idx)
                            ShowCurrentFile();
                    });
                }
                catch (Exception ex) { Logger.Log($"AsyncDecode failed: {ex.Message}"); }
            });
        }

        private void ShowCurrentFile()
        {
            try
            {
                if (_currentFileIndex < 0 || _currentFileIndex >= _viewableFiles.Count) return;
                var file = _viewableFiles[_currentFileIndex];

                UpdatePageLabel();

                if (file.IsMedia) { ShowMedia(file); return; }

                _mediaPlayer.Stop();
                _mediaPlayer.Visible = false;
                _imageViewer.Visible = true;

                // 見開き判定
                // モード2（手動見開き）: 次のファイルがあれば常に見開き
                // モード0（自動）: taureader互換の判定
                bool isSpread;
                if (_viewMode == 2)
                    isSpread = _currentFileIndex + 1 < _viewableFiles.Count;
                else if (_viewMode == 0)
                    isSpread = ShouldShowSpread(_currentFileIndex);
                else
                    isSpread = false;

                if (isSpread)
                {
                    int nextIdx = _currentFileIndex + 1;
                    var file2 = _viewableFiles[nextIdx];
                    Parallel.Invoke(() => LoadAndCacheImage(file), () => LoadAndCacheImage(file2));
                    var bmp1 = _imageCache.Get(file.FullPath);
                    var bmp2 = _imageCache.Get(file2.FullPath);
                    if (bmp1 != null || bmp2 != null)
                    {
                        _imageViewer.ShowSpread(
                            _isRTL ? bmp2 : bmp1,
                            _isRTL ? bmp1 : bmp2);
                        _statusLeft.Text = file.FullPath;
                        StartPrefetch();
                        return;
                    }
                    // 両方nullなら前の表示を維持（1ページにフォールバックしない）
                    // 非同期デコードが完了したらShowCurrentFileが再呼び出しされる
                    return;
                }

                LoadAndShowImage(file, synchronous: true);
                StartPrefetch();
            }
            catch (Exception ex) { Logger.Log($"ShowCurrentFile error: {ex.Message}"); }
        }

        private void LoadAndCacheImage(FileItem file)
        {
            if (_imageCache.Contains(file.FullPath)) return;
            try
            {
                using var stream = GetFileStream(file);
                if (stream != null)
                {
                    var (maxW, maxH) = GetMaxDecodeSize();
                    var bmp = ImageViewer.FastDecode(stream, file.Ext, maxW, maxH, out int ow, out int oh);
                    if (bmp != null && !_imageCache.Put(file.FullPath, bmp, ow, oh))
                        bmp.Dispose();
                }
            }
            catch (Exception ex) { Logger.Log($"Failed to load and cache image: {ex.Message}"); }
        }

        private void LoadAndShowImage(FileItem file, bool synchronous = false)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // キャッシュヒットなら即表示（全形式共通）
            var cached = _imageCache.Get(file.FullPath);
            if (cached != null)
            {
                var (ow, oh) = _imageCache.GetOriginalSize(file.FullPath);
                _imageViewer.ShowBitmap(cached, ow, oh);
                _statusLeft.Text = file.FullPath;
                Logger.Log($"[PERF] Cache HIT: {sw.ElapsedMilliseconds}ms {file.Name}");
                return;
            }

            try
            {
                var swStream = System.Diagnostics.Stopwatch.StartNew();
                var stream = GetFileStream(file);
                var streamMs = swStream.ElapsedMilliseconds;
                if (stream == null) return;

                // GIFのみアニメーション判定（WebPは静止画として処理→軽量化）
                if (file.Ext == ".gif")
                {
                    var (maxW, maxH) = GetMaxDecodeSize();
                    var bmp = _imageViewer.LoadFromStream(stream, file.Ext, maxW, maxH, out int ow, out int oh);
                    if (bmp != null && !_imageCache.Put(file.FullPath, bmp, ow, oh))
                        bmp.Dispose();
                    _statusLeft.Text = file.FullPath;
                    Logger.Log($"[PERF] GIF: stream={streamMs}ms total={sw.ElapsedMilliseconds}ms {ow}x{oh} {file.Name}");
                    return;
                }

                // 静止画: FastDecodeで直接デコード
                using (stream)
                {
                    var (maxW, maxH) = GetMaxDecodeSize();
                    var swDecode = System.Diagnostics.Stopwatch.StartNew();
                    var skBmp = ImageViewer.FastDecode(stream, file.Ext, maxW, maxH, out int origW, out int origH);
                    var decodeMs = swDecode.ElapsedMilliseconds;

                    if (skBmp != null)
                    {
                        if (!_imageCache.Put(file.FullPath, skBmp, origW, origH))
                        {
                            skBmp.Dispose();
                            skBmp = _imageCache.Get(file.FullPath)!;
                            (origW, origH) = _imageCache.GetOriginalSize(file.FullPath);
                        }
                        _imageViewer.ShowBitmap(skBmp, origW, origH);
                        _statusLeft.Text = file.FullPath;
                    }
                    else
                    {
                        _imageViewer.ShowBitmap(null, 0, 0);
                    }
                    Logger.Log($"[PERF] Decode: stream={streamMs}ms decode={decodeMs}ms total={sw.ElapsedMilliseconds}ms {origW}x{origH} {file.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"LoadAndShowImage failed: {ex.Message}");
                try { _imageViewer.ShowBitmap(null, 0, 0); } catch { }
            }
        }

        private void StartPrefetch()
        {
            _prefetchCts?.Cancel();
            _prefetchCts?.Dispose();
            _prefetchCts = new CancellationTokenSource();
            var ct = _prefetchCts.Token;
            var currentIdx = _currentFileIndex;
            var currentArchive = _currentArchivePath;
            var (maxW, maxH) = GetMaxDecodeSize();

            Task.Run(() =>
            {
                try
                {
                    // プリフェッチ対象を収集（ナビゲーション方向を優先）
                    var targets = new List<(int idx, FileItem file)>();
                    int dir = _navDirection; // 1=前方向, -1=後ろ方向
                    int majorCount = AppConstants.PrefetchCount * 3 / 4; // 75% 進行方向
                    int minorCount = AppConstants.PrefetchCount - majorCount; // 25% 逆方向

                    // 進行方向を先に（dir=1なら+i、dir=-1なら-i）
                    for (int i = 1; i <= majorCount; i++)
                    {
                        int idx = currentIdx + i * dir;
                        if (idx >= 0 && idx < _viewableFiles.Count)
                        {
                            var f = _viewableFiles[idx];
                            if (!_imageCache.Contains(f.FullPath) && f.IsImage && f.Ext != ".gif"
                                && !(f.Ext == ".webp" && f.Size > 5 * 1024 * 1024))
                                targets.Add((idx, f));
                        }
                    }
                    // 逆方向（dir=1なら-i、dir=-1なら+i）
                    for (int i = 1; i <= minorCount; i++)
                    {
                        int idx = currentIdx + i * (-dir);
                        if (idx >= 0 && idx < _viewableFiles.Count)
                        {
                            var f = _viewableFiles[idx];
                            if (!_imageCache.Contains(f.FullPath) && f.IsImage && f.Ext != ".gif"
                                && !(f.Ext == ".webp" && f.Size > 5 * 1024 * 1024))
                                targets.Add((idx, f));
                        }
                    }

                    if (targets.Count == 0) return;

                    // 書庫モード: 一括展開でStreamキャッシュに先行投入
                    if (currentArchive != null && !ct.IsCancellationRequested)
                        BulkExtractForPrefetch(targets, ct, currentArchive);

                    if (ct.IsCancellationRequested) return;

                    // 4並列でデコード（マルチコア活用）
                    Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, target =>
                    {
                        if (ct.IsCancellationRequested) return;
                        try
                        {
                            using var stream = GetFileStream(target.file);
                            if (stream != null)
                            {
                                var bmp = ImageViewer.FastDecode(stream, target.file.Ext, maxW, maxH, out int ow, out int oh);
                                if (bmp != null)
                                {
                                    if (ct.IsCancellationRequested || !_imageCache.Put(target.file.FullPath, bmp, ow, oh))
                                        bmp.Dispose();
                                }
                            }
                        }
                        catch (Exception ex) { Logger.Log($"Failed to prefetch image: {ex.Message}"); }
                    });
                }
                catch (Exception ex) { Logger.Log($"Failed to start prefetch task: {ex.Message}"); }
            }, ct);
        }

        /// <summary>書庫モード時、プリフェッチ対象を一括展開してStreamキャッシュに投入</summary>
        private void BulkExtractForPrefetch(List<(int idx, FileItem file)> targets, CancellationToken ct, string archivePath)
        {
            var prefix = archivePath + "!";

            var entryKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, file) in targets)
            {
                if (ct.IsCancellationRequested) return;
                lock (_streamCacheLock)
                {
                    if (_archiveStreamCache.ContainsKey(file.FullPath)) continue;
                }
                if (file.FullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    entryKeys.Add(file.FullPath.Substring(prefix.Length));
            }

            if (entryKeys.Count == 0) return;
            if (ct.IsCancellationRequested || _currentArchivePath != archivePath) return;

            var extracted = ArchiveService.ExtractAll(archivePath, _sevenZipLibPath, entryKeys, ct);
            foreach (var (entryKey, bytes) in extracted)
            {
                if (ct.IsCancellationRequested || _currentArchivePath != archivePath) return;
                var fullPath = prefix + entryKey;
                if (bytes.Length >= 2 * 1024 * 1024) continue;

                lock (_streamCacheLock)
                {
                    if (_archiveStreamCache.ContainsKey(fullPath)) continue;

                    // LRU逐出
                    while (_archiveStreamCacheBytes + bytes.Length > MaxArchiveStreamCacheBytes
                           && _archiveStreamCacheOrder.Last != null)
                    {
                        var oldest = _archiveStreamCacheOrder.Last.Value;
                        _archiveStreamCacheOrder.RemoveLast();
                        if (_archiveStreamCache.Remove(oldest, out var oldBytes))
                            _archiveStreamCacheBytes -= oldBytes.Length;
                    }

                    _archiveStreamCache[fullPath] = bytes;
                    _archiveStreamCacheOrder.AddFirst(fullPath);
                    _archiveStreamCacheBytes += bytes.Length;
                }
            }
        }

        /// <summary>画像が縦向き（ポートレート）かどうか</summary>
        /// <remarks>寸法不明の場合は縦向きと仮定（taureader互換）</remarks>
        private bool IsPortrait(FileItem file)
        {
            var (w, h) = _imageCache.GetOriginalSize(file.FullPath);
            if (w > 0 && h > 0) return w <= h;
            // 寸法不明 → 縦向きと仮定して見開きを優先
            // （単独表示のちらつきを避けるため）
            return true;
        }

        /// <summary>
        /// taureader互換の見開き判定:
        /// ・最初のページは単独表示（表紙）
        /// ・横向き画像は単独
        /// ・次のファイルが縦向き画像なら見開き
        /// ・次が横向き/動画/音声/なしなら単独
        /// </summary>
        private bool ShouldShowSpread(int index)
        {
            if (_viewableFiles.Count <= 1) return false;

            var file = _viewableFiles[index];

            if (file.IsMedia) return false;

            // 最初のページは単独（表紙）— 設定で制御
            if (index == 0 && _appSettings.AutoSpreadCover) return false;

            // 現在のファイルの寸法を確認
            var (w1, h1) = _imageCache.GetOriginalSize(file.FullPath);
            bool curPortrait = (w1 <= 0 || h1 <= 0) ? true : ((float)w1 / h1 <= _appSettings.SpreadThreshold);

            if (!curPortrait) return false; // 横向きは単独

            // 次のファイルをチェック
            int nextIdx = index + 1;
            if (nextIdx >= _viewableFiles.Count) return false;

            var nextFile = _viewableFiles[nextIdx];

            if (nextFile.IsMedia) return false;

            var (w2, h2) = _imageCache.GetOriginalSize(nextFile.FullPath);
            bool nextPortrait = (w2 <= 0 || h2 <= 0) ? true : ((float)w2 / h2 <= _appSettings.SpreadThreshold);

            if (!nextPortrait) return false; // 次が横向きなら単独

            return true;
        }

        /// <summary>現在の表示ページ数を返す（見開きなら2、単独なら1）</summary>
        private int GetPagesPerView()
        {
            if (_currentFileIndex < 0 || _currentFileIndex >= _viewableFiles.Count) return 1;
            if (_viewMode == 1) return 1; // 単頁モード
            if (_viewMode == 2) return (_currentFileIndex + 1 < _viewableFiles.Count) ? 2 : 1;
            if (_viewMode == 0) return ShouldShowSpread(_currentFileIndex) ? 2 : 1;
            return 1;
        }

        private string? _tempMediaFile;

        private void ShowMedia(FileItem file)
        {
            _imageViewer.Clear();
            _imageViewer.Visible = false;
            _mediaPlayer.Visible = true;

            // 前回の一時ファイルを削除
            CleanupTempMedia();

            var split = SplitArchivePath(file.FullPath);
            if (split != null)
            {
                // 書庫内の動画: バックグラウンドで一時ファイルに展開してから再生
                _statusLeft.Text = "動画を展開中...";
                var fileCopy = file;
                Task.Run(() =>
                {
                    try
                    {
                        var stream = GetFileStream(fileCopy);
                        if (stream == null) { BeginInvoke(() => _statusLeft.Text = "展開に失敗しました"); return; }

                        var tempDir = Path.Combine(Path.GetTempPath(), "leeyez_media");
                        Directory.CreateDirectory(tempDir);
                        var tempFile = Path.Combine(tempDir, Guid.NewGuid().ToString("N") + fileCopy.Ext);

                        using (var fs = new FileStream(tempFile, FileMode.Create))
                            stream.CopyTo(fs);
                        stream.Dispose();

                        BeginInvoke(() =>
                        {
                            _tempMediaFile = tempFile;
                            _mediaPlayer.Play(tempFile);
                            _statusLeft.Text = fileCopy.FullPath;
                        });
                    }
                    catch (Exception ex)
                    {
                        BeginInvoke(() => _statusLeft.Text = $"展開失敗: {ex.Message}");
                    }
                });
                return;
            }
            else
            {
                _mediaPlayer.Play(file.FullPath);
                _statusLeft.Text = file.FullPath;
            }
        }

        private void CleanupTempMedia()
        {
            if (_tempMediaFile != null)
            {
                try { File.Delete(_tempMediaFile); } catch { }
                _tempMediaFile = null;
            }
        }

        private (int w, int h) GetMaxDecodeSize()
        {
            // 表示サイズの2倍でデコード（ズーム200%まで劣化なし、メモリ節約）
            int w = _imageViewer.Width > 0 ? _imageViewer.Width * 2 : AppConstants.MaxDecodeWidth;
            int h = _imageViewer.Height > 0 ? _imageViewer.Height * 2 : AppConstants.MaxDecodeHeight;
            return (Math.Min(w, AppConstants.MaxDecodeWidth), Math.Min(h, AppConstants.MaxDecodeHeight));
        }

        // ── ズーム ──
        private static readonly Color HighlightBg = Color.FromArgb(0xD0, 0xD0, 0xD0);
        private static readonly Color NormalBg = Color.Transparent;

        private void SetScaleMode(ImageViewer.ScaleMode mode)
        {
            _imageViewer.CurrentScaleMode = mode;
            UpdateZoomLabel();
            UpdateScaleModeHighlight(mode);
        }

        private void UpdateScaleModeHighlight(ImageViewer.ScaleMode mode)
        {
            _btnFitWindow.BackColor = mode == ImageViewer.ScaleMode.FitWindow ? HighlightBg : NormalBg;
            _btnFitWidth.BackColor = mode == ImageViewer.ScaleMode.FitWidth ? HighlightBg : NormalBg;
            _btnFitHeight.BackColor = mode == ImageViewer.ScaleMode.FitHeight ? HighlightBg : NormalBg;
            _btnOriginal.BackColor = mode == ImageViewer.ScaleMode.Original ? HighlightBg : NormalBg;
        }

        private void SetViewMode(int mode)
        {
            _viewMode = mode;
            UpdateViewModeHighlight();
            ShowCurrentFile();
        }

        private void UpdateViewModeHighlight()
        {
            _btnAutoView.BackColor = _viewMode == 0 ? HighlightBg : NormalBg;
            _btnSingleView.BackColor = _viewMode == 1 ? HighlightBg : NormalBg;
            _btnSpreadView.BackColor = _viewMode == 2 ? HighlightBg : NormalBg;
        }

        private void UpdateZoomLabel()
        {
            _zoomLabel.Text = $"{(int)Math.Round(_imageViewer.Zoom * 100)}%";
        }

        private void ZoomStep(int stepPercent)
        {
            int cur = (int)Math.Round(_imageViewer.Zoom * 100);
            int next = stepPercent > 0
                ? ((cur / stepPercent) + 1) * stepPercent
                : ((cur + stepPercent - 1) / (-stepPercent)) * (-stepPercent);
            next = Math.Clamp(next, AppConstants.ZoomMin, AppConstants.ZoomMax);
            _imageViewer.Zoom = next / 100f;
            UpdateZoomLabel();
        }

        private void UpdatePageLabel()
        {
            _pageLabel.Text = _viewableFiles.Count > 0
                ? $"{_currentFileIndex + 1} / {_viewableFiles.Count}"
                : "0 / 0";
        }

        // ── ホバープレビュー ──
        private void SetupHoverPreview()
        {
            _hoverPreview = new HoverPreviewForm();
            _hoverPreviewEnabled = false; // デフォルト無効
            _hoverTimer = new System.Windows.Forms.Timer { Interval = 200 };
            _hoverTimer.Tick += HoverTimer_Tick;

            _fileList.MouseMove += FileList_MouseMovePreview;
            _fileList.MouseLeave += (s, e) => HideHoverPreview();
            // ツリーでもホバープレビュー
            _folderTree.MouseMove += FolderTree_MouseMovePreview;
            _folderTree.MouseLeave += (s, e) => HideHoverPreview();

            _btnHoverPreview.Checked = false;
            _btnHoverPreview.Click += (s, e) =>
            {
                _hoverPreviewEnabled = !_hoverPreviewEnabled;
                _btnHoverPreview.Checked = _hoverPreviewEnabled;
                if (!_hoverPreviewEnabled) HideHoverPreview();
            };
        }

        private void FileList_MouseMovePreview(object? sender, MouseEventArgs e)
        {
            if (!_hoverPreviewEnabled) return;
            var hit = _fileList.HitTest(e.Location);
            if (hit.Item?.Tag is FileItem fi)
            {
                if (fi.IsDirectory) { HideHoverPreview(); return; }
                if (fi.IsImage || fi.IsArchiveExt)
                {
                    if (_hoverItem?.FullPath != fi.FullPath)
                    {
                        HideHoverPreview();
                        _hoverItem = fi;
                        _hoverTimer?.Stop();
                        _hoverTimer?.Start();
                    }
                    return;
                }
            }
            HideHoverPreview();
        }

        private void FolderTree_MouseMovePreview(object? sender, MouseEventArgs e)
        {
            if (!_hoverPreviewEnabled) return;
            var node = _folderTree.GetNodeAt(e.Location);
            if (node?.Tag is string path && !string.IsNullOrEmpty(path)
                && path != "FAVORITES" && path != "PC" && path != "DUMMY")
            {
                var ext = FileExtensions.GetExt(path);
                if (FileExtensions.IsArchive(ext))
                {
                    if (_hoverItem?.FullPath != path)
                    {
                        HideHoverPreview();
                        _hoverItem = new FileItem { FullPath = path, Name = System.IO.Path.GetFileName(path) };
                        _hoverTimer?.Stop();
                        _hoverTimer?.Start();
                    }
                    return;
                }
            }
            HideHoverPreview();
        }

        private void HoverTimer_Tick(object? sender, EventArgs e)
        {
            _hoverTimer?.Stop();
            if (_hoverItem == null || !_hoverPreviewEnabled) return;

            var item = _hoverItem;

            // まずキャッシュを確認
            var cached = _imageCache.Get(item.FullPath);
            if (cached != null)
            {
                _hoverPreview?.ShowPreview(cached, Cursor.Position);
                return;
            }

            // 書庫の場合は最初の画像をプレビュー
            if (item.IsArchiveExt)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var entries = ArchiveService.GetEntries(item.FullPath, _sevenZipLibPath);
                        var firstImage = entries.FirstOrDefault(en =>
                            !en.IsFolder && FileExtensions.IsImage(FileExtensions.GetExt(en.FileName)));
                        if (firstImage == null) return;

                        using var stream = ArchiveService.GetEntryStream(item.FullPath, firstImage.FileName, _sevenZipLibPath);
                        if (stream == null) return;
                        var imgExt = FileExtensions.GetExt(firstImage.FileName);
                        var bmp = ImageViewer.FastDecode(stream, imgExt, 320, 320, out _, out _);
                        if (bmp == null) return;

                        _fileList.BeginInvoke(() =>
                        {
                            if (_hoverItem?.FullPath != item.FullPath) { bmp.Dispose(); return; }
                            _hoverPreview?.ShowPreview(bmp, Cursor.Position);
                        });
                    }
                    catch (Exception ex) { Logger.Log($"Failed to load hover preview for archive: {ex.Message}"); }
                });
                return;
            }

            // 通常の画像ファイル
            Task.Run(() =>
            {
                try
                {
                    using var stream = GetFileStream(item);
                    if (stream == null) return;
                    var bmp = ImageViewer.FastDecode(stream, item.Ext, 320, 320, out _, out _);
                    if (bmp == null) return;

                    _fileList.BeginInvoke(() =>
                    {
                        if (_hoverItem?.FullPath != item.FullPath) { bmp.Dispose(); return; }
                        _hoverPreview?.ShowPreview(bmp, Cursor.Position);
                    });
                }
                catch (Exception ex) { Logger.Log($"Failed to load hover preview: {ex.Message}"); }
            });
        }

        private void HideHoverPreview()
        {
            _hoverTimer?.Stop();
            _hoverItem = null;
            _hoverPreview?.HidePreview();
        }
    }
}
