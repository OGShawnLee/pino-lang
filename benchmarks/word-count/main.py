def main() -> None:
    with open("benchmarks/data/word-count.txt", "r", encoding="utf-8") as f:
        text = f.read()

    counts: dict[str, int] = {}
    total = 0

    for line in text.split("\n"):
        for w in line.split(" "):
            if not w:
                continue
            total += 1
            counts[w] = counts.get(w, 0) + 1

    print(total)
    print(len(counts))

    used: set[str] = set()
    keys = list(counts.keys())
    limit = min(10, len(keys))

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
        print(f"{best_word} {best_count}")

if __name__ == "__main__":
    main()
