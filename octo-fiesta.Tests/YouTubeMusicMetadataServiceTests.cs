using octo_fiesta.Models.Domain;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.YouTubeMusic;
using octo_fiesta.Services.YouTubeMusic;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace octo_fiesta.Tests;

public class YouTubeMusicMetadataServiceTests
{
    private readonly Mock<YouTubeMusicBridgeService> _bridgeMock;
    private readonly YouTubeMusicMetadataService _service;
    private readonly Mock<ILogger<YouTubeMusicMetadataService>> _loggerMock;

    public YouTubeMusicMetadataServiceTests()
    {
        _loggerMock = new Mock<ILogger<YouTubeMusicMetadataService>>();
        _bridgeMock = new Mock<YouTubeMusicBridgeService>(
            Mock.Of<Microsoft.Extensions.Options.IOptions<Models.Settings.YouTubeMusicSettings>>(),
            Mock.Of<ILogger<YouTubeMusicBridgeService>>())
        { CallBase = true };
        var settings = new SubsonicSettings { ExplicitFilter = ExplicitFilter.All };
        var ytSettings = new YouTubeMusicSettings();
        _service = new YouTubeMusicMetadataService(_bridgeMock.Object, _loggerMock.Object, Microsoft.Extensions.Options.Options.Create(settings), Microsoft.Extensions.Options.Options.Create(ytSettings));
    }

    [Fact]
    public async Task SearchSongsAsync_ReturnsMappedSongs()
    {
        // Arrange
        var bridgeResult = new YouTubeMusicSongSearchResult
        {
            Songs = new List<YouTubeMusicTrack>
            {
                new()
                {
                    VideoId = "abc123",
                    Title = "Bohemian Rhapsody",
                    Artists = new List<YouTubeMusicArtistShort>
                    {
                        new() { Name = "Queen", Id = "UC123" }
                    },
                    Album = new YouTubeMusicAlbumShort { Name = "A Night at the Opera", Id = "MPREb_album1" },
                    DurationSeconds = 354,
                    TrackNumber = 1,
                    TrackCount = 12,
                    Year = 1975,
                    Thumbnails = new List<YouTubeMusicThumbnail>
                    {
                        new() { Url = "https://thumb.com/small.jpg", Width = 60, Height = 60 },
                        new() { Url = "https://thumb.com/large.jpg", Width = 544, Height = 544 }
                    },
                    IsExplicit = false
                }
            }
        };

        _bridgeMock.Setup(b => b.SearchSongsAsync("queen", 20)).ReturnsAsync(bridgeResult);

        // Act
        var result = await _service.SearchSongsAsync("queen", 20);

        // Assert
        Assert.Single(result);
        var song = result[0];
        Assert.Equal("Bohemian Rhapsody", song.Title);
        Assert.Equal("Queen", song.Artist);
        Assert.Equal("abc123", song.ExternalId);
        Assert.Equal("youtube_music", song.ExternalProvider);
        Assert.Equal(354, song.Duration);
        Assert.Equal(1, song.Track);
        Assert.Equal(12, song.TotalTracks);
        Assert.Equal(1975, song.Year);
        Assert.Equal("A Night at the Opera", song.Album);
        Assert.Equal("ext-youtube_music-album-MPREb_album1", song.AlbumId);
        Assert.Equal("ext-youtube_music-artist-UC123", song.ArtistId);
        Assert.Equal("https://thumb.com/large.jpg", song.CoverArtUrlLarge);
    }

