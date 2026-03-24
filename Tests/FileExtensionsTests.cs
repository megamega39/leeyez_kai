namespace leeyez_kai.Tests;

public class FileExtensionsTests
{
    [Theory]
    [InlineData(".jpg", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".png", true)]
    [InlineData(".webp", true)]
    [InlineData(".avif", true)]
    [InlineData(".bmp", true)]
    [InlineData(".gif", true)]
    [InlineData(".mp4", false)]
    [InlineData(".zip", false)]
    [InlineData(".txt", false)]
    [InlineData("", false)]
    public void IsImage_ReturnsCorrect(string ext, bool expected)
    {
        Assert.Equal(expected, FileExtensions.IsImage(ext));
    }

    [Theory]
    [InlineData(".mp4", true)]
    [InlineData(".mkv", true)]
    [InlineData(".webm", true)]
    [InlineData(".jpg", false)]
    [InlineData(".mp3", false)]
    public void IsVideo_ReturnsCorrect(string ext, bool expected)
    {
        Assert.Equal(expected, FileExtensions.IsVideo(ext));
    }

    [Theory]
    [InlineData(".mp3", true)]
    [InlineData(".flac", true)]
    [InlineData(".wav", true)]
    [InlineData(".mp4", false)]
    [InlineData(".jpg", false)]
    public void IsAudio_ReturnsCorrect(string ext, bool expected)
    {
        Assert.Equal(expected, FileExtensions.IsAudio(ext));
    }

    [Theory]
    [InlineData(".mp4", true)]
    [InlineData(".mp3", true)]
    [InlineData(".jpg", false)]
    public void IsMedia_ReturnsVideoOrAudio(string ext, bool expected)
    {
        Assert.Equal(expected, FileExtensions.IsMedia(ext));
    }

    [Theory]
    [InlineData(".zip", true)]
    [InlineData(".rar", true)]
    [InlineData(".7z", true)]
    [InlineData(".cbz", true)]
    [InlineData(".jpg", false)]
    public void IsArchive_ReturnsCorrect(string ext, bool expected)
    {
        Assert.Equal(expected, FileExtensions.IsArchive(ext));
    }

    [Theory]
    [InlineData(".jpg", true)]
    [InlineData(".mp4", true)]
    [InlineData(".mp3", true)]
    [InlineData(".zip", false)]
    [InlineData(".txt", false)]
    public void IsViewable_ReturnsImageOrMedia(string ext, bool expected)
    {
        Assert.Equal(expected, FileExtensions.IsViewable(ext));
    }

    [Theory]
    [InlineData("test.jpg", ".jpg")]
    [InlineData("test.PNG", ".png")]
    [InlineData("archive.ZIP", ".zip")]
    [InlineData("noext", "")]
    public void GetExt_ReturnsLowercase(string name, string expected)
    {
        Assert.Equal(expected, FileExtensions.GetExt(name));
    }

    // HashSetはcase-insensitiveか確認
    [Theory]
    [InlineData(".JPG")]
    [InlineData(".Jpg")]
    [InlineData(".jpg")]
    public void IsImage_CaseInsensitive(string ext)
    {
        Assert.True(FileExtensions.IsImage(ext));
    }
}
