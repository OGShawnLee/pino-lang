using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace PinoBenchmarks;

public static class RegexBench {
  public static void Run(bool quick) {
    string text = File.ReadAllText("benchmarks/data/regex.txt");

    var sw = Stopwatch.StartNew();
    var emailRe = new Regex(@"\w+@\w+\.com");
    var identRe = new Regex(@"[A-Za-z_][A-Za-z0-9_]*");

    int emails = emailRe.Matches(text).Count;
    int idents = identRe.Matches(text).Count;
    sw.Stop();

    Console.WriteLine(emails);
    Console.WriteLine(idents);
    Console.WriteLine($"PINO_BENCH_MS={sw.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}");
  }
}
