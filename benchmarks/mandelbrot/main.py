import os
import sys

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    size = 150 if quick else 335
    max_iter = 50
    inv_size = 1.0 / size

    inside = 0
    for y in range(size):
        ci = 2.0 * y * inv_size - 1.0
        for x in range(size):
            cr = 2.0 * x * inv_size - 1.5
            zr = 0.0
            zi = 0.0
            escaped = False
            for _ in range(max_iter):
                tr = zr * zr - zi * zi + cr
                ti = 2.0 * zr * zi + ci
                zr = tr
                zi = ti
                if zr * zr + zi * zi > 4.0:
                    escaped = True
                    break
            if not escaped:
                inside += 1
    print(inside)

if __name__ == "__main__":
    main()
