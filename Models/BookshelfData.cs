using System;
using System.Collections.Generic;

namespace leeyez_kai.Models
{
    public class BookshelfItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class BookshelfCategory
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public List<BookshelfItem> Items { get; set; } = new();
    }

    public class BookshelfData
    {
        public List<BookshelfCategory> Categories { get; set; } = new();
        public List<BookshelfItem> UncategorizedItems { get; set; } = new();
    }
}
