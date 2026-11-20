namespace Pino.Cli;

/// <summary>
/// Everything <see cref="CliApp"/> needs to know about the process it runs in, so tests can
/// supply their own values instead of touching the real console or environment.
/// </summary>
/// <param name="GetEnvironmentVariable">Reads an environment variable; <see langword="null"/> when unset.</param>
/// <param name="IsOutputRedirected">Whether stdout is redirected (a pipe or a file).</param>
/// <param name="SupportsAnsi">Whether stdout understands ANSI escape sequences.</param>
/// <param name="Notes">Test override for the <c>= Pino:</c> notes; <see langword="null"/> means <c>PinoVoice.Notes</c>.</param>
public sealed record CliEnvironment(
    Func<string, string?> GetEnvironmentVariable,
    bool IsOutputRedirected,
    bool SupportsAnsi,
    IReadOnlyDictionary<string, string>? Notes = null);
