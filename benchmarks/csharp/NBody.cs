using System;

namespace PinoBenchmarks;

public static class NBody {
  private struct Body {
    public double X, Y, Z;
    public double Vx, Vy, Vz;
    public double Mass;
  }

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

  private static void Advance(Body[] bodies, double dt) {
    int n = bodies.Length;
    for (int i = 0; i < n; i++) {
      for (int j = i + 1; j < n; j++) {
        double dx = bodies[i].X - bodies[j].X;
        double dy = bodies[i].Y - bodies[j].Y;
        double dz = bodies[i].Z - bodies[j].Z;
        double dsq = dx * dx + dy * dy + dz * dz;
        double dist = MySqrt(dsq);
        double mag = dt / (dsq * dist);

        double mi = bodies[i].Mass;
        double mj = bodies[j].Mass;

        bodies[i].Vx -= dx * mj * mag;
        bodies[i].Vy -= dy * mj * mag;
        bodies[i].Vz -= dz * mj * mag;

        bodies[j].Vx += dx * mi * mag;
        bodies[j].Vy += dy * mi * mag;
        bodies[j].Vz += dz * mi * mag;
      }
    }
    for (int k = 0; k < n; k++) {
      bodies[k].X += dt * bodies[k].Vx;
      bodies[k].Y += dt * bodies[k].Vy;
      bodies[k].Z += dt * bodies[k].Vz;
    }
  }

  private static double Energy(Body[] bodies) {
    double e = 0.0;
    int n = bodies.Length;
    for (int i = 0; i < n; i++) {
      Body bi = bodies[i];
      e += 0.5 * bi.Mass * (bi.Vx * bi.Vx + bi.Vy * bi.Vy + bi.Vz * bi.Vz);
      for (int j = i + 1; j < n; j++) {
        Body bj = bodies[j];
        double dx = bi.X - bj.X;
        double dy = bi.Y - bj.Y;
        double dz = bi.Z - bj.Z;
        double dist = MySqrt(dx * dx + dy * dy + dz * dz);
        e -= (bi.Mass * bj.Mass) / dist;
      }
    }
    return e;
  }

  public static void Run(bool quick) {
    int steps = quick ? 3000 : 50000;

    const double pi = 3.141592653589793;
    const double solarMass = 4.0 * pi * pi;
    const double daysPerYear = 365.24;

    Body[] bodies = [
      new Body { X = 0.0, Y = 0.0, Z = 0.0, Vx = 0.0, Vy = 0.0, Vz = 0.0, Mass = solarMass },
      new Body {
        X = 4.84143144246472090, Y = -1.16032004402742839, Z = -0.103622044471123109,
        Vx = 0.00166007664274403694 * daysPerYear, Vy = 0.00769901118419740425 * daysPerYear, Vz = -0.0000690460016972063023 * daysPerYear,
        Mass = 0.000954791938424326609 * solarMass,
      },
      new Body {
        X = 8.34336671824457987, Y = 4.12479856412430479, Z = -0.403523417114321381,
        Vx = -0.00276742510726862411 * daysPerYear, Vy = 0.00499852801234917238 * daysPerYear, Vz = 0.0000230417297573763929 * daysPerYear,
        Mass = 0.000285885980666130812 * solarMass,
      },
      new Body {
        X = 12.8943695621391310, Y = -15.1111514016986312, Z = -0.223307578892655734,
        Vx = 0.00296460137564761618 * daysPerYear, Vy = 0.00237847173959480950 * daysPerYear, Vz = -0.0000296589568540237556 * daysPerYear,
        Mass = 0.0000436624404335156298 * solarMass,
      },
      new Body {
        X = 15.3796971148509165, Y = -25.9193146099879641, Z = 0.179258772950371181,
        Vx = 0.00268067772490389322 * daysPerYear, Vy = 0.00162824170038242295 * daysPerYear, Vz = -0.0000951592254519715870 * daysPerYear,
        Mass = 0.0000515138902046611451 * solarMass,
      },
    ];

    double px = 0.0, py = 0.0, pz = 0.0;
    for (int i = 0; i < bodies.Length; i++) {
      px += bodies[i].Vx * bodies[i].Mass;
      py += bodies[i].Vy * bodies[i].Mass;
      pz += bodies[i].Vz * bodies[i].Mass;
    }
    bodies[0].Vx = -px / solarMass;
    bodies[0].Vy = -py / solarMass;
    bodies[0].Vz = -pz / solarMass;

    double e0 = Energy(bodies);
    for (int i = 0; i < steps; i++) {
      Advance(bodies, 0.01);
    }
    double e1 = Energy(bodies);

    Console.WriteLine(RoundInt(e0 * 1000000000.0));
    Console.WriteLine(RoundInt(e1 * 1000000000.0));
  }
}
