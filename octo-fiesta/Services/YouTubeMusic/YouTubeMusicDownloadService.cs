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
    }

    public override async Task<bool> IsAvailableAsync()
    {
        return await _bridge.IsBridgeAvailableAsync();
    }

    protected override async Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        var quality = _settings.Quality ?? "FLAC";
        cancellationToken.ThrowIfCancellationRequested();

        var tempDir = Path.Combine(Path.GetTempPath(), "octo-fiesta-ytm-dl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string? filePath = null;
        try
        {
            var downloadResult = await _bridge.DownloadTrackFileAsync(trackId, quality, tempDir);

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

            var memoryStream = new MemoryStream();
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await fileStream.CopyToAsync(memoryStream, cancellationToken);
            }
            memoryStream.Position = 0;

            var extension = YouTubeMusicQuality.MimeTypeToExtension(downloadResult.MimeType);
            var downloadedQuality = YouTubeMusicQuality.FromApiParams(downloadResult.MimeType, downloadResult.Bitrate);

            double? mp4Duration = null;
            if (downloadResult.DurationMs > 0)
            {
                mp4Duration = downloadResult.DurationMs / 1000.0;
            }

            return new DownloadResult(memoryStream, extension, downloadedQuality, mp4Duration);
        }
        finally
        {
            // Clean up downloaded file and temp directory
            if (filePath != null)
            {
                try { File.Delete(filePath); } catch { /* best effort */ }
            }
            try { Directory.Delete(tempDir, false); } catch { /* best effort */ }
        }
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