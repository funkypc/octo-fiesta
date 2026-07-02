namespace octo_fiesta.Services.YouTubeMusic;

/// <summary>
/// Configures the HttpClient for downloading audio streams from YouTube CDN.
/// </summary>
public static class YouTubeMusicHttpClientConfiguration
{
    public static void ConfigureClient(IServiceProvider sp, HttpClient client)
    {
        // YouTube CDN URLs are dynamic, so we don't set a base address
        // Large timeout for audio downloads which can be slow
        client.Timeout = TimeSpan.FromMinutes(10);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "*/*");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        client.DefaultRequestHeaders.Add("Accept-Encoding", "identity");
        client.DefaultRequestHeaders.Add("Referer", "https://music.youtube.com/");
        client.DefaultRequestHeaders.Add("Origin", "https://music.youtube.com");
    }
}
