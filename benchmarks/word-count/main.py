import time

def main() -> None:
    with open("benchmarks/data/word-count.txt", "r", encoding="utf-8") as f:
        text = f.read()

    t0 = time.perf_counter()
    counts: dict[str, int] = {}
    total = 0

    for line in text.split("\n"):
        for w in line.split(" "):
            if not w:
                continue
            total += 1
            counts[w] = counts.get(w, 0) + 1

    used: set[str] = set()
    keys = list(counts.keys())
    limit = min(10, len(keys))
    top_lines: list[str] = []

    for _ in range(limit):
        best_word = ""
        best_count = -1
        for k in keys:
            if k in used:
                continue
            c = counts[k]
            if c > best_count or (c == best_count and k < best_word):
                best_count = c
                best_word = k
        used.add(best_word)
        top_lines.append(f"{best_word} {best_count}")
    bench_ms = (time.perf_counter() - t0) * 1000.0

    print(total)
    print(len(counts))
    for line in top_lines:
        print(line)
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
