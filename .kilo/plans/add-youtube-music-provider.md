# Plan: Add YouTube Music Provider

## Overview

Add YouTube Music as a provider service (`youtube_music`) to octo-fiesta, following the same pattern as existing providers (Deezer, Qobuz, SquidWTF, Yandex). Users with YouTube Music Premium can search for and download songs (not music videos) from YouTube Music.

## Architecture: Python Subprocess Bridge

A Python script wrapping `ytmusicapi` will be called via `System.Diagnostics.Process` from C#. The C# service invokes the script for metadata queries and streaming URL retrieval; actual audio file downloading is done by C# `HttpClient`. This approach leverages the well-maintained `ytmusicapi` library, avoiding the need to reverse-engineer YouTube Music's complex protobuf-based internal API.

**Why not native C#?** YouTube Music's internal API uses protobuf serialization, complex auth signing, and changes frequently. ytmusicapi already handles all of this. Porting it to C# would be extremely fragile.

---

## Implementation Plan

### Phase 1: Configuration & Enumeration

1. **Add `YouTubeMusic` to `MusicService` enum** (`Models/Settings/SubsonicSettings.cs`)
   ```csharp
   YouTubeMusic
   ```

2. **Create `YouTubeMusicSettings`** (`Models/Settings/YouTubeMusicSettings.cs`)
   ```csharp
   public class YouTubeMusicSettings
   {
       public string? AuthCookie { get; set; }    // Browser cookie header for auth
       public string? Quality { get; set; } = "FLAC";
       public string PythonPath { get; set; } = "python3";
       public string ScriptPath { get; set; } = "./youtube-music-bridge.py";
       public bool IncludeUnavailable { get; set; } = false;
   }
   ```

3. **Register settings in DI** (`Program.cs`)
   - Add `builder.Services.Configure<YouTubeMusicSettings>(builder.Configuration.GetSection("YouTubeMusic"));`

4. **Add `youtube_music` to `PlaylistIdHelper.KnownProviders`** (`Services/Common/PlaylistIdHelper.cs`)

### Phase 2: Python Bridge Script

5. **Create `youtube-music-bridge.py`** (project root)
   - CLI script using ytmusicapi, accepts commands via CLI args, outputs JSON to stdout
   - Auth: loads cookie header from env var or config file; also supports OAuth via `YTMUSIC_OAUTH_CREDENTIALS`
   - Commands (initial, no playlist support):
     - `search-songs <query> <limit>` - Search with `filter="songs"` to get audio tracks only
     - `search-albums <query> <limit>` - Search albums
     - `search-artists <query> <limit>` - Search artists
     - `search-all <query> <song_limit> <album_limit> <artist_limit>` - Combined search
     - `get-song <video_id>` - Get song details
     - `get-album <browse_id>` - Get album with tracks
     - `get-artist <browse_id>` - Get artist details
     - `get-artist-albums <browse_id>` - Get artist's albums
     - `get-stream-url <video_id> [quality]` - Get streaming URL + info (mime, bitrate, codec, duration)
     - `check-auth` - Validate authentication works
   - All commands output structured JSON to stdout; errors output JSON with `{"error": "..."}` to stderr

### Phase 3: API Response Models

6. **Create `Models/YouTubeMusic/YouTubeMusicApiResponses.cs`**
   - C# record classes matching the Python bridge JSON output
   - Models for: tracks, albums, artists, stream info, search results, error responses
   - Uses `JsonPropertyName` for deserialization
   - Separate from domain models - these map the raw bridge output before converting to `Song`/`Album`/`Artist`

### Phase 4: Bridge Process Service

7. **Create `Services/YouTubeMusic/YouTubeMusicBridgeService.cs`**
   - Helper class that manages calling the Python subprocess
   - Handles process invocation, JSON serialization/deserialization, error handling
   - Caches authentication state (avoid re-initializing ytmusicapi on every call)
   - Methods like `CallBridgeAsync<T>(string command, params string[] args)`
   - Timeout handling, process reuse where possible

### Phase 5: Metadata Service

8. **Create `Services/YouTubeMusic/YouTubeMusicMetadataService.cs`**
   - Implements `IMusicMetadataService`
   - `ProviderName` = `"youtube_music"`
   - Delegates to `YouTubeMusicBridgeService` for all API calls
   - Maps bridge JSON responses to domain models (`Song`, `Album`, `Artist`)
   - ID convention:
     - Songs: `ext-youtube_music-{video_id}`
     - Albums: `ext-youtube_music-album-{browse_id}`
     - Artists: `ext-youtube_music-artist-{browse_id}`
   - Playlist methods return empty results (no playlist support initially):
     - `SearchPlaylistsAsync` -> empty list
     - `GetPlaylistAsync` -> null
     - `GetPlaylistTracksAsync` -> empty list
   - Uses `filter="songs"` in search to get audio tracks, not music videos

### Phase 6: Download Service

9. **Create `Services/YouTubeMusic/YouTubeMusicDownloadService.cs`**
   - Extends `BaseDownloadService`
   - `ProviderName` = `"youtube_music"`
   - `DownloadTrackAsync`:
     1. Calls bridge `get-stream-url <video_id> <quality>` to get streaming URL + file metadata
     2. Downloads audio stream via C# `HttpClient`
     3. Returns `DownloadResult` with stream, extension, and quality
   - `ExtractExternalIdFromAlbumId`: Extracts ID from `"ext-youtube_music-album-{id}"`
   - `GetTargetQuality`: Returns configured quality preference
   - `IsAvailableAsync`: Validates Python + ytmusicapi are installed and auth works

