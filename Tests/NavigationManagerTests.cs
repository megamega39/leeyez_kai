using leeyez_kai.Services;

namespace leeyez_kai.Tests;

public class NavigationManagerTests
{
    [Fact]
    public void InitialState_NoHistory()
    {
        var nav = new NavigationManager();
        Assert.False(nav.CanGoBack);
        Assert.False(nav.CanGoForward);
        Assert.True(string.IsNullOrEmpty(nav.CurrentPath));
    }

    [Fact]
    public void NavigateTo_SetsCurrentPath()
    {
        var nav = new NavigationManager();
        nav.NavigateTo("C:\\test");
        Assert.Equal("C:\\test", nav.CurrentPath);
    }

    [Fact]
    public void NavigateTo_ThenGoBack()
    {
        var nav = new NavigationManager();
        nav.NavigateTo("C:\\a");
        nav.NavigateTo("C:\\b");

        Assert.True(nav.CanGoBack);
        var result = nav.GoBack();
        Assert.Equal("C:\\a", result);
        Assert.Equal("C:\\a", nav.CurrentPath);
    }

    [Fact]
    public void GoBack_ThenGoForward()
    {
        var nav = new NavigationManager();
        nav.NavigateTo("C:\\a");
        nav.NavigateTo("C:\\b");

        nav.GoBack();
        Assert.True(nav.CanGoForward);

        var result = nav.GoForward();
        Assert.Equal("C:\\b", result);
    }

    [Fact]
    public void GoBack_AtStart_ReturnsNull()
    {
        var nav = new NavigationManager();
        nav.NavigateTo("C:\\a");
        Assert.False(nav.CanGoBack);
        Assert.Null(nav.GoBack());
    }

    [Fact]
    public void GoForward_AtEnd_ReturnsNull()
    {
        var nav = new NavigationManager();
        nav.NavigateTo("C:\\a");
        Assert.False(nav.CanGoForward);
        Assert.Null(nav.GoForward());
    }

    [Fact]
    public void NavigateAfterGoBack_ClearsForwardHistory()
    {
        var nav = new NavigationManager();
        nav.NavigateTo("C:\\a");
        nav.NavigateTo("C:\\b");
        nav.NavigateTo("C:\\c");

        nav.GoBack(); // → b
        nav.NavigateTo("C:\\d"); // forward history (c) should be cleared

        Assert.False(nav.CanGoForward);
        Assert.True(nav.CanGoBack);
    }

    [Fact]
    public void DuplicateNavigation_DoesNotAddToHistory()
    {
        var nav = new NavigationManager();
        nav.NavigateTo("C:\\a");
        nav.NavigateTo("C:\\a"); // same path

        Assert.False(nav.CanGoBack); // should not have added duplicate
    }
}
