using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services;
using octo_fiesta.Services.YouTubeMusic;
using octo_fiesta.Services.Local;
using octo_fiesta.Services.Subsonic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace octo_fiesta.Tests;

public class YouTubeMusicDownloadServiceTests
{
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly Mock<ILocalLibraryService> _localLibraryServiceMock;
    private readonly Mock<IMusicMetadataService> _metadataServiceMock;
    private readonly Mock<YouTubeMusicBridgeService> _bridgeMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<ILogger<YouTubeMusicDownloadService>> _loggerMock;
    private readonly SubsonicSettings _subsonicSettings;
    private readonly YouTubeMusicSettings _youTubeMusicSettings;
    private readonly YouTubeMusicDownloadService _service;

    public YouTubeMusicDownloadServiceTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpMessageHandlerMock.Object);
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        _localLibraryServiceMock = new Mock<ILocalLibraryService>();
        _metadataServiceMock = new Mock<IMusicMetadataService>();
        _bridgeMock = new Mock<YouTubeMusicBridgeService>(
            Mock.Of<IOptions<YouTubeMusicSettings>>(),
            Mock.Of<ILogger<YouTubeMusicBridgeService>>())
        { CallBase = true };
        _serviceProviderMock = new Mock<IServiceProvider>();
        _loggerMock = new Mock<ILogger<YouTubeMusicDownloadService>>();

        _subsonicSettings = new SubsonicSettings { FolderTemplate = "{artist}/{album}/{track} - {title}" };
        _youTubeMusicSettings = new YouTubeMusicSettings { Quality = "FLAC" };

        var config = new ConfigurationBuilder().Build();

        _service = new YouTubeMusicDownloadService(
            _httpClientFactoryMock.Object,
            config,
            _localLibraryServiceMock.Object,
            _metadataServiceMock.Object,
            Options.Create(_subsonicSettings),
            Options.Create(_youTubeMusicSettings),
            _bridgeMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public void ProviderName_ReturnsYouTubeMusic()
    {
        // Access via reflection since ProviderName is protected
        var providerName = typeof(YouTubeMusicDownloadService)
            .GetProperty("ProviderName", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(_service) as string;

        Assert.Equal("youtube_music", providerName);
    }

    [Fact]
    public void ExtractExternalIdFromAlbumId_ValidId_ReturnsId()
    {
        var method = typeof(YouTubeMusicDownloadService)
            .GetMethod("ExtractExternalIdFromAlbumId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var result = method?.Invoke(_service, new[] { "ext-youtube_music-album-MPREb_abc123" });

        Assert.Equal("MPREb_abc123", result);
    }

    [Fact]
    public void ExtractExternalIdFromAlbumId_InvalidId_ReturnsNull()
    {
        var method = typeof(YouTubeMusicDownloadService)
            .GetMethod("ExtractExternalIdFromAlbumId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var result = method?.Invoke(_service, new[] { "ext-deezer-album-123" });

        Assert.Null(result);
    }

    [Fact]
    public void GetTargetQuality_ReturnsConfiguredQuality()
    {
        var method = typeof(YouTubeMusicDownloadService)
            .GetMethod("GetTargetQuality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var result = method?.Invoke(_service, null);

        Assert.Equal("FLAC", result);
    }

    [Fact]
    public void GetTargetQuality_DefaultQuality_ReturnsFLAC()
    {
        var settings = new YouTubeMusicSettings { Quality = null };
        var service = new YouTubeMusicDownloadService(
            _httpClientFactoryMock.Object,
            new ConfigurationBuilder().Build(),
            _localLibraryServiceMock.Object,
            _metadataServiceMock.Object,
            Options.Create(_subsonicSettings),
            Options.Create(settings),
            _bridgeMock.Object,
            _serviceProviderMock.Object,
            _loggerMock.Object);

        var method = typeof(YouTubeMusicDownloadService)
            .GetMethod("GetTargetQuality", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var result = method?.Invoke(service, null);

        Assert.Equal("FLAC", result);
    }

    [Fact]
    public async Task IsAvailableAsync_BridgeAvailable_ReturnsTrue()
    {
        _bridgeMock.Setup(b => b.IsBridgeAvailableAsync()).ReturnsAsync(true);

        var result = await _service.IsAvailableAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task IsAvailableAsync_BridgeUnavailable_ReturnsFalse()
    {
        _bridgeMock.Setup(b => b.IsBridgeAvailableAsync()).ReturnsAsync(false);

        var result = await _service.IsAvailableAsync();

        Assert.False(result);
    }

    [Fact]
    public void ExtractExternalIdFromAlbumId_EmptyPrefix_ReturnsNull()
    {
        var method = typeof(YouTubeMusicDownloadService)
            .GetMethod("ExtractExternalIdFromAlbumId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var result = method?.Invoke(_service, new[] { "ext-youtube_music-album-" });

        Assert.Equal("", result);
    }
}