### Phase 7: HTTP Client Configuration

10. **Create `Services/YouTubeMusic/DI/YouTubeMusicHttpClientConfiguration.cs`**
    - Configures `HttpClient` with appropriate headers for YouTube CDN downloads
    - User-Agent, range header support for streaming

### Phase 8: Quality Helper

11. **Create `Services/YouTubeMusic/Helpers/YouTubeMusicQuality.cs`**
    - Quality constants: `FLAC`, `MP3_256`, `MP3_128`, `AAC_64`
    - `IsValid(quality)` - validates quality setting
    - `CodecToExtension(codec)` - maps codec to file extension
    - `FromApiParams(mimeType, bitrate)` - maps stream info to quality label
    - `ToApiParam(quality)` - maps quality setting to ytmusicapi quality parameter

### Phase 9: Startup Validator

12. **Create `Services/YouTubeMusic/YouTubeMusicStartupValidator.cs`**
    - Extends `BaseStartupValidator`
    - Validates:
      - Python executable found at configured path
      - ytmusicapi installed (`python3 -c "import ytmusicapi"`)
      - Auth cookie or OAuth credentials configured
      - Authentication works (calls `check-auth` via bridge)
    - `ServiceName` = `"YouTube Music"`

### Phase 10: DI Registration

13. **Update `Program.cs`**
    - Add YouTubeMusic provider branch:
      ```csharp
      else if (musicService == MusicService.YouTubeMusic)
      {
          builder.Services.AddSingleton<IMusicMetadataService, YouTubeMusicMetadataService>();
          builder.Services.AddSingleton<IDownloadService, YouTubeMusicDownloadService>();
      }
      ```
    - Register `YouTubeMusicSettings` configuration binding
    - Register `YouTubeMusicStartupValidator`
    - Register named HttpClient for YouTube CDN downloads
    - Register `YouTubeMusicBridgeService` as singleton (manages Python process)

### Phase 11: Configuration

14. **Update `appsettings.json`**
    ```json
    "YouTubeMusic": {
      "AuthCookie": "",
      "Quality": "FLAC",
      "PythonPath": "python3",
      "ScriptPath": "./youtube-music-bridge.py"
    }
    ```

15. **Create `requirements.txt`** at project root:
    ```
    ytmusicapi>=1.0.0
    ```

### Phase 12: Tests

16. **Create `octo-fiesta.Tests/YouTubeMusicMetadataServiceTests.cs`**
    - Test metadata parsing from bridge JSON responses
    - Test ID prefix/extraction logic
    - Test search result mapping

17. **Create `octo-fiesta.Tests/YouTubeMusicDownloadServiceTests.cs`**
    - Test quality mapping
    - Test album ID extraction
    - Test stream URL handling

18. **Create `octo-fiesta.Tests/YouTubeMusicQualityTests.cs`**
    - Test quality validation
    - Test codec-to-extension mapping

---

## ID Convention

- Songs: `ext-youtube_music-{video_id}` (e.g., `ext-youtube_music-dQw4w9WgXcQ`)
- Albums: `ext-youtube_music-album-{browse_id}` (e.g., `ext-youtube_music-album-MPREb_AlbumId`)
- Artists: `ext-youtube_music-artist-{browse_id}` (e.g., `ext-youtube_music-artist-UCChannelId`)

## Quality Options

YouTube Music with Premium supports:

| Setting  | Description                          |
|----------|--------------------------------------|
| FLAC     | Lossless (Premium only, selective)  |
| MP3_256  | High quality 256kbps (Premium)       |
| MP3_128  | Medium quality 128kbps               |
| AAC_64   | Low quality 64kbps                   |

## Authentication

YouTube Music requires a browser cookie header for authentication:
1. User logs into music.youtube.com in a browser
2. Exports cookie header (specifically `SID`, `HSID`, `SSID`, `APISID`, `SAPISID`, `__Secure-1PSID`, `__Secure-1PSIDTS` cookies)
3. Provides this as `AuthCookie` in configuration (JSON config or env var)
4. Alternatively, ytmusicapi supports OAuth credentials via `YTMUSIC_OAUTH_CREDENTIALS` env var

The bridge script will support both cookie-based auth and OAuth.

## File Structure

```
octo-fiesta/
├── youtube-music-bridge.py                      # Python bridge script
├── requirements.txt                              # Python deps
├── Models/
│   ├── Settings/
│   │   └── YouTubeMusicSettings.cs
│   └── YouTubeMusic/
│       └── YouTubeMusicApiResponses.cs
├── Services/
│   └── YouTubeMusic/
│       ├── YouTubeMusicBridgeService.cs          # Manages Python subprocess calls
│       ├── YouTubeMusicMetadataService.cs
│       ├── YouTubeMusicDownloadService.cs
│       ├── YouTubeMusicStartupValidator.cs
│       ├── Helpers/
│       │   └── YouTubeMusicQuality.cs
│       └── DI/
│           └── YouTubeMusicHttpClientConfiguration.cs
└── (modified) appsettings.json, Program.cs, PlaylistIdHelper.cs, SubsonicSettings.cs
```

## Deferred (Not in Initial Scope)

- **Playlist support**: `SearchPlaylistsAsync`, `GetPlaylistAsync`, `GetPlaylistTracksAsync` - returns empty/null initially, can be added later
- **OAuth browser flow**: Users must provide cookies manually initially; OAuth flow can be added as a convenience later