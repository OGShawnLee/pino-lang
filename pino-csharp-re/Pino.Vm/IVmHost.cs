using System.Text;

namespace Pino.Vm;

/// <summary>The failure of a host file operation, mapped by the VM to the prelude <c>IOError</c> union.</summary>
public enum VmFileErrorKind {
  NotFound,
  PermissionDenied,
  AlreadyExists,
  Other,
}

public readonly record struct VmFileError(VmFileErrorKind Kind, string Message);

/// <summary>File-system access for <c>read_file</c>, <c>write_file</c> and <c>file_exists</c>.</summary>
public interface IVmFileSystem {
  bool TryReadAllText(string path, out string content, out VmFileError error);

  bool TryWriteAllText(string path, string content, out VmFileError error);

  bool Exists(string path);
}

/// <summary>
/// Everything the VM needs from the outside world. The VM core never touches <see cref="Console"/>,
/// <see cref="File"/> or the clock directly, so the same VM runs under the CLI, tests and a WASM host.
/// </summary>
public interface IVmHost {
  TextWriter Out { get; }

  TextWriter Err { get; }

  TextReader In { get; }

  IVmFileSystem Fs { get; }

  /// <summary>Milliseconds since the Unix epoch on every platform (<c>BUILTIN-7</c>).</summary>
  double ClockMs();

  /// <summary>Returns current UTC time as fractional milliseconds since the Unix epoch (<c>BUILTIN-7</c>).</summary>
  public static double UtcNowMs() =>
      (DateTimeOffset.UtcNow.Ticks - DateTimeOffset.UnixEpoch.Ticks) / (double) TimeSpan.TicksPerMillisecond;

  Random Random { get; }

  void Sleep(int milliseconds);

  void Clear();
}

/// <summary>A file system that has no files and refuses every write.</summary>
public sealed class NullVmFileSystem : IVmFileSystem {
  public static readonly NullVmFileSystem Instance = new();

  public bool TryReadAllText(string path, out string content, out VmFileError error) {
    content = string.Empty;
    error = new VmFileError(VmFileErrorKind.NotFound, path);
    return false;
  }

  public bool TryWriteAllText(string path, string content, out VmFileError error) {
    error = new VmFileError(VmFileErrorKind.PermissionDenied, path);
    return false;
  }

  public bool Exists(string path) => false;
}

/// <summary>An in-memory file system, used by tests.</summary>
public sealed class InMemoryVmFileSystem : IVmFileSystem {
  public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

  public bool TryReadAllText(string path, out string content, out VmFileError error) {
    if (Files.TryGetValue(path, out string? text)) {
      content = text;
      error = default;
      return true;
    }

    content = string.Empty;
    error = new VmFileError(VmFileErrorKind.NotFound, path);
    return false;
  }

  public bool TryWriteAllText(string path, string content, out VmFileError error) {
    Files[path] = content;
    error = default;
    return true;
  }

  public bool Exists(string path) => Files.ContainsKey(path);
}

/// <summary>
/// A host that captures output in memory and reads from a fixed input string; the clock, random numbers and sleep are
/// deterministic. Used by tests and by embedders that want to collect output.
/// </summary>
public sealed class BufferedVmHost : IVmHost {
  private readonly StringBuilder _out = new();
  private readonly StringBuilder _err = new();

  public BufferedVmHost(string input = "", IVmFileSystem? fs = null, int seed = 12345) {
    Out = new StringWriter(_out) { NewLine = "\n" };
    Err = new StringWriter(_err) { NewLine = "\n" };
    In = new StringReader(input);
    Fs = fs ?? NullVmFileSystem.Instance;
    Random = new Random(seed);
  }

  public TextWriter Out { get; }

  public TextWriter Err { get; }

  public TextReader In { get; }

  public IVmFileSystem Fs { get; }

  public Random Random { get; }

  public double FixedClockMs { get; set; } = 1_700_000_000_000.0;

  public int SleepCalls { get; private set; }

  public int ClearCalls { get; private set; }

  public string StdOut => _out.ToString();

  public string StdErr => _err.ToString();

  public double ClockMs() => FixedClockMs;

  public void Sleep(int milliseconds) => SleepCalls++;

  public void Clear() => ClearCalls++;
}
