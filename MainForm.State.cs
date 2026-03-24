using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using leeyez_kai.Models;
using leeyez_kai.Services;

namespace leeyez_kai
{
    public partial class MainForm
    {
        private System.Windows.Forms.Timer? _autoSaveTimer;
        private AppState? _savedState;

        private void AutoSaveState()
        {
            if (_autoSaveTimer == null)
            {
                _autoSaveTimer = new System.Windows.Forms.Timer { Interval = AppConstants.AutoSaveMs };
                _autoSaveTimer.Tick += (s, e) => { _autoSaveTimer.Stop(); SaveCurrentState(); };
            }
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        private void SaveCurrentState()
        {
            try
            {
                string lastViewingFile = "";
                int lastFileIndex = -1;
                if (_currentFileIndex >= 0 && _currentFileIndex < _viewableFiles.Count)
                {
                    lastViewingFile = _viewableFiles[_currentFileIndex].FullPath;
                    lastFileIndex = _currentFileIndex;
                }

                var bounds = WindowState == FormWindowState.Normal
                    ? new Rectangle(Left, Top, Width, Height)
                    : RestoreBounds;

                // 既存の状態を読み込んでお気に入りを保持
                var existing = PersistenceService.LoadState() ?? new AppState();
                existing.LastPath = _nav.CurrentPath;
                existing.LastViewingFile = lastViewingFile;
                existing.LastFileIndex = lastFileIndex;
                existing.WindowWidth = bounds.Width;
                existing.WindowHeight = bounds.Height;
                existing.WindowTop = bounds.Top;
                existing.WindowLeft = bounds.Left;
                existing.WindowState = WindowState == FormWindowState.Maximized ? 2 : 0;
                existing.SidebarWidth = _mainSplit.SplitterDistance;
                PersistenceService.SaveState(existing);
            }
            catch { }
        }

        private void RestoreWindowState()
        {
            _savedState = PersistenceService.LoadState();
            if (_savedState == null) return;

            if (_savedState.WindowWidth.HasValue && _savedState.WindowHeight.HasValue)
                Size = new Size(_savedState.WindowWidth.Value, _savedState.WindowHeight.Value);

            if (_savedState.WindowTop.HasValue && _savedState.WindowLeft.HasValue)
            {
                var pt = new Point(_savedState.WindowLeft.Value, _savedState.WindowTop.Value);
                bool onScreen = Screen.AllScreens.Any(s =>
                    s.WorkingArea.IntersectsWith(new Rectangle(pt, Size)));
                if (onScreen && pt.X > -10000 && pt.Y > -10000)
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = pt;
                }
            }
            if (_savedState.WindowState == 2) WindowState = FormWindowState.Maximized;
        }

        private void LoadState()
        {
            var state = _savedState ?? PersistenceService.LoadState();
            var favorites = state?.Favorites ?? new List<string>();

            _treeManager?.Initialize(favorites);
            if (state == null) return;

            if (state.SidebarWidth > 0)
                _mainSplit.SplitterDistance = state.SidebarWidth;

            if (!string.IsNullOrEmpty(state.LastPath))
            {
                var lastPath = state.LastPath;
                var lastViewingFile = state.LastViewingFile;
                var lastFileIndex = state.LastFileIndex;

                BeginInvoke(() =>
                {
                    try
                    {
                        var ext = FileExtensions.GetExt(lastPath);
                        if (FileExtensions.IsArchive(ext) && File.Exists(lastPath))
                            OpenArchiveAndShowFirstImage(lastPath);
                        else
                            NavigateTo(lastPath);

                        // 前回のファイルを復元
                        if (!string.IsNullOrEmpty(lastViewingFile) && _viewableFiles.Count > 0)
                        {
                            int idx = _viewableFiles.FindIndex(f => f.FullPath == lastViewingFile);
                            if (idx < 0 && lastFileIndex >= 0 && lastFileIndex < _viewableFiles.Count)
                                idx = lastFileIndex;
                            if (idx >= 0)
                            {
                                _currentFileIndex = idx;
                                UpdatePageLabel();
                                ShowCurrentFile();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"LoadState restore failed: {ex.Message}");
                    }

                    // ツリーのオートリビール（ファイルリスト構築完了後に実行）
                    Task.Run(() => Thread.Sleep(500))
                        .ContinueWith(_ =>
                        {
                            try { _treeManager?.SelectPath(lastPath); }
                            catch (Exception ex) { Logger.Log($"SelectPath failed: {ex.Message}"); }
                        }, TaskScheduler.FromCurrentSynchronizationContext());
                });
            }
        }
    }
}
