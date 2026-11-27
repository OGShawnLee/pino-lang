# Pino Benchmarks

Cross-language benchmark suite comparing **Pino** (`pino-vm`, `pino-c` with TinyCC, and `pino-gcc` with `gcc`/`clang -O3 -flto`) against **Go**, **Rust**, **C# (.NET)**, **TypeScript (Bun)**, and **Python**.

Every benchmark uses the same algorithm, the same deterministic input data, and produces bit-for-bit identical output across all six languages.

## Benchmarks and Sizes

Each benchmark's full size is about **5× the work** of its `--quick` size so the full suite runs quickly on any machine. `--quick` runs smaller inputs for fast verification.

| Benchmark | Measures | Full size | `--quick` size |
| :--- | :--- | :--- | :--- |
| `hello` | process startup cost | 1 line | 1 line |
| `fib` | recursive function calls | `fib(29)` | `fib(25)` |
| `binary-trees` | allocation & freeing (RC/GC/heap) | depth `12` | depth `9` |
| `nbody` | float math & structs | 5 bodies, `15,000` steps | `3,000` steps |
| `spectral-norm` | tight float loops | `n = 180` | `n = 80` |
| `mandelbrot` | float loops, integer output | `335 × 335`, 50 iters | `150 × 150`, 50 iters |
| `sort` | arrays, indexing, custom quicksort | `25,000` ints | `5,000` ints |
| `word-count` | hashing, strings, maps | ~250 KB text (`benchmarks/data/word-count.txt`) | ~50 KB text |
| `string-build` | string `+` append, split, join | `7,500` pieces | `1,500` pieces |
| `regex` | regex engine `find_all` (2 patterns) | ~250 KB text (`benchmarks/data/regex.txt`) | ~50 KB text |
| `lexer` | realistic mixed workload (Pino-like lexer) | ~100 KB source (`benchmarks/data/lexer.txt`) | ~20 KB source |

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
python benchmarks/run.py --quick --stats --lang pino-gcc,rust,go,python
python benchmarks/run.py --timeout 60
```

### Languages and Build Commands

Build and compile time is measured separately from execution time:

- **`pino-vm`**: `pino run <file>` (Pino IR VM, after publishing `Pino.Cli` with NativeAOT by default, or `dotnet build -c Release` with `--no-aot`)
- **`pino-c`**: `pino build -o <out> <file>` (native C99 via bundled TinyCC on Windows)
- **`pino-gcc`**: `pino build --release -o <out> <file>` (native C99 via `gcc`/`clang -O3 -flto`)
- **`go`**: `go build -o <out> <file>`
- **`rust`**: `cargo build --release` (single Cargo workspace in `benchmarks/rust/`)
- **`csharp`**: `dotnet build -c Release` (single .NET project in `benchmarks/csharp/`, invoked as the built executable)
- **`bun`**: `bun <file>.ts`
- **`python`**: `python -I <file>.py`

## How to Read the Results

Each run writes two files to `benchmarks/results/`:

- `<YYYY-MM-DD>-<os>-<arch>.json`: machine details (CPU model, logical cores, RAM, OS), toolchain versions, Pino git commit, raw timings (`median_ms`, `min_ms`, `max_ms`, `samples_ms`, plus `work_median_ms`, `work_min_ms`, `work_max_ms`, `work_samples_ms` for compute benchmarks), `peak_rss_mb`, `builds[lang]["binary_kb"]`, `build_ms`, status, and optional `pino_stats` (`--stats`).
- `<YYYY-MM-DD>-<os>-<arch>.md`: summary tables with wall-clock `## Execution Time (ms)`, in-process `## In-Process Work Time (ms)` (parsed from `PINO_BENCH_MS=<ms>`), ratios **relative to Rust**, `## Build / Compile Time (ms)`, `## Peak Memory (RSS, MB)`, `## Binary Size (KB)`, and (with `--stats`) `## Runtime Counters (-DPINO_STATS, pino-gcc)` (`allocs`, `reallocs`, `frees`, `peak_live_kb`, `rc_inc`, `rc_dec`).

**Compare against the `hello` row:** because full sizes are kept compact (~5× the `--quick` sizes) so the suite runs well on any machine, the fastest compiled languages (`rust`, `go`, `pino-gcc`, `pino-c`) will land near their process startup time (the `hello` row) on some wall-clock rows, and runtimes with higher startup cost (`pino-vm`, `csharp`, `python`, `bun`) include that baseline in `## Execution Time (ms)`. The `## In-Process Work Time (ms)` table isolates steady-state compute throughput directly.
