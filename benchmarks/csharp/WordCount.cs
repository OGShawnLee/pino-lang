using System;
using System.Collections.Generic;
using System.IO;

namespace PinoBenchmarks;

public static class WordCount {
  public static void Run(bool quick) {
    string text = File.ReadAllText("benchmarks/data/word-count.txt");

    var counts = new Dictionary<string, int>();
    int total = 0;

    string[] lines = text.Split('\n');
    foreach (string line in lines) {
      string[] words = line.Split(' ');
      foreach (string w in words) {
        if (w.Length == 0) {
          continue;
        }
        total++;
        if (counts.TryGetValue(w, out int c)) {
          counts[w] = c + 1;
        } else {
          counts[w] = 1;
        }
      }
    }

    Console.WriteLine(total);
    Console.WriteLine(counts.Count);

    var used = new HashSet<string>();
    var keys = new List<string>(counts.Keys);
    int limit = Math.Min(10, keys.Count);

    for (int iter = 0; iter < limit; iter++) {
      string bestWord = "";
      int bestCount = -1;
      foreach (string k in keys) {
        if (used.Contains(k)) {
          continue;
        }
        int c = counts[k];
        if (c > bestCount || (c == bestCount && string.CompareOrdinal(k, bestWord) < 0)) {
          bestCount = c;
          bestWord = k;
        }
      }
      used.Add(bestWord);
      Console.WriteLine($"{bestWord} {bestCount}");
    }
  }
}
