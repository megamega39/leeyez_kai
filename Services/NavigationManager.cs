using System;
using System.Collections.Generic;
using System.Linq;

namespace leeyez_kai.Services
{
    public class NavigationManager
    {
        private readonly List<string> _history = new();
        private int _historyIndex = -1;
        private const int MaxHistory = 500;

        public string CurrentPath => _historyIndex >= 0 && _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
        public bool CanGoBack => _historyIndex > 0;
        public bool CanGoForward => _historyIndex < _history.Count - 1;

        /// <summary>戻る履歴（現在位置より前、新しい順）</summary>
        public List<string> BackHistory
        {
            get
            {
                if (_historyIndex <= 0) return new();
                var list = new List<string>(_historyIndex);
                for (int i = _historyIndex - 1; i >= 0; i--)
                    list.Add(_history[i]);
                return list;
            }
        }

        /// <summary>進む履歴（現在位置より後）</summary>
        public List<string> ForwardHistory
        {
            get
            {
                if (_historyIndex >= _history.Count - 1) return new();
                return _history.GetRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            }
        }

        public void NavigateTo(string path)
        {
            if (path == CurrentPath) return;

            // 現在位置より先の履歴を削除
            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);

            _history.Add(path);
            _historyIndex = _history.Count - 1;

            // 上限制限
            if (_history.Count > MaxHistory)
            {
                int remove = _history.Count - MaxHistory;
                _history.RemoveRange(0, remove);
                _historyIndex -= remove;
            }
        }

        public string? GoBack()
        {
            if (!CanGoBack) return null;
            _historyIndex--;
            return CurrentPath;
        }

        public string? GoForward()
        {
            if (!CanGoForward) return null;
            _historyIndex++;
            return CurrentPath;
        }

        /// <summary>履歴の特定位置にジャンプ</summary>
        public string? GoToHistoryIndex(int absoluteIndex)
        {
            if (absoluteIndex < 0 || absoluteIndex >= _history.Count) return null;
            _historyIndex = absoluteIndex;
            return CurrentPath;
        }

        public string? GoUp()
        {
            if (string.IsNullOrEmpty(CurrentPath)) return null;
            var parentDir = System.IO.Path.GetDirectoryName(CurrentPath);
            if (parentDir != null)
            {
                NavigateTo(parentDir);
                return parentDir;
            }
            return null;
        }
    }
}
