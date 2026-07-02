namespace octo_fiesta.Models.Settings;

/// <summary>
/// Configuration for the YouTube Music downloader and metadata service
/// </summary>
public class YouTubeMusicSettings
{
    /// <summary>
    /// YouTube Music authentication cookie header.
    /// Obtained from browser cookies after logging into music.youtube.com.
    /// Must include SID, HSID, SSID, APISID, SAPISID, __Secure-1PSID, __Secure-1PSIDTS cookies.
    /// </summary>
    public string? AuthCookie { get; set; }

    /// <summary>
    /// Preferred audio quality.
    /// If not specified or unavailable, the highest available quality will be used.
    /// Default: FLAC
    /// Available: FLAC, MP3_256, MP3_128, AAC_64
    /// </summary>
    public string? Quality { get; set; } = "FLAC";

    /// <summary>
    /// Path to the Python executable.
    /// Default: "python3" (Linux/macOS) or "python" (Windows)
    /// </summary>
    public string PythonPath { get; set; } = "python3";

    /// <summary>
    /// Path to the ytmusicapi bridge script.
    /// Default: "./youtube-music-bridge.py"
    /// </summary>
    public string ScriptPath { get; set; } = "./youtube-music-bridge.py";

    /// <summary>
    /// Include unavailable songs in albums and search results.
    /// Default: false
    /// </summary>
    public bool IncludeUnavailable { get; set; } = false;

    /// <summary>
    /// Enable detailed timestamped timing logs for search/download.
    /// Default: true
    /// </summary>
    public bool VerboseTiming { get; set; } = true;
}
