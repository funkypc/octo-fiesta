using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using octo_fiesta.Models.Settings;
using octo_fiesta.Models.YouTubeMusic;

namespace octo_fiesta.Services.YouTubeMusic;

/// <summary>
/// Manages the Python subprocess bridge for ytmusicapi.
/// Handles invoking the youtube-music-bridge.py script, parsing JSON responses,
/// and managing authentication state.
/// </summary>
public class YouTubeMusicBridgeService
{
    private readonly YouTubeMusicSettings _settings;
    private readonly ILogger<YouTubeMusicBridgeService> _logger;
    private readonly TimeSpan _processTimeout = TimeSpan.FromSeconds(60);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public YouTubeMusicBridgeService(
        IOptions<YouTubeMusicSettings> settings,
        ILogger<YouTubeMusicBridgeService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Calls the Python bridge with the given command and arguments.
    /// </summary>
    /// <typeparam name="T">Expected response type.</typeparam>
    /// <param name="command">Bridge command (e.g., "search-songs").</param>
    /// <param name="args">Command arguments.</param>
    /// <returns>Parsed response.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the bridge returns an error or fails to execute.</exception>
    public virtual async Task<T> CallBridgeAsync<T>(string command, params string[] args) where T : class
    {
        return await CallBridgeAsync<T>(command, _processTimeout, args);
    }

    public virtual async Task<T> CallBridgeAsync<T>(string command, TimeSpan timeout, params string[] args) where T : class
    {
        var allArgs = new List<string> { command };
        allArgs.AddRange(args);

        // Resolve script path relative to application base directory if relative
        var scriptPath = _settings.ScriptPath;
        if (!Path.IsPathRooted(scriptPath))
        {
            var baseDir = AppContext.BaseDirectory;
            scriptPath = Path.Combine(baseDir, scriptPath);
        }

        if (!File.Exists(scriptPath))
        {
            throw new InvalidOperationException(
                $"YouTube Music bridge script not found at '{scriptPath}'. " +
                "Ensure the script is deployed alongside the application or update the ScriptPath configuration.");
        }

        _logger.LogDebug("Calling bridge: {Script} {Args}", scriptPath, string.Join(" ", allArgs));

        var startInfo = new ProcessStartInfo
        {
            FileName = _settings.PythonPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in allArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        // Set environment for auth
        if (!string.IsNullOrEmpty(_settings.AuthCookie))
        {
            startInfo.EnvironmentVariables["YT_MUSIC_COOKIE"] = _settings.AuthCookie;
        }

        using var process = new Process { StartInfo = startInfo };
        var outputTcs = new TaskCompletionSource<string>();
        var errorTcs = new TaskCompletionSource<string>();

        var outputBuffer = new System.Text.StringBuilder();
        var errorBuffer = new System.Text.StringBuilder();

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuffer.AppendLine(e.Data);
            }
            else
            {
                outputTcs.TrySetResult(outputBuffer.ToString());
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                errorBuffer.AppendLine(e.Data);
            }
            else
            {
                errorTcs.TrySetResult(errorBuffer.ToString());
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var waitForOutput = Task.WhenAll(outputTcs.Task, errorTcs.Task);
        var completed = await Task.WhenAny(
            waitForOutput,
            Task.Delay(timeout)
        );

        if (completed != waitForOutput)
        {
            try { process.Kill(); } catch { /* ignored */ }
            throw new InvalidOperationException($"Bridge process timed out after {timeout.TotalSeconds}s for command: {command}");
        }

        var output = await outputTcs.Task;
        var error = await errorTcs.Task;

        await process.WaitForExitAsync();

        _logger.LogDebug("Bridge output: {Output}", output);
        if (!string.IsNullOrWhiteSpace(error))
        {
            _logger.LogDebug("Bridge stderr: {Error}", error);
        }

        if (process.ExitCode != 0)
        {
            _logger.LogError("Bridge process exited with code {ExitCode} for command {Command}. Stderr: {Stderr}",
                process.ExitCode, command, error);
            throw new InvalidOperationException(
                $"Bridge process failed with exit code {process.ExitCode}. Error: {error}");
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Bridge process returned empty output.");
        }

        // Try to parse the response as a BridgeResponse first
        try
        {
            var response = JsonSerializer.Deserialize<YouTubeMusicBridgeResponse<T>>(output.Trim(), _jsonOptions);
            if (response?.Ok == true && response.Result != null)
            {
                return response.Result;
            }
            if (response?.Ok == false && !string.IsNullOrEmpty(response.Error))
            {
                throw new InvalidOperationException($"Bridge error: {response.Error}");
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Failed to parse as BridgeResponse, trying direct deserialization");
        }

        // Fallback: try direct deserialization
        var result = JsonSerializer.Deserialize<T>(output.Trim(), _jsonOptions);
        if (result != null)
        {
            return result;
        }

        throw new InvalidOperationException("Failed to parse bridge response.");
    }

    /// <summary>
    /// Checks if the bridge is available (Python + ytmusicapi installed + auth works).
    /// </summary>
    public virtual async Task<bool> IsBridgeAvailableAsync()
    {
        try
        {
            var result = await CallBridgeAsync<YouTubeMusicAuthCheckResult>("check-auth");
            return result.Authenticated;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bridge availability check failed");
            return false;
        }
    }

    /// <summary>
    /// Searches for songs.
    /// </summary>
    public virtual Task<YouTubeMusicSongSearchResult> SearchSongsAsync(string query, int limit)
    {
        return CallBridgeAsync<YouTubeMusicSongSearchResult>("search-songs", query, limit.ToString());
    }

    public virtual Task<YouTubeMusicAlbumSearchResult> SearchAlbumsAsync(string query, int limit)
    {
        return CallBridgeAsync<YouTubeMusicAlbumSearchResult>("search-albums", query, limit.ToString());
    }

    public virtual Task<YouTubeMusicArtistSearchResult> SearchArtistsAsync(string query, int limit)
    {
        return CallBridgeAsync<YouTubeMusicArtistSearchResult>("search-artists", query, limit.ToString());
    }

    public virtual Task<YouTubeMusicCombinedSearchResult> SearchAllAsync(string query, int songLimit, int albumLimit, int artistLimit)
    {
        return CallBridgeAsync<YouTubeMusicCombinedSearchResult>("search-all", query, songLimit.ToString(), albumLimit.ToString(), artistLimit.ToString());
    }

    public virtual async Task<YouTubeMusicTrack?> GetSongAsync(string videoId)
    {
        try
        {
            var result = await CallBridgeAsync<YouTubeMusicSongResult>("get-song", videoId);
            return result.Song;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get song {VideoId}", videoId);
            return null;
        }
    }

    public virtual async Task<YouTubeMusicAlbum?> GetAlbumAsync(string browseId)
    {
        try
        {
            var result = await CallBridgeAsync<YouTubeMusicAlbumResult>("get-album", browseId);
            return result.Album;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get album {BrowseId}", browseId);
            return null;
        }
    }

    public virtual async Task<YouTubeMusicArtist?> GetArtistAsync(string browseId)
    {
        try
        {
            var result = await CallBridgeAsync<YouTubeMusicArtistResult>("get-artist", browseId);
            return result.Artist;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get artist {BrowseId}", browseId);
            return null;
        }
    }

    public virtual async Task<List<YouTubeMusicAlbum>> GetArtistAlbumsAsync(string browseId)
    {
        try
        {
            var result = await CallBridgeAsync<YouTubeMusicArtistAlbumsResult>("get-artist-albums", browseId);
            return result.Albums;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get artist albums {BrowseId}", browseId);
            return new List<YouTubeMusicAlbum>();
        }
    }

    public virtual async Task<YouTubeMusicStreamResult?> GetStreamUrlAsync(string videoId, string? quality = null)
    {
        try
        {
            return await CallBridgeAsync<YouTubeMusicStreamResult>("get-stream-url", videoId, quality ?? "FLAC");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get stream URL for {VideoId} (quality={Quality})", videoId, quality);
            return null;
        }
    }

    public virtual async Task<YouTubeMusicDownloadResult?> DownloadTrackFileAsync(string videoId, string quality, string outputDir)
    {
        try
        {
            var downloadTimeout = TimeSpan.FromMinutes(5);
            return await CallBridgeAsync<YouTubeMusicDownloadResult>(
                "download-track", downloadTimeout, videoId, quality, outputDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download track {VideoId} (quality={Quality})", videoId, quality);
            return null;
        }
    }

}
