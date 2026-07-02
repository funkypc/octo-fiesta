using System.Diagnostics;
using System.Net;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Common;
using octo_fiesta.Services.Local;
using octo_fiesta.Services.Subsonic;
using Microsoft.Extensions.Options;

namespace octo_fiesta.Services.YouTubeMusic;

public class YouTubeMusicDownloadService : BaseDownloadService
{
    private readonly YouTubeMusicBridgeService _bridge;
    private readonly YouTubeMusicSettings _settings;
    private readonly ILogger<YouTubeMusicDownloadService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

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
        _httpClientFactory = httpClientFactory;
    }

    public override async Task<bool> IsAvailableAsync()
    {
        return await _bridge.IsBridgeAvailableAsync();
    }

    protected override async Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        var quality = _settings.Quality ?? "FLAC";
        cancellationToken.ThrowIfCancellationRequested();
        var totalSw = Stopwatch.StartNew();
        var bridgeSw = Stopwatch.StartNew();

        // Fast path: get a direct stream URL and download via HTTP (avoids yt-dlp overhead)
        var streamResult = await _bridge.GetStreamUrlAsync(trackId, quality);
        if (streamResult?.Url != null)
        {
            try
            {
                return await TryDirectDownloadAsync(trackId, streamResult, totalSw, bridgeSw, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Direct stream URL download failed for {TrackId}, falling back to bridge", trackId);
            }
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "octo-fiesta-ytm-dl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string? filePath = null;
        try
        {
            var downloadResult = await _bridge.DownloadTrackFileAsync(trackId, quality, tempDir);
            bridgeSw.Stop();

            if (downloadResult == null || string.IsNullOrEmpty(downloadResult.Filepath))
            {
                throw new Exception($"yt-dlp download failed for track {trackId}: bridge returned no filepath");
            }

            filePath = downloadResult.Filepath;
            if (!File.Exists(filePath))
            {
                throw new Exception($"yt-dlp download failed for track {trackId}: file not found at {filePath}");
            }

            _logger.LogInformation(
                "Downloaded track {TrackId} via yt-dlp: {Filepath} (codec={Codec}, bitrate={Bitrate})",
                trackId, filePath, downloadResult.Codec, downloadResult.Bitrate);

            var readSw = Stopwatch.StartNew();
            var memoryStream = new MemoryStream();
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await fileStream.CopyToAsync(memoryStream, cancellationToken);
            }
            memoryStream.Position = 0;
            readSw.Stop();

            var extension = YouTubeMusicQuality.MimeTypeToExtension(downloadResult.MimeType);
            var downloadedQuality = YouTubeMusicQuality.FromApiParams(downloadResult.MimeType, downloadResult.Bitrate);

            double? mp4Duration = null;
            if (downloadResult.DurationMs > 0)
            {
                mp4Duration = downloadResult.DurationMs / 1000.0;
            }

            if (_settings.VerboseTiming)
            {
                _logger.LogInformation(
                    "[ytm-dl] track={TrackId} bridge={BridgeMs}ms read={ReadMs}ms total={TotalMs}ms bytes={Bytes}",
                    trackId, bridgeSw.ElapsedMilliseconds, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, memoryStream.Length);
            }

            return new DownloadResult(memoryStream, extension, downloadedQuality, mp4Duration);
        }
        finally
        {
            var cleanupSw = Stopwatch.StartNew();
            // Clean up downloaded file and temp directory
            if (filePath != null)
            {
                try { File.Delete(filePath); } catch { /* best effort */ }
            }
            try { Directory.Delete(tempDir, false); } catch { /* best effort */ }
            cleanupSw.Stop();
            if (_settings.VerboseTiming)
            {
                _logger.LogInformation("[ytm-dl] track={TrackId} cleanup={CleanupMs}ms", trackId, cleanupSw.ElapsedMilliseconds);
            }
        }
    }

    private async Task<DownloadResult> TryDirectDownloadAsync(
        string trackId,
        Models.YouTubeMusic.YouTubeMusicStreamResult streamResult,
        Stopwatch totalSw,
        Stopwatch bridgeSw,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();
        var url = streamResult.Url!;
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var retryUrl = url + (url.Contains('?') ? "&" : "?") + "ratebypass=yes";
            using var retryResponse = await httpClient.GetAsync(retryUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            retryResponse.EnsureSuccessStatusCode();
            return await BuildResultFromHttpContentAsync(trackId, streamResult, retryResponse.Content, totalSw, bridgeSw, cancellationToken);
        }

        response.EnsureSuccessStatusCode();
        return await BuildResultFromHttpContentAsync(trackId, streamResult, response.Content, totalSw, bridgeSw, cancellationToken);
    }

    private async Task<DownloadResult> BuildResultFromHttpContentAsync(
        string trackId,
        Models.YouTubeMusic.YouTubeMusicStreamResult streamResult,
        HttpContent content,
        Stopwatch totalSw,
        Stopwatch bridgeSw,
        CancellationToken cancellationToken)
    {
        var readSw = Stopwatch.StartNew();
        var memoryStream = new MemoryStream();
        await content.CopyToAsync(memoryStream);
        memoryStream.Position = 0;
        readSw.Stop();

        var extension = YouTubeMusicQuality.MimeTypeToExtension(streamResult.MimeType);
        var downloadedQuality = YouTubeMusicQuality.FromApiParams(streamResult.MimeType, streamResult.Bitrate);

        double? mp4Duration = null;
        if (streamResult.DurationMs > 0)
        {
            mp4Duration = streamResult.DurationMs / 1000.0;
        }

        _logger.LogInformation(
            "Downloaded track {TrackId} via direct stream URL: {Url} (codec={Codec}, bitrate={Bitrate})",
            trackId, streamResult.Url, streamResult.Codec, streamResult.Bitrate);

        if (_settings.VerboseTiming)
        {
            _logger.LogInformation(
                "[ytm-dl] track={TrackId} direct={DirectMs}ms read={ReadMs}ms total={TotalMs}ms bytes={Bytes}",
                trackId, bridgeSw.ElapsedMilliseconds, readSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, memoryStream.Length);
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
