using System;
using System.Diagnostics;
using System.Globalization;

namespace PinoBenchmarks;

public static class SpectralNorm {
  private static double MySqrt(double x) {
    double g = x * 0.5 + 1.0;
    for (int i = 0; i < 16; i++) {
      g = 0.5 * (g + x / g);
    }
    return g;
  }

  private static long RoundInt(double v) {
    return v >= 0.0 ? (long)(v + 0.5) : (long)(v - 0.5);
  }

  private static double EvalA(int i, int j) {
    int ij = i + j;
    return 1.0 / (ij * (ij + 1) / 2 + i + 1);
  }

  private static void EvalATimesU(double[] u, double[] au, int n) {
    for (int i = 0; i < n; i++) {
      double sum = 0.0;
      for (int j = 0; j < n; j++) {
        sum += EvalA(i, j) * u[j];
      }
      au[i] = sum;
    }
  }

  private static void EvalAtTimesU(double[] u, double[] au, int n) {
    for (int i = 0; i < n; i++) {
      double sum = 0.0;
      for (int j = 0; j < n; j++) {
        sum += EvalA(j, i) * u[j];
      }
      au[i] = sum;
    }
  }

  private static void EvalAtATimesU(double[] u, double[] outArr, double[] tmp, int n) {
    EvalATimesU(u, tmp, n);
    EvalAtTimesU(tmp, outArr, n);
  }

  public static void Run(bool quick) {
    int n = quick ? 80 : 180;
    var sw = Stopwatch.StartNew();
    double[] u = new double[n];
    double[] v = new double[n];
    double[] tmp = new double[n];
    for (int i = 0; i < n; i++) {
      u[i] = 1.0;
    }

    for (int iter = 0; iter < 10; iter++) {
      EvalAtATimesU(u, v, tmp, n);
      EvalAtATimesU(v, u, tmp, n);
    }

    double vbv = 0.0;
    double vv = 0.0;
    for (int i = 0; i < n; i++) {
      vbv += u[i] * v[i];
      vv += v[i] * v[i];
    }

    double ans = MySqrt(vbv / vv);
    long outVal = RoundInt(ans * 1000000000.0);
    sw.Stop();
    Console.WriteLine(outVal);
    Console.WriteLine($"PINO_BENCH_MS={sw.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture)}");
  }
}
