using System.Text.Json;
using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.Search;
using octo_fiesta.Models.Subsonic;
using octo_fiesta.Models.SquidWTF;
using Microsoft.Extensions.Options;

namespace octo_fiesta.Services.JiaSaavn;

/// <summary>
/// Metadata service implementation using the JiaSaavn API (SquidWTF-hosted)
/// </summary>
public class JiaSaavnMetadataService : IMusicMetadataService
{
    private readonly HttpClient _httpClient;
    private readonly JiaSaavnSettings _settings;
    private readonly SubsonicSettings _subsonicSettings;
    private readonly ILogger<JiaSaavnMetadataService> _logger;

    public JiaSaavnMetadataService(
        IHttpClientFactory httpClientFactory,
        IOptions<JiaSaavnSettings> settings,
        IOptions<SubsonicSettings> subsonicSettings,
        ILogger<JiaSaavnMetadataService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _settings = settings.Value;
        _subsonicSettings = subsonicSettings.Value;
        _logger = logger;
    }

    #region IMusicMetadataService Implementation

    public async Task<List<Song>> SearchSongsAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{_settings.SearchApiUrl}/api/songs?q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("JiaSaavn song search returned {StatusCode} for {Url}", response.StatusCode, url);
                return new List<Song>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<JiaSaavnSearchResponse>(json);
            if (searchResponse?.Results == null) return new List<Song>();

