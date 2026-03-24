using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using leeyez_kai.Models;

namespace leeyez_kai.Services
{
    public class BookshelfService
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "leeyez", "bookshelf.json");

        private BookshelfData _data = new();

        public BookshelfData Data => _data;
        public List<BookshelfCategory> Categories => _data.Categories;
        public List<BookshelfItem> UncategorizedItems => _data.UncategorizedItems;

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    _data = JsonSerializer.Deserialize<BookshelfData>(json) ?? new BookshelfData();
                }
            }
            catch { _data = new BookshelfData(); }
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (dir != null && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }

        public BookshelfCategory AddCategory(string name)
        {
            var cat = new BookshelfCategory { Name = name };
            _data.Categories.Add(cat);
            Save();
            return cat;
        }

        public void RemoveCategory(string id)
        {
            _data.Categories.RemoveAll(c => c.Id == id);
            Save();
        }

        public void RenameCategory(string id, string newName)
        {
            var cat = _data.Categories.FirstOrDefault(c => c.Id == id);
            if (cat != null) { cat.Name = newName; Save(); }
        }

        /// <summary>カテゴリにアイテムを追加（重複チェック付き）</summary>
        public void AddItem(string? categoryId, string name, string path)
        {
            var item = new BookshelfItem { Name = name, Path = path };

            if (categoryId == null)
            {
                if (!_data.UncategorizedItems.Any(i => i.Path == path))
                    _data.UncategorizedItems.Add(item);
            }
            else
            {
                var cat = _data.Categories.FirstOrDefault(c => c.Id == categoryId);
                if (cat != null && !cat.Items.Any(i => i.Path == path))
                    cat.Items.Add(item);
            }
            Save();
        }

        /// <summary>アイテムを全カテゴリから削除</summary>
        public void RemoveItem(string path)
        {
            _data.UncategorizedItems.RemoveAll(i => i.Path == path);
            foreach (var cat in _data.Categories)
                cat.Items.RemoveAll(i => i.Path == path);
            Save();
        }

        /// <summary>指定パスが本棚に登録されているか</summary>
        public bool Contains(string path)
        {
            if (_data.UncategorizedItems.Any(i => i.Path == path)) return true;
            return _data.Categories.Any(c => c.Items.Any(i => i.Path == path));
        }
    }
}
