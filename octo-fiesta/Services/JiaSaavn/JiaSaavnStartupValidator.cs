using System.Text.Json;
using Microsoft.Extensions.Options;
using octo_fiesta.Models.Settings;
using octo_fiesta.Services.Validation;

namespace octo_fiesta.Services.JiaSaavn;

/// <summary>
/// Validates JiaSaavn service connectivity at startup (no auth needed)
/// </summary>
public class JiaSaavnStartupValidator : BaseStartupValidator
{
    private readonly JiaSaavnSettings _settings;

    public override string ServiceName => "JiaSaavn";

    public JiaSaavnStartupValidator(
        IOptions<JiaSaavnSettings> settings,
        HttpClient httpClient)
        : base(httpClient)
    {
        _settings = settings.Value;
    }

    public override async Task<ValidationResult> ValidateAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine();

        var quality = _settings.Quality ?? "320";

        WriteStatus("JiaSaavn Base URL", _settings.BaseUrl, ConsoleColor.Cyan);
        WriteStatus("JiaSaavn Quality", $"{quality} kbps", ConsoleColor.Cyan);

        try
        {
            var response = await _httpClient.GetAsync($"{_settings.BaseUrl}/api/songs?q=test", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                WriteStatus("JiaSaavn API", "REACHABLE", ConsoleColor.Green);
                WriteDetail("No authentication required - powered by SquidWTF JiaSaavn");
                return ValidationResult.Success("JiaSaavn validation completed");
            }
            else
            {
                WriteStatus("JiaSaavn API", $"HTTP {(int)response.StatusCode}", ConsoleColor.Yellow);
                WriteDetail("Service may be temporarily unavailable");
                return ValidationResult.Failure($"{response.StatusCode}", "JiaSaavn returned error code");
            }
        }
        catch (TaskCanceledException)
        {
            WriteStatus("JiaSaavn API", "TIMEOUT", ConsoleColor.Yellow);
            WriteDetail("Could not reach service within timeout period");
            return ValidationResult.Failure("-1", "JiaSaavn connection timeout");
        }
        catch (HttpRequestException ex)
        {
            WriteStatus("JiaSaavn API", "UNREACHABLE", ConsoleColor.Red);
            WriteDetail(ex.Message);
            return ValidationResult.Failure("-1", $"Cannot connect to JiaSaavn: {ex.Message}");
        }
        catch (Exception ex)
        {
            WriteStatus("JiaSaavn API", "ERROR", ConsoleColor.Red);
            WriteDetail(ex.Message);
            return ValidationResult.Failure("-1", $"Validation error: {ex.Message}");
        }
    }
}
