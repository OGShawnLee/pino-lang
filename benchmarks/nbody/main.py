import os
import sys
import time

class Body:
    __slots__ = ("x", "y", "z", "vx", "vy", "vz", "mass")
    def __init__(self, x, y, z, vx, vy, vz, mass):
        self.x = x
        self.y = y
        self.z = z
        self.vx = vx
        self.vy = vy
        self.vz = vz
        self.mass = mass

def my_sqrt(x: float) -> float:
    g = x * 0.5 + 1.0
    for _ in range(16):
        g = 0.5 * (g + x / g)
    return g

def round_int(v: float) -> int:
    if v >= 0.0:
        return int(v + 0.5)
    return int(v - 0.5)

def advance(bodies, dt: float) -> None:
    n = len(bodies)
    for i in range(n):
        bi = bodies[i]
        for j in range(i + 1, n):
            bj = bodies[j]
            dx = bi.x - bj.x
            dy = bi.y - bj.y
            dz = bi.z - bj.z
            dsq = dx * dx + dy * dy + dz * dz
            dist = my_sqrt(dsq)
            mag = dt / (dsq * dist)

            mi = bi.mass
            mj = bj.mass

            bi.vx -= dx * mj * mag
            bi.vy -= dy * mj * mag
            bi.vz -= dz * mj * mag

            bj.vx += dx * mi * mag
            bj.vy += dy * mi * mag
            bj.vz += dz * mi * mag

    for k in range(n):
        bk = bodies[k]
        bk.x += dt * bk.vx
        bk.y += dt * bk.vy
        bk.z += dt * bk.vz

def energy(bodies) -> float:
    e = 0.0
    n = len(bodies)
    for i in range(n):
        bi = bodies[i]
        e += 0.5 * bi.mass * (bi.vx * bi.vx + bi.vy * bi.vy + bi.vz * bi.vz)
        for j in range(i + 1, n):
            bj = bodies[j]
            dx = bi.x - bj.x
            dy = bi.y - bj.y
            dz = bi.z - bj.z
            dist = my_sqrt(dx * dx + dy * dy + dz * dz)
            e -= (bi.mass * bj.mass) / dist
    return e

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    steps = 3000 if quick else 15000

    pi = 3.141592653589793
    solar_mass = 4.0 * pi * pi
    days_per_year = 365.24

    bodies = [
        Body(0.0, 0.0, 0.0, 0.0, 0.0, 0.0, solar_mass),
        Body(
            4.84143144246472090, -1.16032004402742839, -0.103622044471123109,
            0.00166007664274403694 * days_per_year, 0.00769901118419740425 * days_per_year, -0.0000690460016972063023 * days_per_year,
            0.000954791938424326609 * solar_mass,
        ),
        Body(
            8.34336671824457987, 4.12479856412430479, -0.403523417114321381,
            -0.00276742510726862411 * days_per_year, 0.00499852801234917238 * days_per_year, 0.0000230417297573763929 * days_per_year,
            0.000285885980666130812 * solar_mass,
        ),
        Body(
            12.8943695621391310, -15.1111514016986312, -0.223307578892655734,
            0.00296460137564761618 * days_per_year, 0.00237847173959480950 * days_per_year, -0.0000296589568540237556 * days_per_year,
            0.0000436624404335156298 * solar_mass,
        ),
        Body(
            15.3796971148509165, -25.9193146099879641, 0.179258772950371181,
            0.00268067772490389322 * days_per_year, 0.00162824170038242295 * days_per_year, -0.0000951592254519715870 * days_per_year,
            0.0000515138902046611451 * solar_mass,
        ),
    ]

    px = 0.0
    py = 0.0
    pz = 0.0
    for b in bodies:
        px += b.vx * b.mass
        py += b.vy * b.mass
        pz += b.vz * b.mass
    bodies[0].vx = -px / solar_mass
    bodies[0].vy = -py / solar_mass
    bodies[0].vz = -pz / solar_mass

    t0 = time.perf_counter()
    e0 = energy(bodies)
    for _ in range(steps):
        advance(bodies, 0.01)
    e1 = energy(bodies)
    bench_ms = (time.perf_counter() - t0) * 1000.0

    print(round_int(e0 * 1000000000.0))
    print(round_int(e1 * 1000000000.0))
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
