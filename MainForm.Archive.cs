using System.Collections.Generic;
using System.IO;
using System.Linq;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm
    {
        /// <summary>書庫エントリをキャッシュ付きで取得</summary>
        private List<ArchiveEntryInfo> GetArchiveEntries(string archivePath)
        {
            if (_archiveEntryCache.TryGetValue(archivePath, out var cached))
                return cached;

            var entries = ArchiveService.GetEntries(archivePath, _sevenZipLibPath);
            _archiveEntryCache[archivePath] = entries;
            return entries;
        }

        /// <summary>単一フォルダ書庫のプレフィックスを検出（表示名用）</summary>
        private static string? DetectSingleFolderPrefix(List<ArchiveEntryInfo> entries)
        {
            // ToList()を避けて直接列挙
            var first = entries.FirstOrDefault(e => !e.IsFolder && !string.IsNullOrEmpty(e.FileName));
            if (first == null) return null;

            var firstSlash = first.FileName.IndexOf('/');
            if (firstSlash <= 0) return null;

            var prefix = first.FileName.Substring(0, firstSlash + 1);
            if (entries.Where(e => !e.IsFolder && !string.IsNullOrEmpty(e.FileName))
                      .All(f => f.FileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return prefix;
            return null;
        }

        /// <summary>書庫内画像をフラットにviewableFilesに収集（単一フォルダ自動展開対応）</summary>
        private void BuildArchiveViewableFiles(string archivePath, List<ArchiveEntryInfo> entries)
        {
            _viewableFiles.Clear();
            var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            var prefix = DetectSingleFolderPrefix(entries);

            var imageEntries = entries
                .Where(e => !e.IsFolder && FileExtensions.IsViewable(FileExtensions.GetExt(e.FileName)))
                .OrderBy(e => e.FileName, System.StringComparer.OrdinalIgnoreCase);

            foreach (var entry in imageEntries)
            {
                var fullPath = archivePath + "!" + entry.FileName; // 元のエントリパスを保持
                if (!seen.Add(fullPath)) continue;

                // 表示名からプレフィックスを除去
                var displayName = entry.FileName;
                if (prefix != null && displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    displayName = displayName.Substring(prefix.Length);

                _viewableFiles.Add(new FileItem
                {
                    Name = Path.GetFileName(displayName),
                    FullPath = fullPath,
                    Size = entry.Size,
                    LastModified = entry.LastWriteTime ?? System.DateTime.MinValue,
                    IsDirectory = false,
                    DisplayType = Path.GetExtension(entry.FileName).TrimStart('.').ToUpperInvariant()
                });
            }
        }

        private void LoadArchive(string archivePath, string innerPath)
        {
            if (_archiveEntries == null || _currentArchivePath != archivePath)
            {
                _prefetchCts?.Cancel();
                _archiveStreamCache.Clear();
                _imageCache.Clear();

                // 大量のBitmap破棄後にメモリを即解放
                GC.Collect(2, GCCollectionMode.Optimized, false);

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
            var entries = GetArchiveEntries(archivePath);
            LoadArchive(archivePath, "");
            if (!_skipSelectPath) _treeManager?.SelectPath(archivePath);
            BuildArchiveViewableFiles(archivePath, entries);

            Logger.Log($"OpenArchiveInline: {_viewableFiles.Count} viewable files");

            if (_viewableFiles.Count > 0)
            {
                _currentFileIndex = 0;
                UpdatePageLabel();
                // 最初の画像は同期で即表示（非同期だと真っ黒になる）
                var firstFile = _viewableFiles[0];
                LoadAndCacheImage(firstFile);
                var bmp = _imageCache.Get(firstFile.FullPath);
                if (bmp != null)
                {
                    var (ow, oh) = _imageCache.GetOriginalSize(firstFile.FullPath);
                    _mediaPlayer.Stop();
                    _mediaPlayer.Visible = false;
                    _imageViewer.Visible = true;
                    _imageViewer.ShowBitmap(bmp, ow, oh);
                    _statusLeft.Text = firstFile.FullPath;
                    StartPrefetch();
                }
                else
                {
                    ShowCurrentFile();
                }
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

            // オンデマンド展開（プリフェッチで先読み）
        }

        /// <summary>書庫を開き最初の画像を表示（状態復元・ファイルリストから呼ばれる）</summary>
        private void OpenArchiveAndShowFirstImage(string archivePath)
        {
            var entries = GetArchiveEntries(archivePath);
            NavigateTo(archivePath);
            BuildArchiveViewableFiles(archivePath, entries);

            if (_viewableFiles.Count > 0)
            {
                _currentFileIndex = 0;
                UpdatePageLabel();
                AutoSaveState();

                // 最初の画像は同期で即表示
                var firstFile = _viewableFiles[0];
                LoadAndCacheImage(firstFile);
                var bmp = _imageCache.Get(firstFile.FullPath);
                if (bmp != null)
                {
                    var (ow, oh) = _imageCache.GetOriginalSize(firstFile.FullPath);
                    _mediaPlayer.Stop();
                    _mediaPlayer.Visible = false;
                    _imageViewer.Visible = true;
                    _imageViewer.ShowBitmap(bmp, ow, oh);
                    _statusLeft.Text = firstFile.FullPath;
                    StartPrefetch();
                }
                else
                {
                    ShowCurrentFile();
                }
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

            // オンデマンド展開（プリフェッチで先読み）
        }

        private Stream? GetFileStream(FileItem file)
        {
            // 書庫内ファイルのStreamキャッシュ確認
            if (_archiveStreamCache.TryGetValue(file.FullPath, out var cachedBytes))
                return new MemoryStream(cachedBytes);

            // 高速パス: 現在の書庫パスが分かっていれば直接分割
            if (_currentArchivePath != null && file.FullPath.StartsWith(_currentArchivePath + "!", StringComparison.OrdinalIgnoreCase))
            {
                var entry = file.FullPath.Substring(_currentArchivePath.Length + 1);
                var stream = ArchiveService.GetEntryStream(_currentArchivePath, entry, _sevenZipLibPath);
                // 2MB以下のみStreamキャッシュ、上限100件
                if (stream is MemoryStream ms && ms.Length < 2 * 1024 * 1024)
                {
                    if (_archiveStreamCache.Count >= 100)
                    {
                        var oldest = _archiveStreamCache.Keys.First();
                        _archiveStreamCache.Remove(oldest);
                    }
                    _archiveStreamCache[file.FullPath] = ms.ToArray();
                    ms.Position = 0;
                }
                return stream;
            }

            // フォールバック
            var split = SplitArchivePath(file.FullPath);
            if (split != null)
            {
                var stream = ArchiveService.GetEntryStream(split.Value.archive, split.Value.entry, _sevenZipLibPath);
                if (stream is MemoryStream ms && ms.Length < 3 * 1024 * 1024)
                {
                    _archiveStreamCache[file.FullPath] = ms.ToArray();
                    ms.Position = 0;
                }
                return stream;
            }

            return new FileStream(file.FullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, bufferSize: AppConstants.FileStreamBuffer);
        }

        /// <summary>
        /// パスを書庫パスとエントリパスに分割。
        /// 書庫ファイル名自体に!が含まれる場合を考慮し、後ろから!を探す。
        /// </summary>
        // SplitArchivePathのキャッシュ（同じパスパターンの繰り返し呼び出しを高速化）
        private static string? _lastSplitPath;
        private static (string archive, string entry)? _lastSplitResult;

        internal static (string archive, string entry)? SplitArchivePath(string path)
        {
            if (!path.Contains('!')) return null;

            // キャッシュヒット
            if (_lastSplitPath == path) return _lastSplitResult;

            // 後ろから!を探して、その前が実在する書庫ファイルかチェック
            int idx = path.Length;
            while ((idx = path.LastIndexOf('!', idx - 1)) >= 0)
            {
                var possibleArchive = path.Substring(0, idx);
                if (FileExtensions.IsArchive(FileExtensions.GetExt(possibleArchive)) && File.Exists(possibleArchive))
                {
                    var result = (possibleArchive, path.Substring(idx + 1));
                    _lastSplitPath = path;
                    _lastSplitResult = result;
                    return result;
                }
                if (idx == 0) break;
            }

            _lastSplitPath = path;
            _lastSplitResult = null;
            return null;
        }
    }
}
