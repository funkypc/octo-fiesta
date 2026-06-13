using System.Text.Json.Serialization;

namespace octo_fiesta.Models.YouTubeMusic;

/// <summary>
/// Common wrapper for all bridge script responses.
/// Contains either a result or an error.
/// </summary>
public record YouTubeMusicBridgeResponse<T>
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("result")]
    public T? Result { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// Bridge result for a single song.
/// </summary>
public record YouTubeMusicSongResult
{
    [JsonPropertyName("song")]
    public YouTubeMusicTrack? Song { get; init; }
}

/// <summary>
/// Bridge result for album search.
/// </summary>
public record YouTubeMusicAlbumSearchResult
{
    [JsonPropertyName("albums")]
    public List<YouTubeMusicAlbum> Albums { get; init; } = new();
}

/// <summary>
/// Bridge result for artist search.
/// </summary>
public record YouTubeMusicArtistSearchResult
{
    [JsonPropertyName("artists")]
    public List<YouTubeMusicArtist> Artists { get; init; } = new();
}

/// <summary>
/// Bridge result for song search.
/// </summary>
public record YouTubeMusicSongSearchResult
{
    [JsonPropertyName("songs")]
    public List<YouTubeMusicTrack> Songs { get; init; } = new();
}

/// <summary>
/// Bridge result for combined search.
/// </summary>
public record YouTubeMusicCombinedSearchResult
{
    [JsonPropertyName("songs")]
    public List<YouTubeMusicTrack> Songs { get; init; } = new();

    [JsonPropertyName("albums")]
    public List<YouTubeMusicAlbum> Albums { get; init; } = new();

    [JsonPropertyName("artists")]
    public List<YouTubeMusicArtist> Artists { get; init; } = new();
}

/// <summary>
/// Bridge result for an album with tracks.
/// </summary>
public record YouTubeMusicAlbumResult
{
    [JsonPropertyName("album")]
    public YouTubeMusicAlbum? Album { get; init; }
}

/// <summary>
/// Bridge result for an artist.
/// </summary>
public record YouTubeMusicArtistResult
{
    [JsonPropertyName("artist")]
    public YouTubeMusicArtist? Artist { get; init; }
}

/// <summary>
/// Bridge result for artist albums.
/// </summary>
public record YouTubeMusicArtistAlbumsResult
{
    [JsonPropertyName("albums")]
    public List<YouTubeMusicAlbum> Albums { get; init; } = new();
}

/// <summary>
/// Bridge result for streaming URL.
/// </summary>
public record YouTubeMusicStreamResult
{
    [JsonPropertyName("videoId")]
    public string? VideoId { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("mimeType")]
    public string? MimeType { get; init; }

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; init; }

    [JsonPropertyName("codec")]
    public string? Codec { get; init; }

    [JsonPropertyName("quality")]
    public string? Quality { get; init; }

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; init; }
}

/// <summary>
/// Bridge result for auth check.
/// </summary>
public record YouTubeMusicAuthCheckResult
{
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; init; }

    [JsonPropertyName("searchWorks")]
    public bool SearchWorks { get; init; }
}

/// <summary>
/// Represents a track from ytmusicapi.
/// </summary>
public record YouTubeMusicTrack
{
    [JsonPropertyName("videoId")]
    public string? VideoId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("artists")]
    public List<YouTubeMusicArtistShort> Artists { get; init; } = new();

    [JsonPropertyName("album")]
    public YouTubeMusicAlbumShort? Album { get; init; }

    [JsonPropertyName("duration")]
    public string? Duration { get; init; }

    [JsonPropertyName("durationSeconds")]
    public int? DurationSeconds { get; init; }

    [JsonPropertyName("thumbnails")]
    public List<YouTubeMusicThumbnail> Thumbnails { get; init; } = new();

    [JsonPropertyName("isExplicit")]
    public bool IsExplicit { get; init; }

    [JsonPropertyName("videoType")]
    public string? VideoType { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("trackNumber")]
    public int? TrackNumber { get; init; }

    [JsonPropertyName("trackCount")]
    public int? TrackCount { get; init; }
}

/// <summary>
/// Short artist info embedded in tracks.
/// </summary>
public record YouTubeMusicArtistShort
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>
/// Short album info embedded in tracks.
/// </summary>
public record YouTubeMusicAlbumShort
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

/// <summary>
/// Represents an album from ytmusicapi.
/// </summary>
public record YouTubeMusicAlbum
{
    [JsonPropertyName("browseId")]
    public string? BrowseId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("artists")]
    public List<YouTubeMusicArtistShort> Artists { get; init; } = new();

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("trackCount")]
    public int? TrackCount { get; init; }

    [JsonPropertyName("thumbnails")]
    public List<YouTubeMusicThumbnail> Thumbnails { get; init; } = new();

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("tracks")]
    public List<YouTubeMusicTrack> Tracks { get; init; } = new();
}

/// <summary>
/// Represents an artist from ytmusicapi.
/// </summary>
public record YouTubeMusicArtist
{
    [JsonPropertyName("browseId")]
    public string? BrowseId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("thumbnails")]
    public List<YouTubeMusicThumbnail> Thumbnails { get; init; } = new();

    [JsonPropertyName("albumCount")]
    public int? AlbumCount { get; init; }
}

/// <summary>
/// Thumbnail from ytmusicapi.
/// </summary>
public record YouTubeMusicThumbnail
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("width")]
    public int? Width { get; init; }

    [JsonPropertyName("height")]
    public int? Height { get; init; }
}
