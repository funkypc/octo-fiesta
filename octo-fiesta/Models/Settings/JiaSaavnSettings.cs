namespace octo_fiesta.Models.Settings;

/// <summary>
/// Configuration for the JiaSaavn downloader and metadata service
/// </summary>
public class JiaSaavnSettings
{
    /// <summary>
    /// Base URL for the JiaSaavn search API (songs and albums search)
    /// Default: https://js-odskyler.vercel.app
    /// </summary>
    public string SearchApiUrl { get; set; } = "https://js-odskyler.vercel.app";

    /// <summary>
    /// Base URL for the JiaSaavn detail API (song and album lookups by URL)
    /// Default: https://sda.rhythmax.workers.dev
    /// </summary>
    public string DetailApiUrl { get; set; } = "https://sda.rhythmax.workers.dev";

    /// <summary>
    /// Preferred audio quality: 320, 160, 96, 48, 12 (kbps)
    /// If not specified or unavailable, the highest available quality (320) will be used.
    /// </summary>
    public string? Quality { get; set; } = "320";
}
