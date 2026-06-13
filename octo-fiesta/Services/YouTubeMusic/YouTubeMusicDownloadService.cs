using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Common;
using octo_fiesta.Services.Local;
using octo_fiesta.Services.Subsonic;
using Microsoft.Extensions.Options;

namespace octo_fiesta.Services.YouTubeMusic;

/// <summary>
/// Download service implementation using YouTube Music via ytmusicapi bridge.
/// </summary>
public class YouTubeMusicDownloadService : BaseDownloadService
{
    private readonly YouTubeMusicBridgeService _bridge;
    private readonly YouTubeMusicSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<YouTubeMusicDownloadService> _logger;

    protected override string ProviderName => "youtube_music";

    private const string AlbumPrefix = "ext-youtube_music-album-";

    public YouTubeMusicDownloadService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILocalLibraryService localLibraryService,
        IMusicMetadataService metadataService,
        IOptions<SubsonicSettings> subsonicSettings,
        IOptions<YouTubeMusicSettings> youTubeMusicSettings,
        YouTubeMusicBridgeService bridge,
        IServiceProvider serviceProvider,
        ILogger<YouTubeMusicDownloadService> logger)
        : base(httpClientFactory, configuration, localLibraryService, metadataService, subsonicSettings.Value, serviceProvider, logger)
    {
        _logger = logger;
        _bridge = bridge;
        _settings = youTubeMusicSettings.Value;
        _httpClient = httpClientFactory.CreateClient("YouTubeMusic");
    }

    public override async Task<bool> IsAvailableAsync()
    {
        return await _bridge.IsBridgeAvailableAsync();
    }

    protected override async Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        var quality = _settings.Quality ?? "FLAC";
        var streamInfo = await _bridge.GetStreamUrlAsync(trackId, quality);

        if (streamInfo == null || string.IsNullOrEmpty(streamInfo.Url))
        {
            var detail = streamInfo == null
                ? "bridge returned null"
                : $"bridge returned url='{streamInfo.Url}', mimeType='{streamInfo.MimeType}'";
            throw new Exception($"No streaming URL available for track {trackId} ({detail})");
        }

        _logger.LogInformation(
            "Downloading track {TrackId} from YouTube Music: {Url} (codec={Codec}, bitrate={Bitrate})",
            trackId, streamInfo.Url, streamInfo.Codec, streamInfo.Bitrate);

        var response = await _httpClient.GetAsync(streamInfo.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseStream = await HttpResponseStream.CreateAsync(response, cancellationToken);
        var extension = YouTubeMusicQuality.MimeTypeToExtension(streamInfo.MimeType);
        var downloadedQuality = YouTubeMusicQuality.FromApiParams(streamInfo.MimeType, streamInfo.Bitrate);

        // YouTube Music returns audio in MP4 containers (M4A), so we may need to handle duration
        double? mp4Duration = null;
        if (streamInfo.DurationMs > 0)
        {
            mp4Duration = streamInfo.DurationMs / 1000.0;
        }

        return new DownloadResult(responseStream, extension, downloadedQuality, mp4Duration);
    }

    protected override string? ExtractExternalIdFromAlbumId(string albumId)
    {
        if (albumId.StartsWith(AlbumPrefix))
        {
            return albumId[AlbumPrefix.Length..];
        }
        return null;
    }

    protected override string? GetTargetQuality() => _settings.Quality ?? "FLAC";
}
