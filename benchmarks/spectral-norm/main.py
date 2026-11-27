import os
import sys
import time

def my_sqrt(x: float) -> float:
    g = x * 0.5 + 1.0
    for _ in range(16):
        g = 0.5 * (g + x / g)
    return g

def round_int(v: float) -> int:
    if v >= 0.0:
        return int(v + 0.5)
    return int(v - 0.5)

def eval_a(i: int, j: int) -> float:
    ij = i + j
    return 1.0 / (ij * (ij + 1) // 2 + i + 1)

def eval_a_times_u(u, au, n: int) -> None:
    for i in range(n):
        s = 0.0
        for j in range(n):
            s += eval_a(i, j) * u[j]
        au[i] = s

def eval_at_times_u(u, au, n: int) -> None:
    for i in range(n):
        s = 0.0
        for j in range(n):
            s += eval_a(j, i) * u[j]
        au[i] = s

def eval_ata_times_u(u, out, tmp, n: int) -> None:
    eval_a_times_u(u, tmp, n)
    eval_at_times_u(tmp, out, n)

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    n = 80 if quick else 180

    t0 = time.perf_counter()
    u = [1.0] * n
    v = [0.0] * n
    tmp = [0.0] * n

    for _ in range(10):
        eval_ata_times_u(u, v, tmp, n)
        eval_ata_times_u(v, u, tmp, n)

    vbv = 0.0
    vv = 0.0
    for i in range(n):
        vbv += u[i] * v[i]
        vv += v[i] * v[i]

    ans = my_sqrt(vbv / vv)
    out_val = round_int(ans * 1000000000.0)
    bench_ms = (time.perf_counter() - t0) * 1000.0
    print(out_val)
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
