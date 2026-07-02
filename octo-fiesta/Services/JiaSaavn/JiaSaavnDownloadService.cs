using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.SquidWTF;
using octo_fiesta.Services.Common;
using octo_fiesta.Services.Local;
using Microsoft.Extensions.Options;
using IOFile = System.IO.File;

namespace octo_fiesta.Services.JiaSaavn;

/// <summary>
/// Download service implementation using the JiaSaavn API (SquidWTF-hosted)
/// Handles DES-decrypted media URL resolution and direct streaming.
/// </summary>
public class JiaSaavnDownloadService : BaseDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly JiaSaavnSettings _settings;

    private const string DesKey = "38346591";
    private static readonly byte[] DesKeyBytes = Encoding.UTF8.GetBytes(DesKey);

    protected override string ProviderName => "jiosaavn";

    public JiaSaavnDownloadService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILocalLibraryService localLibraryService,
        IMusicMetadataService metadataService,
        IOptions<SubsonicSettings> subsonicSettings,
        IOptions<JiaSaavnSettings> jiaSaavnSettings,
        IServiceProvider serviceProvider,
        ILogger<JiaSaavnDownloadService> logger)
        : base(httpClientFactory, configuration, localLibraryService, metadataService, subsonicSettings.Value, serviceProvider, logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _settings = jiaSaavnSettings.Value;
    }

    #region BaseDownloadService Implementation

    public override async Task<bool> IsAvailableAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_settings.SearchApiUrl}/api/songs?q=test");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "JiaSaavn service not available");
            return false;
        }
    }

    protected override string? ExtractExternalIdFromAlbumId(string albumId)
    {
        const string prefix = "ext-jiosaavn-album-";
        if (albumId.StartsWith(prefix))
        {
            return albumId[prefix.Length..];
        }
        return null;
    }

    protected override string? GetTargetQuality()
    {
        return _settings.Quality ?? "320";
    }

    protected override async Task<DownloadResult> DownloadTrackAsync(string trackId, Song song, CancellationToken cancellationToken)
    {
        // Fetch full song details to obtain encrypted_media_url
        var detailUrl = $"{_settings.DetailApiUrl}/song?url={Uri.EscapeDataString(trackId)}";
        var detailResponse = await _httpClient.GetAsync(detailUrl, cancellationToken);
        detailResponse.EnsureSuccessStatusCode();

        var detailJson = await detailResponse.Content.ReadAsStringAsync(cancellationToken);
        var detailSong = JsonSerializer.Deserialize<JiaSaavnSong>(detailJson);

        if (detailSong?.MoreInfo?.EncryptedMediaUrl == null)
        {
            throw new Exception("Failed to get encrypted_media_url from JiaSaavn API");
        }

        var decryptedUrl = DecryptMediaUrl(detailSong.MoreInfo.EncryptedMediaUrl);
        var quality = GetTargetQuality() ?? "320";
        var downloadUrl = GetQualityUrl(decryptedUrl, quality);

        Logger.LogInformation("Got JiaSaavn download URL for track {TrackId}: {Title} (quality: {Quality})",
            trackId, song.Title, quality);

        var stream = await GetDownloadStreamAsync(downloadUrl, cancellationToken);
        return new DownloadResult(stream, ".m4a", $"MP3_{quality}");
    }

    #endregion

    #region DES Decryption Helpers

    /// <summary>
    /// Decrypts a JioSaavn encrypted_media_url using DES ECB PKCS7.
    /// Key: 38346591
    /// </summary>
    internal static string DecryptMediaUrl(string encrypted)
    {
        // Pad base64 string if needed
        int padLen = (4 - (encrypted.Length % 4)) % 4;
        var padded = encrypted + new string('=', padLen);

        byte[] cipherBytes = Convert.FromBase64String(padded);

        using var des = DES.Create();
        des.Key = DesKeyBytes;
        des.Mode = CipherMode.ECB;
        des.Padding = PaddingMode.PKCS7;

        using var decryptor = des.CreateDecryptor();
        byte[] plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// Swaps the quality suffix in a decrypted media URL.
    /// e.g. https://.../song_96.mp4 -> https://.../song_320.mp4
    /// </summary>
    internal static string GetQualityUrl(string decryptedUrl, string quality)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            decryptedUrl,
            @"_\d+\.mp4(\?.*)?$",
            $"_{quality}.mp4$1");
    }

    #endregion

    #region Stream Helpers

    private async Task<Stream> GetDownloadStreamAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0");
        request.Headers.Add("Accept", "*/*");

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await HttpResponseStream.CreateAsync(response, cancellationToken);
    }

    #endregion
}
