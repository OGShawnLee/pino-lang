using System;

namespace PinoBenchmarks;

public static class StringBuild {
  public static void Run(bool quick) {
    int n = quick ? 1500 : 7500;

    string s = "";
    for (int i = 0; i < n; i++) {
      string piece = "w" + (i % 1000).ToString() + ",";
      s = s + piece;
    }

    string[] parts = s.Split(',');
    string joined = string.Join(":", parts);

    Console.WriteLine(s.Length);
    Console.WriteLine(parts.Length);
    Console.WriteLine(joined.Length);
  }
}
