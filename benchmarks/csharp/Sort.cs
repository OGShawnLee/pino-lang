using System;
using System.Diagnostics;
using System.Globalization;

namespace PinoBenchmarks;

public static class Sort {
  private static void Quicksort(long[] arr, int lo, int hi) {
    if (lo >= hi) {
      return;
    }
    long pivot = arr[lo + (hi - lo) / 2];
    int i = lo;
    int j = hi;
    while (i <= j) {
      while (arr[i] < pivot) {
        i++;
      }
      while (arr[j] > pivot) {
        j--;
      }
      if (i <= j) {
        long tmp = arr[i];
        arr[i] = arr[j];
        arr[j] = tmp;
        i++;
        j--;
      }
    }
    if (lo < j) {
      Quicksort(arr, lo, j);
    }
    if (i < hi) {
      Quicksort(arr, i, hi);
    }
  }

  public static void Run(bool quick) {
    int n = quick ? 5000 : 25000;
    var sw = Stopwatch.StartNew();
    long state = 123456789;
    long[] arr = new long[n];
    for (int i = 0; i < n; i++) {
      state = (state * 1664525 + 1013904223) % 2147483648;
      arr[i] = state;
    }

    Quicksort(arr, 0, n - 1);

    long checksum = 0;
    for (int i = 0; i < n; i++) {
      checksum = (checksum + ((long)(i + 1) * (arr[i] % 1000003))) % 1000000007;
    }
    sw.Stop();

    Console.WriteLine(arr[0]);
    Console.WriteLine(arr[n / 2]);
    Console.WriteLine(arr[n - 1]);
    Console.WriteLine(checksum);
    Console.WriteLine($"PINO_BENCH_MS={sw.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}");
  }
}
