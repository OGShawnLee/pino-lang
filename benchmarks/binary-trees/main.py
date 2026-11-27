import os
import sys
import time

class Tree:
    __slots__ = ("left", "right")
    def __init__(self, left=None, right=None):
        self.left = left
        self.right = right

def make_tree(depth: int) -> Tree:
    if depth <= 0:
        return Tree()
    return Tree(make_tree(depth - 1), make_tree(depth - 1))

def check_tree(t: Tree) -> int:
    if t.left is None:
        return 1
    return 1 + check_tree(t.left) + check_tree(t.right)

def pow2(exp: int) -> int:
    r = 1
    for _ in range(exp):
        r *= 2
    return r

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    min_depth = 4
    max_depth = 9 if quick else 12
    stretch_depth = max_depth + 1

    t0 = time.perf_counter()
    stretch_check = check_tree(make_tree(stretch_depth))
    long_lived = make_tree(max_depth)

    total_check = stretch_check
    d = min_depth
    while d <= max_depth:
        iterations = pow2(max_depth - d + min_depth)
        check = 0
        for _ in range(iterations):
            check += check_tree(make_tree(d))
        total_check += check
        d += 2

    long_check = check_tree(long_lived)
    total_check += long_check
    bench_ms = (time.perf_counter() - t0) * 1000.0

    print(stretch_check)
    print(total_check)
    print(long_check)
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
