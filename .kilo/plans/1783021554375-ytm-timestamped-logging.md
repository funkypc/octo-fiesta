# Plan: Timestamped timing logs for YouTube Music search & download

## Goal
Diagnose slowness in YouTube Music search and download by adding detailed,
timestamped timing logs across the Python bridge and the C# YouTube Music
services. A single config flag enables/disables the verbose timing output.

## Scope (files to edit only)
- `octo-fiesta/Models/Settings/YouTubeMusicSettings.cs`
- `youtube-music-bridge.py`
- `octo-fiesta/Services/YouTubeMusic/YouTubeMusicBridgeService.cs`
- `octo-fiesta/Services/YouTubeMusic/YouTubeMusicMetadataService.cs`
- `octo-fiesta/Services/YouTubeMusic/YouTubeMusicDownloadService.cs`

No model/response shape changes. No new files.

## Decisions
- **Python stderr surfacing**: each timestamped stderr line from the bridge is
  logged via `ILogger` at **Information** level with a `[ytm-bridge]` prefix
  when timing is enabled, so timings appear in normal logs without Debug.
- **Timestamp format**: each line shows **both** ISO-8601 wall-clock and
  `Δ+xxxms` elapsed since the previous step.
- **C# instrumentation**: all three services (Bridge, Metadata, Download) get
  `Stopwatch`-based Info logs when the flag is on.
- **Disable flag**: `YouTubeMusic__VerboseTiming` (bool, default `true`) on
  `YouTubeMusicSettings`, passed to Python as env var `YT_MUSIC_VERBOSE_TIMING`
  so one config silences both sides.

## Tasks

### 1. Settings flag
- Add to `YouTubeMusicSettings`:
  ```csharp
  /// <summary>Enable detailed timestamped timing logs for search/download (default true).</summary>
  public bool VerboseTiming { get; set; } = true;
  ```

### 2. Python bridge (`youtube-music-bridge.py`)
- Add module-level timing helper near top (after imports):
  ```python
  _TIMING = os.environ.get("YT_MUSIC_VERBOSE_TIMING", "1") not in ("0", "false", "False")
  _T0 = time.time()
  _T_PREV = time.time()
  def _t(label: str):
      global _T_PREV
      if not _TIMING:
          return
      now = time.time()
      from datetime import datetime
      iso = datetime.now().isoformat(timespec="milliseconds")
      delta = int((now - _T_PREV) * 1000)
      _T_PREV = now
      print(f"[ytm-timing] {iso} Δ+{delta}ms {label}", file=sys.stderr, flush=True)
  ```
- Call `_t("script start")` at top of `main()` (before auto-update).
- `_t("imports done")` after the ytmusicapi/yt-dlp/requests import block.
- `_t("auto-update check done")` after `_maybe_auto_update_packages()`.
- In `_create_ytmusic_instance` / `get_ytmusic`: `_t("auth: ytmusic instance created")`.
- In `_cached_search`: `_t("cache hit: <name>")` or `_t("cache miss: <name>")`,
  then `_t("ytm.search done: <name>")` after the searcher returns.
- `cmd_get_song`: `_t("get_song api done")`, `_t("search fallback done")`,
  `_t("watch_playlist fallback done")`.
- `cmd_get_album` / `cmd_get_artist` / `cmd_get_artist_albums`: `_t("ytm.get_* done")`.
- `cmd_download_track`:
  - `_t("yt-dlp attempt start: <client>")` and `_t("yt-dlp attempt done: <client>")`
    per client in `_download_track_ytdlp`.
  - `_t("ffmpeg conversion done")` after `_convert_webm_to_m4a`.
  - `_t("ytmusicapi download start/done")` in `_download_track_ytmusicapi`.
  - `_t("file written <size> bytes")` after download write.
- `cmd_get_stream_url`: `_t("ytmusicapi stream-url done")`, `_t("yt-dlp stream-url done")`.
- `cmd_check_auth`: `_t("check-auth done")`.
- Gate existing `[yt-dlp]`/`[ytmusicapi]`/`[ffmpeg]`/`[auto-update]` prints behind
  `_TIMING` where they are purely informational (keep error prints unconditional).

