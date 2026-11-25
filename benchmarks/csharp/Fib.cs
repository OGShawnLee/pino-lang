using System;

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
    Console.WriteLine(Calc(n));
  }
}
