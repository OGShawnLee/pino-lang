using System.Runtime.InteropServices;

namespace Pino.Cli;

/// <summary>
/// Everything <see cref="CliApp"/> needs to know about the process it runs in, so tests can
/// supply their own values instead of touching the real console, filesystem layout, network, or clock.
/// </summary>
/// <param name="GetEnvironmentVariable">Reads an environment variable; <see langword="null"/> when unset.</param>
/// <param name="IsOutputRedirected">Whether stdout is redirected (a pipe or a file).</param>
/// <param name="SupportsAnsi">Whether stdout understands ANSI escape sequences.</param>
/// <param name="Notes">Test override for the <c>= Pino:</c> notes; <see langword="null"/> means <c>PinoVoice.Notes</c>.</param>
/// <param name="WorkingDirectory">Working directory override; <see langword="null"/> uses <see cref="Environment.CurrentDirectory"/>.</param>
/// <param name="ExecutablePath">Path to the running <c>pino</c> executable; <see langword="null"/> uses <see cref="Environment.ProcessPath"/>.</param>
/// <param name="HttpFetcher">Injectable HTTP fetcher for <c>update</c> and <c>play update</c>; <see langword="null"/> uses <see cref="DefaultPinoHttpFetcher"/>.</param>
/// <param name="WatchDriver">Injectable watch driver for <c>watch</c>; <see langword="null"/> uses <see cref="DefaultWatchDriver"/>.</param>
/// <param name="OsPlatform">OS platform override for <c>update</c> asset selection; <see langword="null"/> detects the current OS.</param>
/// <param name="ProcessArchitecture">CPU architecture override for <c>update</c> asset selection; <see langword="null"/> uses <see cref="RuntimeInformation.ProcessArchitecture"/>.</param>
public sealed record CliEnvironment(
    Func<string, string?> GetEnvironmentVariable,
    bool IsOutputRedirected,
    bool SupportsAnsi,
    IReadOnlyDictionary<string, string>? Notes = null,
    string? WorkingDirectory = null,
    string? ExecutablePath = null,
    IPinoHttpFetcher? HttpFetcher = null,
    IWatchDriver? WatchDriver = null,
    OSPlatform? OsPlatform = null,
    Architecture? ProcessArchitecture = null) {
  public string EffectiveWorkingDirectory =>
      string.IsNullOrEmpty(WorkingDirectory) ? Environment.CurrentDirectory : WorkingDirectory;

  public string? EffectiveExecutablePath =>
      string.IsNullOrEmpty(ExecutablePath) ? Environment.ProcessPath : ExecutablePath;

  public string EffectiveExecutableDirectory =>
      !string.IsNullOrEmpty(EffectiveExecutablePath)
          ? Path.GetDirectoryName(Path.GetFullPath(EffectiveExecutablePath)) ?? AppContext.BaseDirectory
          : AppContext.BaseDirectory;

  public OSPlatform EffectiveOsPlatform =>
      OsPlatform ?? (OperatingSystem.IsWindows()
          ? OSPlatform.Windows
          : OperatingSystem.IsMacOS()
              ? OSPlatform.OSX
              : OSPlatform.Linux);

  public Architecture EffectiveArchitecture =>
      ProcessArchitecture ?? RuntimeInformation.ProcessArchitecture;
}
