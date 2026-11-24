using System.Net.Http.Headers;

namespace Pino.Cli;

/// <summary>Result of one HTTP GET performed by <see cref="IPinoHttpFetcher"/>.</summary>
public sealed record PinoHttpResponse(int StatusCode, byte[] Bytes);

/// <summary>Injectable HTTP client used by <c>pino update</c> and <c>pino play update</c> so tests never touch the network.</summary>
public interface IPinoHttpFetcher {
  PinoHttpResponse Get(string url);
}

/// <summary>Default <see cref="IPinoHttpFetcher"/> backed by <see cref="HttpClient"/>.</summary>
public sealed class DefaultPinoHttpFetcher : IPinoHttpFetcher {
  public static readonly DefaultPinoHttpFetcher Instance = new();

  private static readonly HttpClient s_client = CreateClient();

  private static HttpClient CreateClient() {
    var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Pino-CLI", "0.4"));
    return client;
  }

  public PinoHttpResponse Get(string url) {
    using HttpResponseMessage response = s_client.GetAsync(url).GetAwaiter().GetResult();
    byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
    return new PinoHttpResponse((int) response.StatusCode, bytes);
  }
}

/// <summary>
/// Injectable file-watch loop used by <c>pino watch</c>. Production uses <see cref="FileSystemWatcher"/>
/// with a 200 ms debounce and Ctrl+C cancellation; tests inject a synchronous driver with no sleeps.
/// </summary>
public interface IWatchDriver {
  void Watch(string directory, Action<CancellationToken> onRun);
}

/// <summary>Production <see cref="IWatchDriver"/> using <see cref="FileSystemWatcher"/> and a 200 ms debounce.</summary>
public sealed class DefaultWatchDriver : IWatchDriver {
  public static readonly DefaultWatchDriver Instance = new();
  private const int DebounceMs = 200;

  public void Watch(string directory, Action<CancellationToken> onRun) {
    using var stopEvent = new ManualResetEventSlim(false);
    object runLock = new();
    CancellationTokenSource? currentRunCts = null;
    Timer? debounceTimer = null;

    void TriggerRun() {
      CancellationTokenSource nextCts;
      lock (runLock) {
        if (stopEvent.IsSet) {
          return;
        }

        currentRunCts?.Cancel();
        currentRunCts?.Dispose();
        currentRunCts = new CancellationTokenSource();
        nextCts = currentRunCts;
      }

      ThreadPool.QueueUserWorkItem(_ => {
        try {
          onRun(nextCts.Token);
        } catch (OperationCanceledException) {
          // Superseded by a newer save or Ctrl+C.
        }
      });
    }

    void ScheduleDebounced(object? sender, FileSystemEventArgs e) {
      lock (runLock) {
        if (stopEvent.IsSet) {
          return;
        }

        if (debounceTimer is null) {
          debounceTimer = new Timer(_ => TriggerRun(), null, DebounceMs, Timeout.Infinite);
        } else {
          debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }
      }
    }

    ConsoleCancelEventHandler cancelHandler = (_, e) => {
      e.Cancel = true;
      lock (runLock) {
        currentRunCts?.Cancel();
      }

      stopEvent.Set();
    };

    Console.CancelKeyPress += cancelHandler;
    try {
      using var watcher = new FileSystemWatcher(directory, "*.pino") {
        IncludeSubdirectories = true,
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
      };
      watcher.Changed += ScheduleDebounced;
      watcher.Created += ScheduleDebounced;
      watcher.Renamed += (s, e) => ScheduleDebounced(s, e);
      watcher.EnableRaisingEvents = true;

      TriggerRun();
      stopEvent.Wait();
    } finally {
      Console.CancelKeyPress -= cancelHandler;
      lock (runLock) {
        debounceTimer?.Dispose();
        currentRunCts?.Cancel();
        currentRunCts?.Dispose();
      }
    }
  }
}
