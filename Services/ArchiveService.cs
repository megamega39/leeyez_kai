using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SevenZipExtractor;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using leeyez_kai.Models;

namespace leeyez_kai.Services
{
    public static class ArchiveService
    {
        private static readonly Encoding[] FallbackEncodings = new[]
        {
            Encoding.GetEncoding("shift-jis"),
            Encoding.UTF8,
            Encoding.Default
        };

        // 書庫ハンドルLRUキャッシュ（最大3個保持）
        private static readonly LinkedList<(string path, ArchiveFile archive)> _archiveCache = new();
        private const int MaxArchiveHandles = 3;
        private static string? _cachedSevenZipLib;
        private static readonly object _archiveLock = new();

        /// <summary>書庫ハンドルを取得または新規オープン（呼び出し元で_archiveLockを取得すること）</summary>
        private static ArchiveFile? GetOrOpenArchiveUnsafe(string archivePath, string sevenZipLibPath)
        {
            // キャッシュヒット検索
            var node = _archiveCache.First;
            while (node != null)
            {
                if (node.Value.path == archivePath)
                {
                    // LRU: 先頭に移動
                    _archiveCache.Remove(node);
                    _archiveCache.AddFirst(node);
                    return node.Value.archive;
                }
                node = node.Next;
            }

            // キャッシュミス: 上限超過なら最古を閉じる
            while (_archiveCache.Count >= MaxArchiveHandles)
            {
                var oldest = _archiveCache.Last!.Value;
                try { oldest.archive.Dispose(); } catch { }
                _archiveCache.RemoveLast();
            }

            try
            {
                var archive = new ArchiveFile(archivePath, sevenZipLibPath);
                _cachedSevenZipLib = sevenZipLibPath;
                _archiveCache.AddFirst((archivePath, archive));
                return archive;
            }
            catch (Exception ex)
            {
                Logger.Log($"ArchiveFile open failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>書庫ハンドルキャッシュをクリア</summary>
        public static void CloseCache()
        {
            lock (_archiveLock)
            {
                foreach (var (_, archive) in _archiveCache)
                    try { archive.Dispose(); } catch (Exception ex) { Logger.Log($"Failed to dispose archive: {ex.Message}"); }
                _archiveCache.Clear();
            }
        }

        public static List<ArchiveEntryInfo> GetEntries(string archivePath, string sevenZipLibPath)
        {
            var entries = new List<ArchiveEntryInfo>();
            if (!File.Exists(archivePath)) return entries;

            bool szipSuccess = false;
            try
            {
                lock (_archiveLock)
                {
                    var archive = GetOrOpenArchiveUnsafe(archivePath, sevenZipLibPath);
                    if (archive != null)
                    {
                        foreach (var entry in archive.Entries)
                        {
                            entries.Add(new ArchiveEntryInfo
                            {
                                FileName = NormalizeEntryPath(entry.FileName),
                                Size = (long)entry.Size,
                                LastWriteTime = entry.LastWriteTime,
                                IsFolder = entry.IsFolder
                            });
                        }
                        szipSuccess = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"SevenZipExtractor failed: {ex.Message}");
                CloseCache();
            }

            if (!szipSuccess || !entries.Any())
            {
                foreach (var enc in FallbackEncodings)
                {
                    try
                    {
                        var options = new ReaderOptions { ArchiveEncoding = new ArchiveEncoding { Default = enc } };
                        using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var archive = ArchiveFactory.Open(stream, options);
                        entries.Clear();
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.Key == null) continue;
                            entries.Add(new ArchiveEntryInfo
                            {
                                FileName = NormalizeEntryPath(entry.Key),
                                Size = entry.Size,
                                LastWriteTime = entry.LastModifiedTime,
                                IsFolder = entry.IsDirectory
                            });
                        }
                        if (entries.Any()) break;
                    }
                    catch (Exception ex) { Logger.Log($"Failed to list archive entries: {ex.Message}"); }
                }
            }

            return entries;
        }

        public static Stream? GetEntryStream(string archivePath, string entryKey, string sevenZipLibPath)
        {
            string targetKey = NormalizeEntryPath(entryKey);

            try
            {
                lock (_archiveLock)
                {
                    var archive = GetOrOpenArchiveUnsafe(archivePath, sevenZipLibPath);
                    if (archive != null)
                    {
                        var entry = archive.Entries.FirstOrDefault(e => NormalizeEntryPath(e.FileName) == targetKey);
                        if (entry != null)
                        {
                            var ms = new MemoryStream();
                            entry.Extract(ms);
                            ms.Position = 0;
                            return ms;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"GetEntryStream failed: {ex.Message}");
                CloseCache();
            }

            // SharpCompress fallback
            foreach (var enc in FallbackEncodings)
            {
                try
                {
                    var options = new ReaderOptions { ArchiveEncoding = new ArchiveEncoding { Default = enc } };
                    using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var archive = ArchiveFactory.Open(stream, options);
                    var entry = archive.Entries.FirstOrDefault(e => e.Key != null && NormalizeEntryPath(e.Key) == targetKey);
                    if (entry != null)
                    {
                        var ms = new MemoryStream();
                        using (var entryStream = entry.OpenEntryStream())
                            entryStream.CopyTo(ms);
                        ms.Position = 0;
                        return ms;
                    }
                }
                catch (Exception ex) { Logger.Log($"Failed to extract archive entry stream: {ex.Message}"); }
            }

            return null;
        }

        /// <summary>書庫内の全画像を一括展開（書庫を1回だけ開く）</summary>
        public static Dictionary<string, byte[]> ExtractAll(string archivePath, string sevenZipLibPath, HashSet<string> targetEntries, CancellationToken ct = default)
        {
            var result = new Dictionary<string, byte[]>();
            if (!File.Exists(archivePath) || targetEntries.Count == 0) return result;

            try
            {
                // エントリ一覧の取得だけロック内で行い、展開はエントリごとに短いロックで実行
                // → 書庫切り替え時にロックが長時間保持されず、次の書庫を即座に開ける
                List<(string key, SevenZipExtractor.Entry entry)>? targets = null;
                lock (_archiveLock)
                {
                    var archive = GetOrOpenArchiveUnsafe(archivePath, sevenZipLibPath);
                    if (archive != null)
                    {
                        targets = new();
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.IsFolder) continue;
                            var key = NormalizeEntryPath(entry.FileName);
                            if (targetEntries.Contains(key))
                                targets.Add((key, entry));
                        }
                    }
                }

                if (targets != null)
                {
                    foreach (var (key, entry) in targets)
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            lock (_archiveLock)
                            {
                                var ms = new MemoryStream();
                                entry.Extract(ms);
                                result[key] = ms.ToArray();
                                ms.Dispose();
                            }
                        }
                        catch (Exception ex) { Logger.Log($"Failed to extract archive entry (7zip): {ex.Message}"); }
                    }
                    Logger.Log($"ExtractAll: {result.Count}/{targetEntries.Count} extracted from {Path.GetFileName(archivePath)}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ExtractAll SevenZip failed: {ex.Message}");
                CloseCache();
            }

            // SharpCompress fallback
            try
            {
                using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var archive = ArchiveFactory.Open(stream);
                foreach (var entry in archive.Entries)
                {
                    if (ct.IsCancellationRequested) break;
                    if (entry.Key == null || entry.IsDirectory) continue;
                    var key = NormalizeEntryPath(entry.Key);
                    if (!targetEntries.Contains(key)) continue;

                    try
                    {
                        var ms = new MemoryStream();
                        using (var es = entry.OpenEntryStream())
                            es.CopyTo(ms);
                        result[key] = ms.ToArray();
                        ms.Dispose();
                    }
                    catch (Exception ex) { Logger.Log($"Failed to extract archive entry (SharpCompress): {ex.Message}"); }
                }
            }
            catch (Exception ex) { Logger.Log($"Failed to open archive for bulk extraction: {ex.Message}"); }

            return result;
        }

        public static string NormalizeEntryPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            string normalized = path.Replace('\\', '/').Trim('/');
            while (normalized.StartsWith("./", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(2).TrimStart('/');
            return normalized;
        }
    }
}
