using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Search;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.Subsonic;
using octo_fiesta.Services.Common;
using Microsoft.Extensions.Options;

namespace octo_fiesta.Services.YouTubeMusic;

/// <summary>
/// Metadata service implementation using YouTube Music via ytmusicapi bridge.
/// </summary>
public class YouTubeMusicMetadataService : IMusicMetadataService
{
    private readonly YouTubeMusicBridgeService _bridge;
    private readonly ILogger<YouTubeMusicMetadataService> _logger;
    private readonly SubsonicSettings _settings;
    public const string ProviderName = "youtube_music";
    private const string SongPrefix = "ext-youtube_music-";
    private const string AlbumPrefix = "ext-youtube_music-album-";
    private const string ArtistPrefix = "ext-youtube_music-artist-";

    public YouTubeMusicMetadataService(
        YouTubeMusicBridgeService bridge,
        ILogger<YouTubeMusicMetadataService> logger,
        IOptions<SubsonicSettings> settings)
    {
        _bridge = bridge;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<List<Song>> SearchSongsAsync(string query, int limit = 20)
    {
        try
        {
            var result = await _bridge.SearchSongsAsync(query, limit);
            return result.Songs.Select(MapTrackToSong).Where(ShouldIncludeSong).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search songs for query: {Query}", query);
            return new List<Song>();
        }
    }

    public async Task<List<Album>> SearchAlbumsAsync(string query, int limit = 20)
    {
        try
        {
            var result = await _bridge.SearchAlbumsAsync(query, limit);
            return result.Albums.Select(MapAlbumToAlbum).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search albums for query: {Query}", query);
            return new List<Album>();
        }
    }

    public async Task<List<Artist>> SearchArtistsAsync(string query, int limit = 20)
    {
        try
        {
            var result = await _bridge.SearchArtistsAsync(query, limit);
            return result.Artists.Select(MapArtistToArtist).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search artists for query: {Query}", query);
            return new List<Artist>();
        }
    }

    public async Task<SearchResult> SearchAllAsync(string query, int songLimit = 20, int albumLimit = 20, int artistLimit = 20)
    {
        try
        {
            var result = await _bridge.SearchAllAsync(query, songLimit, albumLimit, artistLimit);
            return new SearchResult
            {
                Songs = result.Songs.Select(MapTrackToSong).Where(ShouldIncludeSong).ToList(),
                Albums = result.Albums.Select(MapAlbumToAlbum).ToList(),
                Artists = result.Artists.Select(MapArtistToArtist).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to search all for query: {Query}", query);
            return new SearchResult();
        }
    }

    public async Task<Song?> GetSongAsync(string externalProvider, string externalId)
    {
        if (externalProvider != ProviderName) return null;

        var track = await _bridge.GetSongAsync(externalId);
        if (track == null) return null;

        return MapTrackToSong(track);
    }

    public async Task<Album?> GetAlbumAsync(string externalProvider, string externalId)
    {
        if (externalProvider != ProviderName) return null;

        var album = await _bridge.GetAlbumAsync(externalId);
        if (album == null) return null;

        return MapAlbumWithTracks(album);
    }

    public async Task<Artist?> GetArtistAsync(string externalProvider, string externalId)
    {
        if (externalProvider != ProviderName) return null;

        var artist = await _bridge.GetArtistAsync(externalId);
        if (artist == null) return null;

        return MapArtistToArtist(artist);
    }

    public async Task<List<Album>> GetArtistAlbumsAsync(string externalProvider, string externalId)
    {
        if (externalProvider != ProviderName) return new List<Album>();

        var albums = await _bridge.GetArtistAlbumsAsync(externalId);
        return albums.Select(MapAlbumToAlbum).ToList();
    }

    public Task<List<ExternalPlaylist>> SearchPlaylistsAsync(string query, int limit = 20)
    {
        // Playlists not supported in initial implementation
        return Task.FromResult(new List<ExternalPlaylist>());
    }

    public Task<ExternalPlaylist?> GetPlaylistAsync(string externalProvider, string externalId)
    {
        // Playlists not supported in initial implementation
        return Task.FromResult<ExternalPlaylist?>(null);
    }

    public Task<List<Song>> GetPlaylistTracksAsync(string externalProvider, string externalId)
    {
        // Playlists not supported in initial implementation
        return Task.FromResult(new List<Song>());
    }

    #region Mapping

    private Song MapTrackToSong(Models.YouTubeMusic.YouTubeMusicTrack track)
    {
        var externalId = track.VideoId ?? "";
        var mainArtist = track.Artists.FirstOrDefault();
        var album = track.Album;

        // Get best thumbnail
        var thumbnails = track.Thumbnails;
        var coverUrl = thumbnails.LastOrDefault()?.Url ?? thumbnails.FirstOrDefault()?.Url;
        var coverUrlLarge = thumbnails.OrderByDescending(t => t.Width).FirstOrDefault()?.Url ?? coverUrl;

        // Duration: prefer durationSeconds, fallback to parsing duration string
        int? durationSeconds = track.DurationSeconds;
        if (durationSeconds == null && !string.IsNullOrEmpty(track.Duration))
        {
            durationSeconds = ParseDuration(track.Duration);
        }

        // Explicit content mapping
        int? explicitContent = track.IsExplicit ? 1 : 0;

        var artistList = track.Artists.Select(a => new Artist
        {
            Id = ArtistPrefix + a.Id,
            Name = a.Name,
            IsLocal = false,
            ExternalProvider = ProviderName,
            ExternalId = a.Id
        }).ToList();

        return new Song
        {
            Title = track.Title,
            Artist = mainArtist?.Name ?? "",
            Artists = artistList,
            ArtistId = mainArtist?.Id != null ? ArtistPrefix + mainArtist.Id : null,
            Album = album?.Name ?? "",
            AlbumId = album?.Id != null ? AlbumPrefix + album.Id : null,
            Duration = durationSeconds,
            Track = track.TrackNumber,
            TotalTracks = track.TrackCount,
            Year = track.Year,
            CoverArtUrl = coverUrl,
            CoverArtUrlLarge = coverUrlLarge,
            AlbumArtist = track.Artists.Count > 1 ? track.Artists.FirstOrDefault()?.Name : null,
            Contributors = track.Artists.Skip(1).Select(a => a.Name).ToList(),
            IsLocal = false,
            ExternalProvider = ProviderName,
            ExternalId = externalId,
            ExplicitContentLyrics = explicitContent
        };
    }

    private Album MapAlbumToAlbum(Models.YouTubeMusic.YouTubeMusicAlbum album)
    {
        var externalId = album.BrowseId ?? "";
        var mainArtist = album.Artists.FirstOrDefault();
        var coverUrl = album.Thumbnails.LastOrDefault()?.Url ?? album.Thumbnails.FirstOrDefault()?.Url;
        var coverUrlLarge = album.Thumbnails.OrderByDescending(t => t.Width).FirstOrDefault()?.Url ?? coverUrl;

        return new Album
        {
            Id = AlbumPrefix + externalId,
            Title = album.Title,
            Artist = mainArtist?.Name ?? "",
            ArtistId = mainArtist?.Id != null ? ArtistPrefix + mainArtist.Id : null,
            Year = album.Year,
            SongCount = album.TrackCount,
            CoverArtUrl = coverUrl,
            CoverArtUrlLarge = coverUrlLarge,
            IsLocal = false,
            ExternalProvider = ProviderName,
            ExternalId = externalId,
            Songs = new List<Song>()
        };
    }

    private Album MapAlbumWithTracks(Models.YouTubeMusic.YouTubeMusicAlbum album)
    {
        var mapped = MapAlbumToAlbum(album);
        var albumArtist = album.Artists.FirstOrDefault()?.Name;
        int trackIndex = 1;

        foreach (var track in album.Tracks)
        {
            var song = MapTrackToSong(track);
            // Override album info for album tracks
            song.Album = album.Title;
            song.AlbumId = AlbumPrefix + album.BrowseId;
            song.AlbumArtist = albumArtist;
            song.Track ??= trackIndex;
            song.TotalTracks ??= album.TrackCount;
            song.CoverArtUrl ??= mapped.CoverArtUrl;
            song.CoverArtUrlLarge ??= mapped.CoverArtUrlLarge;

            if (ShouldIncludeSong(song))
            {
                mapped.Songs.Add(song);
                trackIndex++;
            }
        }

        return mapped;
    }

    private Artist MapArtistToArtist(Models.YouTubeMusic.YouTubeMusicArtist artist)
    {
        var externalId = artist.BrowseId ?? "";
        var coverUrl = artist.Thumbnails.LastOrDefault()?.Url ?? artist.Thumbnails.FirstOrDefault()?.Url;
        var coverUrlLarge = artist.Thumbnails.OrderByDescending(t => t.Width).FirstOrDefault()?.Url ?? coverUrl;

        return new Artist
        {
            Id = ArtistPrefix + externalId,
            Name = artist.Name,
            ImageUrl = coverUrlLarge,
            AlbumCount = artist.AlbumCount,
            IsLocal = false,
            ExternalProvider = ProviderName,
            ExternalId = externalId
        };
    }

    private int? ParseDuration(string duration)
    {
        // Parse "MM:SS" or "H:MM:SS"
        try
        {
            var parts = duration.Split(':');
            if (parts.Length == 2)
            {
                if (int.TryParse(parts[0], out var minutes) && int.TryParse(parts[1], out var seconds))
                {
                    return minutes * 60 + seconds;
                }
            }
            else if (parts.Length == 3)
            {
                if (int.TryParse(parts[0], out var hours) && int.TryParse(parts[1], out var minutes) && int.TryParse(parts[2], out var seconds))
                {
                    return hours * 3600 + minutes * 60 + seconds;
                }
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    private bool ShouldIncludeSong(Song song)
    {
        // If no explicit content info, include the song
        if (song.ExplicitContentLyrics == null)
            return true;

        return _settings.ExplicitFilter switch
        {
            // All: No filtering, include everything
            ExplicitFilter.All => true,

            // ExplicitOnly: Exclude clean/edited versions (value 3)
            // Include: 0 (naturally clean), 1 (explicit), 2 (not applicable), 6/7 (unknown)
            ExplicitFilter.ExplicitOnly => song.ExplicitContentLyrics != 3,

            // CleanOnly: Only show clean content
            // Include: 0 (naturally clean), 3 (clean/edited version)
            // Exclude: 1 (explicit)
            ExplicitFilter.CleanOnly => song.ExplicitContentLyrics != 1,

            _ => true
        };
    }

    #endregion
}
