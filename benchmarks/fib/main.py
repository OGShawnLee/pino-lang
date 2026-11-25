import os
import sys

def fib(n: int) -> int:
    if n < 2:
        return n
    return fib(n - 1) + fib(n - 2)

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    n = 25 if quick else 29
    print(fib(n))

if __name__ == "__main__":
    main()
