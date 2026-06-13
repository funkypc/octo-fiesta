namespace octo_fiesta.Services.YouTubeMusic;

/// <summary>
/// Helper class for YouTube Music quality settings.
/// </summary>
public static class YouTubeMusicQuality
{
    public const string FLAC = "FLAC";
    public const string MP3_256 = "MP3_256";
    public const string MP3_128 = "MP3_128";
    public const string AAC_64 = "AAC_64";

    private static readonly HashSet<string> _validQualities = new(StringComparer.OrdinalIgnoreCase)
    {
        FLAC,
        MP3_256,
        MP3_128,
        AAC_64
    };

    public static List<string> ValidQualities => _validQualities.ToList();

    public static bool IsValid(string? quality)
    {
        if (string.IsNullOrWhiteSpace(quality))
        {
            return true;
        }
        return _validQualities.Contains(quality);
    }

    /// <summary>
    /// Maps YouTube Music stream info to a quality label.
    /// </summary>
    public static string FromApiParams(string? mimeType, int bitrate)
    {
        var codec = (mimeType ?? "").Split(";")[0].Replace("audio/", "").Trim().ToLowerInvariant();
        var bitrateKbps = bitrate / 1000;

        if (codec == "flac" || (bitrateKbps >= 1000 && codec == "mp4"))
        {
            return FLAC;
        }

        if (bitrateKbps >= 256)
        {
            return codec switch
            {
                "mp4" => MP3_256,
                "mp4a" => MP3_256,
                "opus" => MP3_256,
                "mp3" => MP3_256,
                _ => MP3_256
            };
        }

        if (bitrateKbps >= 128)
        {
            return MP3_128;
        }

        return AAC_64;
    }

    /// <summary>
    /// Maps a quality setting to a ytmusicapi format preference.
    /// </summary>
    public static string? ToApiParam(string? quality)
    {
        quality = quality?.Trim().ToUpperInvariant();
        return quality;
    }

    /// <summary>
    /// Maps a codec name to a file extension.
    /// </summary>
    public static string CodecToExtension(string? codec)
    {
        return codec?.Trim().ToLowerInvariant() switch
        {
            "flac" => ".flac",
            "mp3" => ".mp3",
            "aac" => ".aac",
            "opus" => ".opus",
            "mp4" => ".m4a",
            "mp4a" => ".m4a",
            _ => ".m4a"
        };
    }

    /// <summary>
    /// Maps a MIME type to a file extension.
    /// </summary>
    public static string MimeTypeToExtension(string? mimeType)
    {
        if (string.IsNullOrWhiteSpace(mimeType))
        {
            return ".m4a";
        }

        var codec = mimeType.Split(";")[0].Replace("audio/", "").Trim().ToLowerInvariant();
        return CodecToExtension(codec);
    }
}
