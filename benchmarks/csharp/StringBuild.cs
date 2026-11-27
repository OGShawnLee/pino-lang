using System;
using System.Diagnostics;
using System.Globalization;

namespace PinoBenchmarks;

public static class StringBuild {
  public static void Run(bool quick) {
    int n = quick ? 1500 : 7500;

    var sw = Stopwatch.StartNew();
    string s = "";
    for (int i = 0; i < n; i++) {
      string piece = "w" + (i % 1000).ToString() + ",";
      s = s + piece;
    }

    string[] parts = s.Split(',');
    string joined = string.Join(":", parts);
    sw.Stop();

    Console.WriteLine(s.Length);
    Console.WriteLine(parts.Length);
    Console.WriteLine(joined.Length);
    Console.WriteLine($"PINO_BENCH_MS={sw.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}");
  }
}
