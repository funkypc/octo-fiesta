using System.Net;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Common;
using octo_fiesta.Services.Local;
using octo_fiesta.Services.Subsonic;
using Microsoft.Extensions.Options;
using System.Text.Json;

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

    /// <summary>
    /// Parses the AuthCookie settings value into a CookieCollection for use with HTTP requests.
    /// Supports JSON dict format: {"__Secure-3PAPISID": "value", ...}
    /// and raw cookie string format: "key=val; key2=val2"
    /// </summary>
    private CookieCollection ParseAuthCookies()
    {
        var cookies = new CookieCollection();
        if (string.IsNullOrEmpty(_settings.AuthCookie))
            return cookies;

        // Try JSON dict format first
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(_settings.AuthCookie);
            if (data != null)
            {
                foreach (var (key, value) in data)
                {
                    cookies.Add(new Cookie(key, value, "/", ".youtube.com"));
                }
                return cookies;
            }
        }
        catch (JsonException) { }

        // Try raw cookie string format: "key=val; key2=val2"
        foreach (var pair in _settings.AuthCookie.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eqIndex = pair.IndexOf('=');
            if (eqIndex > 0)
            {
                var key = pair[..eqIndex].Trim();
                var value = pair[(eqIndex + 1)..].Trim();
                cookies.Add(new Cookie(key, value, "/", ".youtube.com"));
            }
        }

        return cookies;
    }

    public override async Task<bool> IsAvailableAsync()
    {
        return await _bridge.IsBridgeAvailableAsync();
    }

    protected override async Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        var quality = _settings.Quality ?? "FLAC";
        // Check for cancellation before starting the slow bridge call
        cancellationToken.ThrowIfCancellationRequested();
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

        // Use a separate CancellationTokenSource with a generous timeout for the actual download,
        // so the download completes even if the Subsonic client disconnects.
        using var downloadCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        // Build request with auth cookies for YouTube CDN
        using var request = new HttpRequestMessage(HttpMethod.Get, streamInfo.Url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
        request.Headers.Add("Referer", "https://music.youtube.com/");
        request.Headers.Add("Origin", "https://music.youtube.com");

        // Add auth cookies to the request
        var authCookies = ParseAuthCookies();
        if (authCookies.Count > 0)
        {
            var cookieContainer = new CookieContainer();
            cookieContainer.Add(authCookies);
            var cookieHeader = cookieContainer.GetCookieHeader(new Uri(streamInfo.Url));
            request.Headers.Add("Cookie", cookieHeader);
        }

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, downloadCts.Token);
        response.EnsureSuccessStatusCode();

        // Buffer the entire stream to memory so the download completes regardless
        // of whether the Subsonic client disconnects. YouTube Music tracks are
        // typically 3-15 MB so this is safe.
        var memoryStream = new MemoryStream();
        await using var networkStream = await HttpResponseStream.CreateAsync(response, downloadCts.Token);
        await networkStream.CopyToAsync(memoryStream, downloadCts.Token);
        memoryStream.Position = 0;

        var extension = YouTubeMusicQuality.MimeTypeToExtension(streamInfo.MimeType);
        var downloadedQuality = YouTubeMusicQuality.FromApiParams(streamInfo.MimeType, streamInfo.Bitrate);

        // YouTube Music returns audio in MP4 containers (M4A), so we may need to handle duration
        double? mp4Duration = null;
        if (streamInfo.DurationMs > 0)
        {
            mp4Duration = streamInfo.DurationMs / 1000.0;
        }

        return new DownloadResult(memoryStream, extension, downloadedQuality, mp4Duration);
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
