namespace Moonwake.Models;

internal sealed record InjectionResult(bool Success, int ExitCode, string Message)
{
    public static InjectionResult Refused(string message) => new(false, -1, message);
}
