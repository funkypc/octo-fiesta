using System.Diagnostics;
using Microsoft.Extensions.Options;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Validation;

namespace octo_fiesta.Services.YouTubeMusic;

/// <summary>
/// Startup validator for YouTube Music provider.
/// </summary>
public class YouTubeMusicStartupValidator : BaseStartupValidator
{
    private readonly YouTubeMusicSettings _settings;
    private readonly ILogger<YouTubeMusicStartupValidator> _logger;

    public YouTubeMusicStartupValidator(
        IOptions<YouTubeMusicSettings> settings,
        IHttpClientFactory httpClientFactory,
        ILogger<YouTubeMusicStartupValidator> logger)
        : base(httpClientFactory.CreateClient())
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public override string ServiceName => "YouTube Music";

    public override async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        // 1. Check Python path
        if (string.IsNullOrEmpty(_settings.PythonPath))
        {
            WriteStatus("PythonPath", "NOT CONFIGURED", ConsoleColor.Red);
            WriteDetail("Set the YouTubeMusic__PythonPath environment variable or config value");
            WriteFailure();
            return ValidationResult.NotConfigured("YouTubeMusic PythonPath not configured");
        }
        WriteStatus("PythonPath", _settings.PythonPath, ConsoleColor.Cyan);

        // 2. Verify Python is accessible
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _settings.PythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add("--version");
            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                WriteStatus("Python", "NOT FOUND", ConsoleColor.Red);
                WriteDetail($"Python executable returned exit code {process.ExitCode}");
                WriteFailure();
                return ValidationResult.NotConfigured("Python executable not found or not working");
            }
            var version = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(version))
            {
                version = await process.StandardError.ReadToEndAsync(cancellationToken);
            }
            WriteStatus("Python", version.Trim(), ConsoleColor.Cyan);
        }
        catch (Exception ex)
        {
            WriteStatus("Python", "ERROR", ConsoleColor.Red);
            WriteDetail($"Failed to execute Python: {ex.Message}");
            WriteFailure();
            return ValidationResult.NotConfigured("Failed to execute Python");
        }

        // 3. Check ytmusicapi is installed
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = _settings.PythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("import ytmusicapi; print(ytmusicapi.__version__)");
            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync(cancellationToken);
                WriteStatus("ytmusicapi", "NOT INSTALLED", ConsoleColor.Red);
                WriteDetail("Install ytmusicapi: pip install ytmusicapi");
                WriteDetail(error);
                WriteFailure();
                return ValidationResult.NotConfigured("ytmusicapi Python package not installed");
            }
            var version = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            WriteStatus("ytmusicapi", version.Trim(), ConsoleColor.Cyan);
        }
        catch (Exception ex)
        {
            WriteStatus("ytmusicapi", "ERROR", ConsoleColor.Red);
            WriteDetail($"Failed to check ytmusicapi: {ex.Message}");
            WriteFailure();
            return ValidationResult.NotConfigured("Failed to check ytmusicapi installation");
        }

        // 4. Check auth configuration
        if (string.IsNullOrEmpty(_settings.AuthCookie))
        {
            // OAuth might be configured via env var
            var oauthEnv = Environment.GetEnvironmentVariable("YTMUSIC_OAUTH_CREDENTIALS");
            if (string.IsNullOrEmpty(oauthEnv))
            {
                WriteStatus("Auth", "NOT CONFIGURED", ConsoleColor.Red);
                WriteDetail("Set YouTubeMusic__AuthCookie or YTMUSIC_OAUTH_CREDENTIALS environment variable");
                WriteDetail("Cookie: Export from browser after logging into music.youtube.com");
                WriteFailure();
                return ValidationResult.NotConfigured("YouTube Music authentication not configured");
            }
            WriteStatus("Auth", "OAuth credentials configured", ConsoleColor.Cyan);
        }
        else
        {
            WriteStatus("Auth", "Cookie configured", ConsoleColor.Cyan);
        }

        // 5. Validate quality setting
        if (!YouTubeMusicQuality.IsValid(_settings.Quality))
        {
            WriteStatus("Quality", "INVALID", ConsoleColor.Red);
            WriteDetail($"Quality option {_settings.Quality} is not valid");
            WriteDetail("Set the YouTubeMusic__Quality environment variable to one of the valid options:");
            WriteDetail(string.Join(", ", YouTubeMusicQuality.ValidQualities));
            WriteFailure();
            return ValidationResult.NotConfigured("Invalid Quality setting");
        }
        WriteStatus("Quality", _settings.Quality ?? "FLAC", ConsoleColor.Cyan);

        // 6. Test bridge auth by running a lightweight check
        try
        {
            // Resolve script path relative to application base directory if relative
            var scriptPath = _settings.ScriptPath;
            if (!Path.IsPathRooted(scriptPath))
            {
                var baseDir = AppContext.BaseDirectory;
                scriptPath = Path.Combine(baseDir, scriptPath);
            }

            if (!File.Exists(scriptPath))
            {
                WriteStatus("Auth Check", "SCRIPT NOT FOUND", ConsoleColor.Red);
                WriteDetail($"Bridge script not found at '{scriptPath}'");
                WriteFailure();
                return ValidationResult.NotConfigured("YouTube Music bridge script not found");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _settings.PythonPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("check-auth");
            if (!string.IsNullOrEmpty(_settings.AuthCookie))
            {
                startInfo.EnvironmentVariables["YT_MUSIC_COOKIE"] = _settings.AuthCookie;
            }

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                WriteStatus("Auth Check", "FAILED", ConsoleColor.Red);
                WriteDetail($"Bridge auth check failed: {error}");
                WriteFailure();
                return ValidationResult.Failure("Auth check failed", error);
            }

            // Parse the JSON response to check auth status
            // check-auth now returns {"authenticated": true/false, "searchWorks": true/false, "reason": "..."}
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(stdout);
                var root = json.RootElement;
                var resultOk = root.TryGetProperty("ok", out var okElem) && okElem.GetBoolean();
                if (resultOk && root.TryGetProperty("result", out var resultElem))
                {
                    var authenticated = resultElem.TryGetProperty("authenticated", out var authElem) && authElem.GetBoolean();
                    if (!authenticated)
                    {
                        var reason = resultElem.TryGetProperty("reason", out var reasonElem) ? reasonElem.GetString() : "unknown";
                        WriteStatus("Auth Check", "NO AUTH (search only)", ConsoleColor.Yellow);
                        WriteDetail($"Auth not configured or failed: {reason}");
                        WriteDetail("Search works, but downloads require authentication");
                    }
                    else
                    {
                        WriteStatus("Auth Check", "SUCCESS", ConsoleColor.Green);
                    }
                }
                else
                {
                    WriteStatus("Auth Check", "SUCCESS", ConsoleColor.Green);
                }
            }
            catch
            {
                WriteStatus("Auth Check", "SUCCESS", ConsoleColor.Green);
            }
        }
        catch (Exception ex)
        {
            var result = HandleException(ex, "Auth Check");
            WriteValidationResult("Auth Check", result);
            WriteFailure();
            return result;
        }

        WriteStatus("YouTube Music Service Validation", "SUCCESS", ConsoleColor.Green);
        WriteDetail("YouTube Music validation completed");
        return ValidationResult.Success("YouTube Music validation completed");
    }

    private void WriteFailure()
    {
        WriteStatus("YouTube Music Service Validation", "FAILED", ConsoleColor.Red);
    }
}
