namespace octo_fiesta.Models.Settings;

/// <summary>
/// Configuration for the JiaSaavn downloader and metadata service
/// Powered by SquidWTF-hosted JiaSaavn API
/// </summary>
public class JiaSaavnSettings
{
    /// <summary>
    /// Base URL for the JiaSaavn API
    /// Default: https://saavn.squid.wtf
    /// </summary>
    public string BaseUrl { get; set; } = "https://saavn.squid.wtf";

    /// <summary>
    /// Preferred audio quality: 320, 160, 96, 48, 12 (kbps)
    /// If not specified or unavailable, the highest available quality (320) will be used.
    /// </summary>
    public string? Quality { get; set; } = "320";
}
