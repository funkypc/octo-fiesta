using System.Text.Json.Serialization;

namespace octo_fiesta.Models.SquidWTF;

#region JiaSaavn API Responses (saavn.squid.wtf)

/// <summary>
/// Generic search response wrapper used by both song and album search endpoints
/// </summary>
public class JiaSaavnSearchResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("start")]
    public int Start { get; set; }

    [JsonPropertyName("results")]
    public List<JiaSaavnSearchResult>? Results { get; set; }
}

public class JiaSaavnSearchResult
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("perma_url")]
    public string? PermaUrl { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("play_count")]
    public string? PlayCount { get; set; }

    [JsonPropertyName("isExplicit")]
    public bool IsExplicit { get; set; }

    [JsonPropertyName("more_info")]
    public JiaSaavnSearchMoreInfo? MoreInfo { get; set; }
}

public class JiaSaavnSearchMoreInfo
{
    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("encrypted_media_url")]
    public string? EncryptedMediaUrl { get; set; }

    [JsonPropertyName("song_count")]
    public string? SongCount { get; set; }

    [JsonPropertyName("artists")]
    public JiaSaavnArtists? Artists { get; set; }
}

public class JiaSaavnSong
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("perma_url")]
    public string? PermaUrl { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("play_count")]
    public string? PlayCount { get; set; }

    [JsonPropertyName("isExplicit")]
    public bool IsExplicit { get; set; }

    [JsonPropertyName("more_info")]
    public JiaSaavnMoreInfo? MoreInfo { get; set; }
}

public class JiaSaavnMoreInfo
{
    [JsonPropertyName("album_id")]
    public string? AlbumId { get; set; }

    [JsonPropertyName("album_token")]
    public string? AlbumToken { get; set; }

    [JsonPropertyName("album")]
    public string? Album { get; set; }

    [JsonPropertyName("album_url")]
    public string? AlbumUrl { get; set; }

    [JsonPropertyName("encrypted_media_url")]
    public string? EncryptedMediaUrl { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("copyright_text")]
    public string? CopyrightText { get; set; }

    [JsonPropertyName("artists")]
    public JiaSaavnArtists? Artists { get; set; }

    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("vcode")]
    public string? Vcode { get; set; }

    [JsonPropertyName("vlink")]
    public string? Vlink { get; set; }
}

public class JiaSaavnArtists
{
    [JsonPropertyName("primary")]
    public List<JiaSaavnArtist>? Primary { get; set; }

    [JsonPropertyName("featured")]
    public List<JiaSaavnArtist>? Featured { get; set; }
}

public class JiaSaavnArtist
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("artist_token")]
    public string? ArtistToken { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("perma_url")]
    public string? PermaUrl { get; set; }
}

public class JiaSaavnAlbumDetail
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("header_desc")]
    public string? HeaderDesc { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("perma_url")]
    public string? PermaUrl { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("song_count")]
    public string? SongCount { get; set; }

    [JsonPropertyName("isExplicit")]
    public bool IsExplicit { get; set; }

    [JsonPropertyName("copyright")]
    public string? Copyright { get; set; }

    [JsonPropertyName("artists")]
    public JiaSaavnArtists? Artists { get; set; }

    [JsonPropertyName("songs")]
    public List<JiaSaavnSong>? Songs { get; set; }
}

#endregion