    [Fact]
    public async Task SearchSongsAsync_WithEmptyResult_ReturnsEmptyList()
    {
        _bridgeMock.Setup(b => b.SearchSongsAsync("unknown", 20)).ReturnsAsync(new YouTubeMusicSongSearchResult());

        var result = await _service.SearchSongsAsync("unknown", 20);
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchSongsAsync_WhenBridgeThrows_ReturnsEmptyList()
    {
        _bridgeMock.Setup(b => b.SearchSongsAsync("fail", 20)).ThrowsAsync(new Exception("bridge error"));

        var result = await _service.SearchSongsAsync("fail", 20);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSongAsync_ReturnsMappedSong()
    {
        var bridgeResult = new YouTubeMusicSongResult
        {
            Song = new YouTubeMusicTrack
            {
                VideoId = "xyz789",
                Title = "Test Song",
                Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Test Artist", Id = "UC456" } },
                DurationSeconds = 180,
                IsExplicit = true
            }
        };

        _bridgeMock.Setup(b => b.GetSongAsync("xyz789")).ReturnsAsync(bridgeResult.Song);

        var result = await _service.GetSongAsync("youtube_music", "xyz789");

        Assert.NotNull(result);
        Assert.Equal("Test Song", result.Title);
        Assert.Equal("Test Artist", result.Artist);
        Assert.Equal("xyz789", result.ExternalId);
        Assert.Equal(1, result.ExplicitContentLyrics);
    }

    [Fact]
    public async Task GetSongAsync_WithWrongProvider_ReturnsNull()
    {
        var result = await _service.GetSongAsync("deezer", "123");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSongAsync_WhenBridgeReturnsNull_ReturnsNull()
    {
        _bridgeMock.Setup(b => b.GetSongAsync("missing")).ReturnsAsync((YouTubeMusicTrack?)null);

        var result = await _service.GetSongAsync("youtube_music", "missing");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAlbumAsync_ReturnsMappedAlbumWithTracks()
    {
        var album = new YouTubeMusicAlbum
        {
            BrowseId = "MPREb_album1",
            Title = "Test Album",
            Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Test Artist", Id = "UC789" } },
            Year = 2020,
            TrackCount = 5,
            Thumbnails = new List<YouTubeMusicThumbnail> { new() { Url = "https://cover.jpg", Width = 300, Height = 300 } },
            Tracks = new List<YouTubeMusicTrack>
            {
                new()
                {
                    VideoId = "track1",
                    Title = "Song 1",
                    Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Test Artist", Id = "UC789" } },
                    DurationSeconds = 200,
                    TrackNumber = 1
                },
                new()
                {
                    VideoId = "track2",
                    Title = "Song 2",
                    Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Test Artist", Id = "UC789" } },
                    DurationSeconds = 180,
                    TrackNumber = 2
                }
            }
        };

        _bridgeMock.Setup(b => b.GetAlbumAsync("MPREb_album1")).ReturnsAsync(album);

        var result = await _service.GetAlbumAsync("youtube_music", "MPREb_album1");

        Assert.NotNull(result);
        Assert.Equal("Test Album", result.Title);
        Assert.Equal("ext-youtube_music-album-MPREb_album1", result.Id);
        Assert.Equal("Test Artist", result.Artist);
        Assert.Equal("ext-youtube_music-artist-UC789", result.ArtistId);
        Assert.Equal(2020, result.Year);
        Assert.Equal(5, result.SongCount);
        Assert.Equal(2, result.Songs.Count);
        Assert.Equal("Song 1", result.Songs[0].Title);
        Assert.Equal("Song 2", result.Songs[1].Title);
    }

    [Fact]
    public async Task GetAlbumAsync_WithWrongProvider_ReturnsNull()
    {
        var result = await _service.GetAlbumAsync("deezer", "123");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetArtistAsync_ReturnsMappedArtist()
    {
        var artist = new YouTubeMusicArtist
        {
            BrowseId = "UC789",
            Name = "Test Artist",
            Thumbnails = new List<YouTubeMusicThumbnail>
            {
                new() { Url = "https://artist.jpg", Width = 400, Height = 400 }
            },
            AlbumCount = 10
        };

        _bridgeMock.Setup(b => b.GetArtistAsync("UC789")).ReturnsAsync(artist);

        var result = await _service.GetArtistAsync("youtube_music", "UC789");

        Assert.NotNull(result);
        Assert.Equal("Test Artist", result.Name);
        Assert.Equal("ext-youtube_music-artist-UC789", result.Id);
        Assert.Equal(10, result.AlbumCount);
        Assert.Equal("https://artist.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task GetArtistAlbumsAsync_ReturnsMappedAlbums()
    {
        var albums = new List<YouTubeMusicAlbum>
        {
            new()
            {
                BrowseId = "MPREb_album1",
                Title = "Album 1",
                Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Test Artist", Id = "UC789" } },
                Year = 2020
            },
            new()
            {
                BrowseId = "MPREb_album2",
                Title = "Album 2",
                Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Test Artist", Id = "UC789" } },
                Year = 2022
            }
        };

        _bridgeMock.Setup(b => b.GetArtistAlbumsAsync("UC789")).ReturnsAsync(albums);

        var result = await _service.GetArtistAlbumsAsync("youtube_music", "UC789");

        Assert.Equal(2, result.Count);
        Assert.Equal("Album 1", result[0].Title);
        Assert.Equal("Album 2", result[1].Title);
    }

    [Fact]
    public async Task SearchPlaylistsAsync_ReturnsEmptyList()
    {
        var result = await _service.SearchPlaylistsAsync("test", 20);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPlaylistAsync_ReturnsNull()
    {
        var result = await _service.GetPlaylistAsync("youtube_music", "pl123");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPlaylistTracksAsync_ReturnsEmptyList()
    {
        var result = await _service.GetPlaylistTracksAsync("youtube_music", "pl123");
        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchAllAsync_ReturnsCombinedResults()
    {
        var bridgeResult = new YouTubeMusicCombinedSearchResult
        {
            Songs = new List<YouTubeMusicTrack>
            {
                new() { VideoId = "s1", Title = "Song 1", Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Artist", Id = "UC1" } }, DurationSeconds = 200 }
            },
            Albums = new List<YouTubeMusicAlbum>
            {
                new() { BrowseId = "a1", Title = "Album 1", Artists = new List<YouTubeMusicArtistShort> { new() { Name = "Artist", Id = "UC1" } }, Year = 2020 }
            },
            Artists = new List<YouTubeMusicArtist>
            {
                new() { BrowseId = "art1", Name = "Artist 1" }
            }
        };

        _bridgeMock.Setup(b => b.SearchAllAsync("test", 20, 20, 20)).ReturnsAsync(bridgeResult);

        var result = await _service.SearchAllAsync("test", 20, 20, 20);

        Assert.Single(result.Songs);
        Assert.Single(result.Albums);
        Assert.Single(result.Artists);
        Assert.Equal("Song 1", result.Songs[0].Title);
        Assert.Equal("Album 1", result.Albums[0].Title);
        Assert.Equal("Artist 1", result.Artists[0].Name);
    }

    [Fact]
    public async Task GetArtistAlbumsAsync_WithWrongProvider_ReturnsEmptyList()
    {
        var result = await _service.GetArtistAlbumsAsync("deezer", "123");
        Assert.Empty(result);
    }
}
