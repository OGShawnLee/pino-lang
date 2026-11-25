using System;

namespace PinoBenchmarks;

public static class Mandelbrot {
  public static void Run(bool quick) {
    int size = quick ? 150 : 335;
    const int maxIter = 50;
    double invSize = 1.0 / size;

    long inside = 0;
    for (int y = 0; y < size; y++) {
      double ci = 2.0 * y * invSize - 1.0;
      for (int x = 0; x < size; x++) {
        double cr = 2.0 * x * invSize - 1.5;
        double zr = 0.0;
        double zi = 0.0;
        bool escaped = false;
        for (int iter = 0; iter < maxIter; iter++) {
          double tr = zr * zr - zi * zi + cr;
          double ti = 2.0 * zr * zi + ci;
          zr = tr;
          zi = ti;
          if (zr * zr + zi * zi > 4.0) {
            escaped = true;
            break;
          }
        }
        if (!escaped) {
          inside++;
        }
      }
    }
    Console.WriteLine(inside);
  }
}
