namespace leeyez_kai.Tests;

public class AppConstantsTests
{
    [Fact]
    public void ZoomMin_LessThan_ZoomMax()
    {
        Assert.True(AppConstants.ZoomMin < AppConstants.ZoomMax);
    }

    [Fact]
    public void PrefetchCount_Positive()
    {
        Assert.True(AppConstants.PrefetchCount > 0);
    }

    [Fact]
    public void ImageCacheSize_Positive()
    {
        Assert.True(AppConstants.ImageCacheSize > 0);
    }

    [Fact]
    public void DebounceMs_Reasonable()
    {
        Assert.InRange(AppConstants.DebounceMs, 10, 500);
    }
}
