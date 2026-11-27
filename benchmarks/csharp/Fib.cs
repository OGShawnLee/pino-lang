using System;
using System.Diagnostics;
using System.Globalization;

namespace PinoBenchmarks;

public static class Fib {
  private static long Calc(long n) {
    if (n < 2) {
      return n;
    }
    return Calc(n - 1) + Calc(n - 2);
  }

  public static void Run(bool quick) {
    long n = quick ? 25 : 29;
    var sw = Stopwatch.StartNew();
    long ans = Calc(n);
    sw.Stop();
    Console.WriteLine(ans);
    Console.WriteLine($"PINO_BENCH_MS={sw.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}");
  }
}
