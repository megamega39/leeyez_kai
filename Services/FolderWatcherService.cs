using System;
using System.IO;

namespace leeyez_kai.Services
{
    public class FolderWatcherService : IDisposable
    {
        private FileSystemWatcher? _watcher;
        private string? _watchedPath;
        private readonly Action<string> _onChanged;
        private readonly System.Windows.Forms.Timer _debounceTimer;

        public FolderWatcherService(Action<string> onChanged)
        {
            _onChanged = onChanged;
            _debounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                if (_watchedPath != null) _onChanged(_watchedPath);
            };
        }

        public void Watch(string? folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || folderPath == _watchedPath) return;
            if (folderPath.Contains("::")) return;
            if (!Directory.Exists(folderPath)) return;

            _watcher?.Dispose();
            _watchedPath = folderPath;
            try
            {
                _watcher = new FileSystemWatcher(folderPath)
                {
                    NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _watcher.Created += OnFsEvent;
                _watcher.Deleted += OnFsEvent;
                _watcher.Renamed += OnFsEvent;
            }
            catch { _watcher = null; _watchedPath = null; }
        }

        private void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            // WinFormsではInvokeを使ってUIスレッドに戻す
            try
            {
                if (Application.OpenForms.Count > 0)
                {
                    var form = Application.OpenForms[0];
                    if (form != null && !form.IsDisposed)
                    {
                        form.BeginInvoke(() =>
                        {
                            _debounceTimer.Stop();
                            _debounceTimer.Start();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"FolderWatcher event dispatch failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _debounceTimer.Stop();
            _watcher?.Dispose();
        }
    }
}
