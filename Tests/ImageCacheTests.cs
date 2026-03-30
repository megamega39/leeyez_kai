using SkiaSharp;
using leeyez_kai.Services;

namespace leeyez_kai.Tests;

public class ImageCacheTests
{
    private static SKBitmap CreateTestBitmap(int w = 10, int h = 10)
        => new SKBitmap(w, h);

    [Fact]
    public void Put_And_Get()
    {
        using var cache = new ImageCache(5);
        var bmp = CreateTestBitmap();
        cache.Put("key1", bmp, 100, 200);

        var result = cache.Get("key1");
        Assert.Same(bmp, result);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        using var cache = new ImageCache(5);
        Assert.Null(cache.Get("nonexistent"));
    }

    [Fact]
    public void Contains_Works()
    {
        using var cache = new ImageCache(5);
        var bmp = CreateTestBitmap();
        cache.Put("key1", bmp, 100, 200);

        Assert.True(cache.Contains("key1"));
        Assert.False(cache.Contains("key2"));
    }

    [Fact]
    public void GetOriginalSize_Works()
    {
        using var cache = new ImageCache(5);
        var bmp = CreateTestBitmap();
        cache.Put("key1", bmp, 1920, 1080);

        var (w, h) = cache.GetOriginalSize("key1");
        Assert.Equal(1920, w);
        Assert.Equal(1080, h);
    }

    [Fact]
    public void Eviction_WhenFull()
    {
        using var cache = new ImageCache(3);
        cache.Put("a", CreateTestBitmap(), 10, 10);
        cache.Put("b", CreateTestBitmap(), 10, 10);
        cache.Put("c", CreateTestBitmap(), 10, 10);
        cache.Put("d", CreateTestBitmap(), 10, 10); // should evict "a"

        Assert.False(cache.Contains("a")); // evicted
        Assert.True(cache.Contains("b"));
        Assert.True(cache.Contains("c"));
        Assert.True(cache.Contains("d"));
    }

    [Fact]
    public void LRU_AccessRefreshesOrder()
    {
        using var cache = new ImageCache(3);
        cache.Put("a", CreateTestBitmap(), 10, 10);
        cache.Put("b", CreateTestBitmap(), 10, 10);
        cache.Put("c", CreateTestBitmap(), 10, 10);

        cache.Get("a"); // refresh "a" → now "b" is oldest

        cache.Put("d", CreateTestBitmap(), 10, 10); // should evict "b"

        Assert.True(cache.Contains("a")); // refreshed
        Assert.False(cache.Contains("b")); // evicted
        Assert.True(cache.Contains("c"));
        Assert.True(cache.Contains("d"));
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        using var cache = new ImageCache(5);
        cache.Put("a", CreateTestBitmap(), 10, 10);
        cache.Put("b", CreateTestBitmap(), 10, 10);

        cache.Clear();

        Assert.False(cache.Contains("a"));
        Assert.False(cache.Contains("b"));
    }

    [Fact]
    public void Put_SameKey_RetainsPrevious()
    {
        using var cache = new ImageCache(5);
        var bmp1 = CreateTestBitmap(10, 10);
        var bmp2 = CreateTestBitmap(20, 20);

        cache.Put("key", bmp1, 10, 10);
        var added = cache.Put("key", bmp2, 20, 20);

        Assert.False(added); // same key → not added
        var result = cache.Get("key");
        Assert.Same(bmp1, result); // original retained

        var (w, h) = cache.GetOriginalSize("key");
        Assert.Equal(10, w);
        Assert.Equal(10, h);

        bmp2.Dispose(); // caller owns rejected bitmap
    }
}