### 3. C# `YouTubeMusicBridgeService`
- Inject `IOptions<YouTubeMusicSettings>` is already present (`_settings`).
- In `CallBridgeAsync<T>(command, timeout, args)`:
  - Start a `Stopwatch` at method entry.
  - Log Info (when `_settings.VerboseTiming`): `[ytm-bridge] cmd={command} start`.
  - Time: script path resolve, process start, output drain (await), parse attempt.
  - After completion: `[ytm-bridge] cmd={command} done total={ms}ms`.
- Stream stderr: in `ErrorDataReceived`, when `e.Data != null` and it starts with
  `[ytm-timing]` (or always when `VerboseTiming`), log at **Information** with
  `[ytm-bridge] ` prefix; otherwise keep current Debug behavior.
  - Note: line-by-line logging requires buffering per-line (the current handler
    appends to a StringBuilder; add a per-line Info log before appending when
    `VerboseTiming` is on).
- Set `startInfo.EnvironmentVariables["YT_MUSIC_VERBOSE_TIMING"]` to
  `"1"` or `"0"` based on `_settings.VerboseTiming`.

### 4. C# `YouTubeMusicMetadataService`
- Already has `_settings` (SubsonicSettings) — needs `YouTubeMusicSettings` for
  the flag. Add `IOptions<YouTubeMusicSettings>` to constructor and store
  `_ytSettings`.
- In each `SearchSongsAsync` / `SearchAlbumsAsync` / `SearchArtistsAsync` /
  `SearchAllAsync` / `GetSongAsync` / `GetAlbumAsync` / `GetArtistAsync` /
  `GetArtistAlbumsAsync`:
  - `Stopwatch` around the `_bridge.*` call and a separate measurement around the
    `.Select(Map...).ToList()` mapping.
  - Log Info when `_ytSettings.VerboseTiming`:
    `[ytm-meta] SearchSongs query={q} bridge={ms}ms map={ms}ms count={n}`.

### 5. C# `YouTubeMusicDownloadService`
- Already has `_settings` (YouTubeMusicSettings) — use `_settings.VerboseTiming`.
- In `DownloadTrackAsync`:
  - `Stopwatch` around: `_bridge.DownloadTrackFileAsync` call, file→MemoryStream
    copy, quality/extension resolution, cleanup (`finally`).
  - Log Info when flag on:
    `[ytm-dl] track={id} bridge={ms}ms read={ms}ms cleanup={ms}ms bytes={n}`.

### 6. StartupValidator (optional, minimal)
- In `ValidateAsync`, after the Quality check, write a status line showing
  `VerboseTiming = on/off`. No behavior change.

## Risks / notes
- Per-line stderr Info logging in `ErrorDataReceived` increases logger traffic;
  gated by `VerboseTiming` (default on, but easy to disable).
- Timing helper uses `datetime.now()` (local time) for ISO prefix — acceptable
  for diagnostics; Δ is monotonic-ish via `time.time()`. Good enough for
  profiling; could switch to `time.monotonic()` for Δ if desired.
- No changes to JSON response shapes, so C# deserialization and tests are
  unaffected. Existing `YouTubeMusicQualityTests` etc. untouched.

## Validation
1. With `YouTubeMusic__VerboseTiming` unset (default true):
   - Run a song search; confirm Info logs show `[ytm-bridge]` per-step Δ lines
     and `[ytm-meta]` bridge/map timings.
   - Run a track download; confirm `[ytm-dl]` bridge/read/cleanup timings and
     Python `[ytm-timing]` lines for yt-dlp attempts + ffmpeg.
2. Set `YouTubeMusic__VerboseTiming=false` (and restart): confirm no Info timing
   logs on either side; bridge stderr falls back to Debug.
3. `dotnet build` the C# project; run `dotnet test` to confirm no regressions
   (no model changes expected).
