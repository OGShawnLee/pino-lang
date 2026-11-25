# Pino Benchmarks

Cross-language benchmark suite comparing **Pino** (`pino-vm`, `pino-c` with TinyCC, and `pino-gcc` with `gcc`/`clang -O2`) against **Go**, **Rust**, **C# (.NET)**, **TypeScript (Bun)**, and **Python**.

Every benchmark uses the same algorithm, the same deterministic input data, and produces bit-for-bit identical output across all six languages.

## Benchmarks and Sizes

Sizes are chosen so the fastest compiled language takes at least ~30 ms on each full benchmark (except `hello`, which measures process startup cost) while every row—including Python and `pino-vm`—finishes under ~20 s per run and the full default suite completes in under ~10 minutes. `--quick` runs smaller inputs (< ~2 s per row, < ~2 minutes total) for fast verification.

| Benchmark | Measures | Full size | `--quick` size |
| :--- | :--- | :--- | :--- |
| `hello` | process startup cost | 1 line | 1 line |
| `fib` | recursive function calls | `fib(33)` | `fib(25)` |
| `binary-trees` | allocation & freeing (RC/GC/heap) | depth `14` | depth `9` |
| `nbody` | float math & structs | 5 bodies, `50,000` steps | `3,000` steps |
| `spectral-norm` | tight float loops | `n = 300` | `n = 80` |
| `mandelbrot` | float loops, integer output | `500 × 500`, 50 iters | `150 × 150`, 50 iters |
| `sort` | arrays, indexing, custom quicksort | `100,000` ints | `5,000` ints |
| `word-count` | hashing, strings, maps | ~1 MB text (`benchmarks/data/word-count.txt`) | ~50 KB text |
| `string-build` | string `+` append, split, join | `25,000` pieces | `1,500` pieces |
| `regex` | regex engine `find_all` (2 patterns) | ~2 MB text (`benchmarks/data/regex.txt`) | ~50 KB text |
| `lexer` | realistic mixed workload (Pino-like lexer) | ~400 KB source (`benchmarks/data/lexer.txt`) | ~20 KB source |

## Deterministic Data (LCG)

Every language that generates numbers (`sort`) and the runner that generates shared text inputs (`word-count`, `regex`, `lexer`) uses the exact same 31-bit Linear Congruential Generator, which stays well within IEEE-754 exact integer range (`< 2^53`) and signed 64-bit integer range:

```text
SEED = 123456789
A    = 1664525
C    = 1013904223
M    = 2147483648   (2^31)

next(state) = (state * A + C) % M
```

Text inputs are generated once per run mode into `benchmarks/data/` (gitignored), and every language reads the exact same file.

## Fairness Rules

1. **One algorithm per benchmark**, translated plainly and idiomatically in each language. No language gets a specialized or hand-unrolled variant.
   - `sort` implements the same in-place quicksort (median-of-three pivot + Hoare partition) in every language — never a standard library sort.
   - `nbody` and `spectral-norm` use the same Newton-Raphson `sqrt` helper (16 iterations) in every language because Pino does not yet expose a built-in `sqrt`.
   - `string-build` literally appends with `+` / `+=` in every language to measure string-concatenation performance as written.
2. **Single-threaded**, no SIMD intrinsics, no `unsafe` blocks, no manual memory pools or allocator overrides.
3. **Standard library only**, with one documented exception:
   - **Rust** uses the [`regex`](https://crates.io/crates/regex) crate for the `regex` benchmark, as Rust's `std` does not include a regex engine and `regex` is the standard choice.
   - **C#** uses `System.Text.RegularExpressions.Regex` with default options.
4. **Identical output**: every program prints only integers or plain strings (floats are printed as scaled rounded integers, e.g. `round(energy * 1e9)`). Output is verified byte-for-byte against `benchmarks/<name>/expected.txt`.
5. **No compiler workarounds**: Pino is measured on `main` as-is. If a language takes longer than the timeout (default 60 s per run), the runner marks it `TIMEOUT`.

## How to Run

Requires Python 3 (standard library only). Any language whose toolchain is not on `PATH` is skipped with a note.

```bash
# Quick smoke test across all benchmarks and languages
python benchmarks/run.py --quick

# Full benchmark suite (default 5 timed runs + 1 warm-up per cell)
python benchmarks/run.py

# Filter benchmarks, languages, run count, or timeout
python benchmarks/run.py --only fib,nbody --lang pino-c,pino-gcc,go,rust --runs 3
python benchmarks/run.py --quick --only fib --lang go,pino-vm --runs 2
python benchmarks/run.py --timeout 60
```

### Languages and Build Commands

Build and compile time is measured separately from execution time:

- **`pino-vm`**: `pino run <file>` (Pino IR VM, after building `Pino.Cli` once in Release)
- **`pino-c`**: `pino build -o <out> <file>` (native C99 via bundled TinyCC on Windows)
- **`pino-gcc`**: `pino build --release -o <out> <file>` (native C99 via `gcc`/`clang -O2`)
- **`go`**: `go build -o <out> <file>`
- **`rust`**: `cargo build --release` (single Cargo workspace in `benchmarks/rust/`)
- **`csharp`**: `dotnet build -c Release` (single .NET project in `benchmarks/csharp/`, invoked as the built executable)
- **`bun`**: `bun <file>.ts`
- **`python`**: `python -I <file>.py`

## Reading the Results

Each run writes two files to `benchmarks/results/`:

- `<YYYY-MM-DD>-<os>-<arch>.json`: machine details (CPU model, logical cores, RAM, OS), toolchain versions, Pino git commit, and raw timings (`median_ms`, `min_ms`, `max_ms`, `build_ms`, status).
- `<YYYY-MM-DD>-<os>-<arch>.md`: summary tables with median execution time in **ms**, a ratio column **relative to Rust** (`1.00x` = same speed as Rust; higher = slower), and a separate build/compile time table.

Check the `hello` row first to see each runtime's baseline process startup cost (especially for `pino-vm`, `bun`, `python`, and `csharp`).
