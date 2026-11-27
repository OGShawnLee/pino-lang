import os
import sys
import time

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    n = 1500 if quick else 7500

    t0 = time.perf_counter()
    s = ""
    for i in range(n):
        piece = "w" + str(i % 1000) + ","
        s = s + piece

    parts = s.split(",")
    joined = ":".join(parts)
    bench_ms = (time.perf_counter() - t0) * 1000.0

    print(len(s))
    print(len(parts))
    print(len(joined))
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