            var songs = new List<Song>();
            foreach (var result in searchResponse.Results.Take(limit))
            {
                var song = MapSearchResultToSong(result);
                if (ShouldIncludeSong(song))
                    songs.Add(song);
            }
            return songs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search songs for query: {Query}", query);
            return new List<Song>();
        }
    }

    public async Task<List<Album>> SearchAlbumsAsync(string query, int limit = 20)
    {
        try
        {
            var url = $"{_settings.SearchApiUrl}/api/albums?q={Uri.EscapeDataString(query)}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("JiaSaavn album search returned {StatusCode} for {Url}", response.StatusCode, url);
                return new List<Album>();
            }

            var json = await response.Content.ReadAsStringAsync();
            var searchResponse = JsonSerializer.Deserialize<JiaSaavnSearchResponse>(json);
            if (searchResponse?.Results == null) return new List<Album>();

            return searchResponse.Results
                .Where(r => r.Type == "album")
                .Take(limit)
                .Select(MapSearchResultToAlbum)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search albums for query: {Query}", query);
            return new List<Album>();
        }
    }

    public async Task<List<Artist>> SearchArtistsAsync(string query, int limit = 20)
    {
        try
        {
            // JiaSaavn API does not expose a dedicated artist search endpoint.
            // Derive artists from song search results.
            var songs = await SearchSongsAsync(query, limit * 2);
            var artists = new List<Artist>();
            var seen = new HashSet<string>();

            foreach (var song in songs)
            {
                foreach (var artist in song.Artists ?? new List<Artist>())
                {
                    if (!string.IsNullOrEmpty(artist.Name) && seen.Add(artist.Name))
                    {
                        artists.Add(new Artist
                        {
                            Id = artist.Id,
                            Name = artist.Name,
                            IsLocal = false,
                            ExternalProvider = "jiosaavn",
                            ExternalId = artist.ExternalId
                        });
                    }
                }

                if (artists.Count >= limit) break;
            }

            return artists.Take(limit).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search artists for query: {Query}", query);
            return new List<Artist>();
        }
    }

    public async Task<SearchResult> SearchAllAsync(string query, int songLimit = 20, int albumLimit = 20, int artistLimit = 20)
    {
        var songsTask = SearchSongsAsync(query, songLimit);
        var albumsTask = SearchAlbumsAsync(query, albumLimit);
        var artistsTask = SearchArtistsAsync(query, artistLimit);

        await Task.WhenAll(songsTask, albumsTask, artistsTask);

        return new SearchResult
        {
            Songs = await songsTask,
            Albums = await albumsTask,
            Artists = await artistsTask
        };
    }

    public async Task<Song?> GetSongAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "jiosaavn") return null;

        try
        {
            var url = $"{_settings.DetailApiUrl}/song?url={Uri.EscapeDataString(externalId)}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("JiaSaavn song detail returned {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var song = JsonSerializer.Deserialize<JiaSaavnSong>(json);
            if (song == null) return null;

            return MapJiaSaavnSongToSong(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get song: {ExternalId}", externalId);
            return null;
        }
    }

    public async Task<Album?> GetAlbumAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "jiosaavn") return null;

        try
        {
            var url = $"{_settings.DetailApiUrl}/album?url={Uri.EscapeDataString(externalId)}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("JiaSaavn album detail returned {StatusCode} for {Url}", response.StatusCode, url);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var album = JsonSerializer.Deserialize<JiaSaavnAlbumDetail>(json);
            if (album == null) return null;

            return MapJiaSaavnAlbumDetailToAlbum(album);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get album: {ExternalId}", externalId);
            return null;
        }
    }

    public Task<Artist?> GetArtistAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "jiosaavn") return Task.FromResult<Artist?>(null);

        // JiaSaavn API does not expose a dedicated artist lookup endpoint.
        return Task.FromResult<Artist?>(null);
    }

    public Task<List<Album>> GetArtistAlbumsAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "jiosaavn") return Task.FromResult(new List<Album>());

        // JiaSaavn API does not expose a dedicated artist albums endpoint.
        return Task.FromResult(new List<Album>());
    }

    public Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
    {
        // JiaSaavn API does not support playlist search.
        return Task.FromResult(new List<ExternalPlaylist>());
    }

    public Task<ExternalPlaylist?> GetPlaylistAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "jiosaavn") return Task.FromResult<ExternalPlaylist?>(null);

        // JiaSaavn API does not support playlist lookup.
        return Task.FromResult<ExternalPlaylist?>(null);
    }

    public Task<List<Song>> GetPlaylistTracksAsync(string externalProvider, string externalId)
    {
        if (externalProvider != "jiosaavn") return Task.FromResult(new List<Song>());

        // JiaSaavn API does not support playlist tracks.
        return Task.FromResult(new List<Song>());
    }

    #endregion

    #region Mapping Methods

    private Song MapSearchResultToSong(JiaSaavnSearchResult result)
    {
        var artistName = ExtractArtistFromSubtitle(result.Subtitle);
        var artists = BuildArtists(result.MoreInfo?.Artists);
        var duration = ParseInt(result.MoreInfo?.Duration);
        var year = ParseInt(result.Year);
        var coverUrl = GetSizedImageUrl(result.Image, "150x150");
        var coverUrlLarge = GetSizedImageUrl(result.Image, "500x500");

        // Use perma_url as the stable external identifier
        var externalId = result.PermaUrl ?? result.Id ?? "";

        return new Song
        {
            Title = result.Title ?? "",
            Artist = artistName,
            Artists = artists,
            Album = result.MoreInfo?.Album ?? "",
            AlbumId = null,
            Duration = duration,
            Year = year,
            CoverArtUrl = coverUrl,
            CoverArtUrlLarge = coverUrlLarge,
            IsLocal = false,
            ExternalProvider = "jiosaavn",
            ExternalId = externalId,
            ExplicitContentLyrics = result.IsExplicit ? 1 : 0
        };
    }

    private Song MapJiaSaavnSongToSong(JiaSaavnSong song)
    {
        var artistName = ExtractArtistFromSubtitle(song.Subtitle);
        var artists = BuildArtists(song.MoreInfo?.Artists);
        var duration = ParseInt(song.MoreInfo?.Duration);
        var year = ParseInt(song.Year);
        var coverUrl = GetSizedImageUrl(song.Image, "150x150");
        var coverUrlLarge = GetSizedImageUrl(song.Image, "500x500");
        var releaseDate = song.MoreInfo?.ReleaseDate;

        var externalId = song.PermaUrl ?? song.Id ?? "";
        var albumExternalId = song.MoreInfo?.AlbumUrl ?? song.MoreInfo?.AlbumId ?? "";

        return new Song
        {
            Title = song.Title ?? "",
            Artist = artistName,
            Artists = artists,
            Album = song.MoreInfo?.Album ?? "",
            AlbumId = !string.IsNullOrEmpty(albumExternalId)
                ? $"ext-jiosaavn-album-{albumExternalId}"
                : null,
            Duration = duration,
            Year = year,
            ReleaseDate = releaseDate,
            Copyright = song.MoreInfo?.CopyrightText,
            CoverArtUrl = coverUrl,
            CoverArtUrlLarge = coverUrlLarge,
            IsLocal = false,
            ExternalProvider = "jiosaavn",
            ExternalId = externalId,
            ExplicitContentLyrics = song.IsExplicit ? 1 : 0
        };
    }

    private Album MapSearchResultToAlbum(JiaSaavnSearchResult result)
    {
        var artistName = ExtractArtistFromSubtitle(result.Subtitle);
        var year = ParseInt(result.Year);
        var songCount = ParseInt(result.MoreInfo?.SongCount);
        var coverUrl = GetSizedImageUrl(result.Image, "150x150");
        var coverUrlLarge = GetSizedImageUrl(result.Image, "500x500");
        var externalId = result.PermaUrl ?? result.Id ?? "";

        return new Album
        {
            Id = $"ext-jiosaavn-album-{externalId}",
            Title = result.Title ?? "",
            Artist = artistName,
            Year = year,
            SongCount = songCount,
            CoverArtUrl = coverUrl,
            CoverArtUrlLarge = coverUrlLarge,
            IsLocal = false,
            ExternalProvider = "jiosaavn",
            ExternalId = externalId
        };
    }

    private Album MapJiaSaavnAlbumDetailToAlbum(JiaSaavnAlbumDetail album)
    {
        var artistName = ExtractPrimaryArtistName(album.Artists);
        var year = ParseInt(album.Year);
        var songCount = ParseInt(album.SongCount);
        var coverUrl = GetSizedImageUrl(album.Image, "150x150");
        var coverUrlLarge = GetSizedImageUrl(album.Image, "500x500");
        var externalId = album.PermaUrl ?? album.Id ?? "";

        var result = new Album
        {
            Id = $"ext-jiosaavn-album-{externalId}",
            Title = album.Title ?? "",
            Artist = artistName,
            Year = year,
            SongCount = songCount,
            CoverArtUrl = coverUrl,
            CoverArtUrlLarge = coverUrlLarge,
            IsLocal = false,
            ExternalProvider = "jiosaavn",
            ExternalId = externalId
        };

        if (album.Songs != null)
        {
            int trackIndex = 1;
            foreach (var song in album.Songs)
            {
                var mappedSong = MapJiaSaavnSongToSong(song);
                mappedSong.Album = result.Title;
                mappedSong.AlbumId = result.Id;
                mappedSong.AlbumArtist = result.Artist;
                mappedSong.Year ??= result.Year;
                mappedSong.TotalTracks ??= result.SongCount;

                if (string.IsNullOrEmpty(mappedSong.CoverArtUrl))
                    mappedSong.CoverArtUrl = result.CoverArtUrl;
                if (string.IsNullOrEmpty(mappedSong.CoverArtUrlLarge))
                    mappedSong.CoverArtUrlLarge = result.CoverArtUrlLarge;

                if (ShouldIncludeSong(mappedSong))
                {
                    mappedSong.Track = trackIndex;
                    result.Songs.Add(mappedSong);
                    trackIndex++;
                }
            }
        }

        return result;
    }

    #endregion

    #region Helpers

    private static string ExtractArtistFromSubtitle(string? subtitle)
    {
        if (string.IsNullOrEmpty(subtitle)) return "Unknown Artist";
        var parts = subtitle.Split(new[] { " - " }, StringSplitOptions.None);
        return parts[0].Trim();
    }

    private static string ExtractPrimaryArtistName(JiaSaavnArtists? artists)
    {
        if (artists?.Primary == null || artists.Primary.Count == 0)
            return "Unknown Artist";
        return string.Join(", ", artists.Primary.Select(a => a.Name).Where(n => !string.IsNullOrEmpty(n)));
    }

    private static List<Artist> BuildArtists(JiaSaavnArtists? artists)
    {
        var result = new List<Artist>();
        if (artists?.Primary != null)
        {
            foreach (var a in artists.Primary)
            {
                if (!string.IsNullOrEmpty(a.Name))
                {
                    result.Add(new Artist
                    {
                        Id = !string.IsNullOrEmpty(a.Id) ? $"ext-jiosaavn-artist-{a.Id}" : "",
                        Name = a.Name,
                        ImageUrl = GetSizedImageUrl(a.Image, "150x150"),
                        IsLocal = false,
                        ExternalProvider = "jiosaavn",
                        ExternalId = a.Id
                    });
                }
            }
        }
        if (artists?.Featured != null)
        {
            foreach (var a in artists.Featured)
            {
                if (!string.IsNullOrEmpty(a.Name))
                {
                    result.Add(new Artist
                    {
                        Id = !string.IsNullOrEmpty(a.Id) ? $"ext-jiosaavn-artist-{a.Id}" : "",
                        Name = a.Name,
                        ImageUrl = GetSizedImageUrl(a.Image, "150x150"),
                        IsLocal = false,
                        ExternalProvider = "jiosaavn",
                        ExternalId = a.Id
                    });
                }
            }
        }
        return result;
    }

    private static string? GetSizedImageUrl(string? imageUrl, string size)
    {
        if (string.IsNullOrEmpty(imageUrl)) return null;
        return imageUrl.Replace("150x150", size).Replace("500x500", size).Replace("50x50", size);
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (int.TryParse(value, out var result)) return result;
        return null;
    }

    private bool ShouldIncludeSong(Song song)
    {
        if (song.ExplicitContentLyrics == null)
            return true;

        return _subsonicSettings.ExplicitFilter switch
        {
            ExplicitFilter.All => true,
            ExplicitFilter.ExplicitOnly => song.ExplicitContentLyrics != 3,
            ExplicitFilter.CleanOnly => song.ExplicitContentLyrics != 1,
            _ => true
        };
    }

    #endregion
}
