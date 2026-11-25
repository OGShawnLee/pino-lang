using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PinoBenchmarks;

public static class RegexBench {
  public static void Run(bool quick) {
    string text = File.ReadAllText("benchmarks/data/regex.txt");

    var emailRe = new Regex(@"\w+@\w+\.com");
    var identRe = new Regex(@"[A-Za-z_][A-Za-z0-9_]*");

    int emails = emailRe.Matches(text).Count;
    int idents = identRe.Matches(text).Count;

    Console.WriteLine(emails);
    Console.WriteLine(idents);
  }
}
