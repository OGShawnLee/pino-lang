using System.Runtime.InteropServices;

namespace Pino.Cli;

/// <summary>Enables ANSI escape sequence processing on stdout.</summary>
internal static partial class ConsoleMode {
  private const int StdOutputHandle = -11;
  private const uint EnableVirtualTerminalProcessing = 0x0004;

  /// <summary>
  /// <see langword="true"/> when stdout understands ANSI sequences: always off Windows, and on
  /// Windows only when virtual-terminal processing could be switched on for the console.
  /// </summary>
  public static bool TryEnableAnsi() {
    if (!OperatingSystem.IsWindows()) {
      return true;
    }

    try {
      nint handle = GetStdHandle(StdOutputHandle);
      if (handle == 0 || handle == -1) {
        return false;
      }

      if (!GetConsoleMode(handle, out uint mode)) {
        return false;
      }

      if ((mode & EnableVirtualTerminalProcessing) != 0) {
        return true;
      }

      return SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
    } catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) {
      return false;
    }
  }

  [LibraryImport("kernel32.dll", SetLastError = true)]
  private static partial nint GetStdHandle(int nStdHandle);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}
