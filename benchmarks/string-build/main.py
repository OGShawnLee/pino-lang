import os
import sys

def main() -> None:
    quick = ("--quick" in sys.argv[1:]) or os.path.exists("benchmarks/data/quick")
    n = 1500 if quick else 7500

    s = ""
    for i in range(n):
        piece = "w" + str(i % 1000) + ","
        s = s + piece

    parts = s.split(",")
    joined = ":".join(parts)

    print(len(s))
    print(len(parts))
    print(len(joined))

if __name__ == "__main__":
    main()
