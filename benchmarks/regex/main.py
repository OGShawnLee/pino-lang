import re
import time

def main() -> None:
    with open("benchmarks/data/regex.txt", "r", encoding="utf-8") as f:
        text = f.read()

    t0 = time.perf_counter()
    email_re = re.compile(r"\w+@\w+\.com")
    ident_re = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")

    emails = email_re.findall(text)
    idents = ident_re.findall(text)
    email_count = len(emails)
    ident_count = len(idents)
    bench_ms = (time.perf_counter() - t0) * 1000.0

    print(email_count)
    print(ident_count)
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
