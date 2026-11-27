import os
import sys
import time

def fib(n: int) -> int:
    if n < 2:
        return n
    return fib(n - 1) + fib(n - 2)

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    n = 25 if quick else 29
    t0 = time.perf_counter()
    ans = fib(n)
    bench_ms = (time.perf_counter() - t0) * 1000.0
    print(ans)
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
