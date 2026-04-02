using System.Collections.Generic;
using System.IO;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm
    {
        /// <summary>書庫エントリをキャッシュ付きで取得（LRU上限あり）</summary>
        private List<ArchiveEntryInfo> GetArchiveEntries(string archivePath)
        {
            if (_archiveEntryCache.TryGetValue(archivePath, out var cached))
            {
                _archiveEntryCacheOrder.Remove(archivePath);
                _archiveEntryCacheOrder.Add(archivePath);
                return cached;
            }

            var entries = ArchiveService.GetEntries(archivePath, _sevenZipLibPath);

            while (_archiveEntryCacheOrder.Count >= MaxArchiveEntryCacheSize)
            {
                var oldest = _archiveEntryCacheOrder[0];
                _archiveEntryCacheOrder.RemoveAt(0);
                _archiveEntryCache.Remove(oldest);
            }

            _archiveEntryCache[archivePath] = entries;
            _archiveEntryCacheOrder.Add(archivePath);
            return entries;
        }

        private void LoadArchive(string archivePath, string innerPath)
        {
            if (_archiveEntries == null || _currentArchivePath != archivePath)
            {
                _prefetchCts?.Cancel();
                _prefetchCts?.Dispose();
                _prefetchCts = null;
                ClearStreamCache();
                _imageCache.Clear();

                _archiveEntries = GetArchiveEntries(archivePath);
            }

            _currentArchivePath = archivePath;
            _fileListManager?.LoadArchiveEntries(_archiveEntries, archivePath, innerPath);
            UpdateViewableFiles();
        }

        // PreExtractArchive廃止 — taureader方式でオンデマンド展開
        // 書庫ハンドル再利用（ArchiveService.GetOrOpenArchive）で個別展開も十分速い

        /// <summary>NavigateToから呼ばれる: 書庫を開いてファイルリスト＋最初の画像を表示</summary>
        private void OpenArchiveInline(string archivePath)
        {
            LoadArchive(archivePath, "");
            Logger.Log($"OpenArchiveInline: {_viewableFiles.Count} viewable files");
            ShowFirstArchiveImage();
        }

        /// <summary>書庫を開き最初の画像を表示（状態復元・ファイルリストから呼ばれる）</summary>
        private void OpenArchiveAndShowFirstImage(string archivePath)
        {
            _isNavigating = true;
            try
            {
                _nav.NavigateTo(archivePath);
                _addressBox.Text = archivePath;
                UpdateBreadcrumb(archivePath);
                UpdateNavButtons();
                AutoSaveState();
                LoadArchive(archivePath, "");
                if (!_skipSelectPath) _treeManager?.SelectPath(archivePath);
                ShowFirstArchiveImage(autoSave: true);
            }
            finally { _isNavigating = false; }
        }

        /// <summary>書庫の最初の画像を表示する共通処理</summary>
        private void ShowFirstArchiveImage(bool autoSave = false)
        {
            if (_viewableFiles.Count > 0)
            {
                _currentFileIndex = 0;
                UpdatePageLabel();
                if (autoSave) AutoSaveState();

                // 最初の画像を同期でキャッシュに登録（非同期だと真っ黒になる）
                var firstFile = _viewableFiles[0];
                LoadAndCacheImage(firstFile);
                // 表示はShowCurrentFileに統一（見開き判定等を含む）
                ShowCurrentFile();
            }
            else
            {
                _currentFileIndex = -1;
                UpdatePageLabel();
                _imageViewer.Clear();
                _mediaPlayer.Stop();
                _mediaPlayer.Visible = false;
                _imageViewer.Visible = true;
            }
        }

        private Stream? GetFileStream(FileItem file)
        {
            lock (_streamCacheLock)
            {
                if (_archiveStreamCache.TryGetValue(file.FullPath, out var cachedBytes))
                {
                    // LRU更新: 先頭に移動
                    _archiveStreamCacheOrder.Remove(file.FullPath);
                    _archiveStreamCacheOrder.AddFirst(file.FullPath);
                    return new MemoryStream(cachedBytes);
                }
            }

            var split = SplitArchivePath(file.FullPath);
            if (split != null)
            {
                var stream = ArchiveService.GetEntryStream(split.Value.archive, split.Value.entry, _sevenZipLibPath);
                TryCacheStream(file.FullPath, stream);
                return stream;
            }

            return new FileStream(file.FullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, bufferSize: AppConstants.FileStreamBuffer);
        }

        private void TryCacheStream(string key, Stream? stream)
        {
            if (stream is not MemoryStream ms || ms.Length >= 2 * 1024 * 1024) return;
            var bytes = ms.ToArray();

            lock (_streamCacheLock)
            {
                // 容量超過時は最古エントリを1個ずつ削除
                while (_archiveStreamCacheBytes + bytes.Length > MaxArchiveStreamCacheBytes
                       && _archiveStreamCacheOrder.Last != null)
                {
                    var oldest = _archiveStreamCacheOrder.Last.Value;
                    _archiveStreamCacheOrder.RemoveLast();
                    if (_archiveStreamCache.Remove(oldest, out var oldBytes))
                        _archiveStreamCacheBytes -= oldBytes.Length;
                }

                _archiveStreamCache[key] = bytes;
                _archiveStreamCacheOrder.AddFirst(key);
                _archiveStreamCacheBytes += bytes.Length;
            }

            ms.Position = 0;
        }

        private void ClearStreamCache()
        {
            lock (_streamCacheLock)
            {
                _archiveStreamCache.Clear();
                _archiveStreamCacheOrder.Clear();
                _archiveStreamCacheBytes = 0;
            }
        }

        /// <summary>
        /// パスを書庫パスとエントリパスに分割。
        /// 書庫ファイル名自体に!が含まれる場合を考慮し、後ろから!を探す。
        /// </summary>
        internal static (string archive, string entry)? SplitArchivePath(string path)
        {
            if (!path.Contains('!')) return null;

            // 後ろから!を探して、その前が実在する書庫ファイルかチェック
            int idx = path.Length;
            while ((idx = path.LastIndexOf('!', idx - 1)) >= 0)
            {
                var possibleArchive = path.Substring(0, idx);
                if (FileExtensions.IsArchive(FileExtensions.GetExt(possibleArchive)) && File.Exists(possibleArchive))
                    return (possibleArchive, path.Substring(idx + 1));
                if (idx == 0) break;
            }

            return null;
        }
    }
}
