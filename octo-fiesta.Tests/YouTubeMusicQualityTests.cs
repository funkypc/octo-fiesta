using octo_fiesta.Services.YouTubeMusic;

namespace octo_fiesta.Tests;

public class YouTubeMusicQualityTests
{
    [Theory]
    [InlineData("FLAC", true)]
    [InlineData("MP3_256", true)]
    [InlineData("MP3_128", true)]
    [InlineData("AAC_64", true)]
    [InlineData("flac", true)]
    [InlineData("mp3_256", true)]
    [InlineData("invalid", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void IsValid_ReturnsExpectedResult(string? quality, bool expected)
    {
        var result = YouTubeMusicQuality.IsValid(quality);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("audio/flac", 1411000, "FLAC")]
    [InlineData("audio/mp4; codecs=mp4a.40.2", 256000, "MP3_256")]
    [InlineData("audio/mp4", 128000, "MP3_128")]
    [InlineData("audio/mp4", 64000, "AAC_64")]
    [InlineData("audio/opus", 128000, "MP3_128")]
    [InlineData("audio/mp3", 128000, "MP3_128")]
    [InlineData("audio/mp4", 256000, "MP3_256")]
    public void FromApiParams_ReturnsCorrectQuality(string mimeType, int bitrate, string expected)
    {
        var result = YouTubeMusicQuality.FromApiParams(mimeType, bitrate);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("flac", ".flac")]
    [InlineData("mp3", ".mp3")]
    [InlineData("aac", ".aac")]
    [InlineData("opus", ".opus")]
    [InlineData("mp4", ".m4a")]
    [InlineData("mp4a", ".m4a")]
    [InlineData("unknown", ".m4a")]
    [InlineData(null, ".m4a")]
    public void CodecToExtension_ReturnsCorrectExtension(string? codec, string expected)
    {
        var result = YouTubeMusicQuality.CodecToExtension(codec);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("audio/flac", ".flac")]
    [InlineData("audio/mp4; codecs=mp4a.40.2", ".m4a")]
    [InlineData("audio/mp3", ".mp3")]
    [InlineData("audio/opus", ".opus")]
    [InlineData(null, ".m4a")]
    public void MimeTypeToExtension_ReturnsCorrectExtension(string? mimeType, string expected)
    {
        var result = YouTubeMusicQuality.MimeTypeToExtension(mimeType);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ValidQualities_ContainsAllExpectedQualities()
    {
        var qualities = YouTubeMusicQuality.ValidQualities;
        Assert.Contains("FLAC", qualities);
        Assert.Contains("MP3_256", qualities);
        Assert.Contains("MP3_128", qualities);
        Assert.Contains("AAC_64", qualities);
        Assert.Equal(4, qualities.Count);
    }

    [Theory]
    [InlineData("FLAC", "FLAC")]
    [InlineData("MP3_256", "MP3_256")]
    [InlineData("MP3_128", "MP3_128")]
    [InlineData("AAC_64", "AAC_64")]
    public void ToApiParam_ReturnsCorrectParam(string quality, string expected)
    {
        var result = YouTubeMusicQuality.ToApiParam(quality);
        Assert.Equal(expected, result);
    }
}
