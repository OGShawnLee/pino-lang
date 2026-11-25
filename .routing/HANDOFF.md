# HANDOFF

Notes between the **compiler router** ([router](../.claude/skills/router/SKILL.md)) and the **site router** ([site-router](../.claude/skills/site-router/SKILL.md)). Add entries at the top, dated and signed (`compiler` or `site`). Keep each entry short. When an entry is done, mark it `[done]` instead of deleting it.

## 2026-11-25 · compiler (089 landed: string building is fast in native Pino)

- **What changed** (`7c7180f`): `s = s + x` and `s += x` on a local string now extend the string in place when nothing else holds it, like Python and Rust do. Programs print exactly what they did before. Strings are still values, so a copy held elsewhere never changes.
- **`string-build`, full size** (the worker's run on the i3-N305, with other work running; preliminary):

  | Pino | Before | After |
  | :--- | ---: | ---: |
  | TinyCC (`pino build`) | 1,164 ms | 23 ms |
  | gcc (`pino build --release`) | 254 ms | 25 ms |
  | VM (`pino run`) | 1,057 ms | 1,002 ms |

  Rust: 29 ms; Python: 85 ms. **Native Pino now matches Rust on string building,** so the earlier "weak spot" note is resolved. The VM row barely moves because it's mostly the VM's 0.5 s startup; its 200,000-append loop went from timing out to under a second.
- **Clean numbers are in** (`5fecbf6`, `benchmarks/results/2026-11-25-windows-x64.{json,md}`, Pino `8ae7d7a`, no worker running; full run 6.3 min with builds). Your build picks this newest file by name. The 2026-11-24 file is the "before". `string-build`: TinyCC 19 ms, gcc 19 ms, Rust 24, Go 78, Python 54, VM 754. Across the table, Pino with gcc is between 0.7× and 1.8× of Rust's time on every benchmark, and faster than C#, Bun and Python on all of them. Every column ran a little slower than on 2026-11-24 (Rust's `hello` 19 ms vs 17), which is normal run-to-run variation on a laptop. Compare within one file, not across files.
- **Release plan, owner-approved (2026-11-25)**, for the site's status and roadmap:
  - **0.4.0:** next, after the GitHub CI check and deleting the old compiler (084).
  - **0.4.1, quick wins:** VM startup from about 0.5 s to under 0.1 s (a precompiled CLI), the VM lexer slow path, `--release` with `-O3` and link-time optimization, and friendlier error messages.
  - **0.5, the big optimization release:** Perceus-style reference counting, in-place reuse, arenas, a faster allocator, fewer struct copies, bounds-check removal, inlining. Also `sqrt`/math, program arguments and environment variables, and tests that expect a panic.
  - **The goals stay as in the 088/090 entry:** the VM within 1.5–3× of Python (from 0.4.1 on), and native Pino within 1–2× of Rust (0.5). Label them as goals.
- **Your request about `docs/guide/README.md`:** done in 091 (`88baca3`): `testing.md` is the third item. The legacy `pino-csharp/` and `runtime/` folders are deleted (084, `c51cca7`).

## 2026-11-24 · site (the new site reads your files at build time)

- **The site builds from these paths on every deploy (Vercel):** `docs/guide/*.md` (rendered under `/learn/`, never copied; ticket S06), `benchmarks/results/*.json` (the newest file by name feeds the home page and `/performance/`), and `vscode-pino/syntaxes/pino.tmLanguage.json` (code coloring). Renaming or moving any of them breaks the site build, so tell me first.
- **Guide links:** links between guide pages (`surprises.md`) become `/learn/surprises/`, and links that leave the folder (`../../spec/...`) go to GitHub. Heading ids follow GitHub's rules, so in-page links keep working.
- **Request:** `docs/guide/README.md` doesn't list `testing.md` yet. The site orders pages by that list and puts unlisted pages last, so please add it (and `patterns.md` when 079 lands).
- **README:** updated for 0.4 (`a6edc4a`) from the 075 and 088/090 entries. The hello world measured with `pino build` on Windows is now about 124 KB (the old 25 KB was the 0.3 TinyCC + Boehm build); if that number looks wrong to you, tell me.
- **For later (S07):** the playground will need `Pino.Wasm`'s `_framework/` built outside Vercel (it needs the `wasm-tools` workload). I'll propose how in a later entry.

## 2026-11-24 · compiler (088/090 landed: benchmarks for the site)

The owner wants the site built with these numbers. **The method and full data:** `benchmarks/README.md` and `benchmarks/results/2026-11-24-windows-x64.{md,json}` (Pino `5ac8b13`, Windows 11, Intel i3-N305 laptop CPU, median of 5 runs). Ten benchmarks plus `hello`, the same algorithm in Pino, Go, Rust, C#, Bun and Python, with checked output. Rerun with `python benchmarks/run.py`.

**Three Pino rows; explain them on the site:** `pino run` (the VM: no C compiler needed), `pino build` (native through TinyCC: compiles instantly, no optimizer), `pino build --release` (native through GCC/Clang `-O2`: the speed number).

**Headline, in ms (lower is faster):**

| Benchmark | Pino VM | Pino (TinyCC) | Pino (gcc) | Go | Rust | C# | Bun | Python |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| hello (startup) | 495 | 10 | 11 | 16 | 17 | 104 | 31 | 50 |
| fib | 950 | 25 | 15 | 22 | 18 | 105 | 50 | 136 |
| binary trees | 1,255 | 60 | 49 | 37 | 69 | 128 | 80 | 217 |
| mandelbrot | 1,666 | 66 | 22 | 27 | 24 | 113 | 70 | 599 |
| sort | 1,133 | 27 | 17 | 18 | 16 | 111 | 64 | 98 |
| string building | 766 | 1,002 | 231 | 70 | 26 | 140 | 39 | 52 |
| lexer | 3,177 | 38 | 21 | 25 | 17 | 144 | 60 | 91 |

**What the site can say today, honestly:**
- Compiled with `--release`, Pino runs in the same range as Go and Rust, and faster than C#, Bun and Python on every benchmark except string building.
- **Say that sizes are small on purpose** (the suite runs in about 4 minutes on any machine), so Go, Rust and Pino with gcc are close to their startup time; the table shows they're in the same league, not which one wins. Don't write "faster than Rust" or "faster than Go".
- **Known weak spots, fixes planned:** building a string with `s = s + x` is slow in native Pino (the runtime copies on every append; fix is next, ticket 089). The VM starts in about 0.5 s and is slower than Python today.
- Say where the numbers came from (machine, date, commit) and link the method.

**What fully optimized Pino should look like (goals, not measurements; label them as goals):**
- **After 089 (next):** string building with gcc drops from 231 ms into the Go/Rust range (tens of ms). The site's table gets a before/after.
- **VM work (after 089):** startup from about 0.5 s to under 0.1 s (ship the CLI precompiled), fix the lexer's slow path, faster float math. **Owner's target: the VM within 1.5–3× of Python's time.** Today its core loops are already about 2.6–6× Python, after subtracting startup.
- **0.5 performance work** (Perceus-style reference counting, in-place reuse, arenas, then inlining, bounds-check elimination): fewer reference-count operations and fewer copies. **Goal: native Pino within about 1–2× of Rust on every benchmark.** Reference counting has a cost, so Pino isn't expected to beat hand-tuned C or Rust in general.
- TinyCC builds stay slower than gcc builds (today 1.2–4.5×, most on float math). That's the trade for compiling instantly; recommend `--release` for speed.

**Corrections to the 075 entry below:** Linux no longer bundles TinyCC (085). On Linux and macOS, `--c` and `pino build` use the system `cc`/`gcc`/`clang` (`sudo apt install build-essential` on Debian/Ubuntu). TinyCC is bundled on Windows only. The release workflow built and smoke-tested all four archives; a macOS CI failure was fixed in 087.

## 2026-11-24 · compiler (075 landed: 0.4 packaging and installers)

- **README updates for 0.4 are yours** (`1bb7a56`). The full suggestions are in `.tickets/075-release-0.4/changelog.md`, section "Suggested README changes". In short:
  - The install one-liners stay (`install.ps1` via `irm … | iex`, `install.sh` via `curl … | bash`). They now install the whole `~/.pino/` folder and verify downloads against `SHA256SUMS`.
  - **Upgrading from 0.3:** the binary moves from `~/.pino/bin/pino` to `~/.pino/pino`. The installer removes the old PATH entry.
  - **Requirements:** `pino run`, `test`, `repl` and `play` need nothing on any OS. For `--c` and `pino build`: Windows has TinyCC bundled; Linux has TinyCC bundled but needs the C library headers (`libc6-dev` / `glibc-devel`); macOS uses Apple's `clang` (`xcode-select --install`). `build --release` needs GCC or Clang.
  - Pino is now **MIT licensed** (`LICENSE`), and TinyCC's LGPL notice is in `THIRD_PARTY_NOTICES.md`.
- **Wait for the GitHub check before publishing install instructions as final.** Linux and macOS haven't run yet (the owner pushes, then CI and a manual release-workflow run).

## 2026-11-24 · compiler (080 landed: `pino test`)

- `pino test` works (`b54917c`): `test "name" { }` blocks, `--vm`/`--c`/`--strict`/`--filter`, `[PASS]`/`[FAIL]` lines, failed asserts that show values, per-test leak warnings. The guide page is `docs/guide/testing.md`; the spec is `spec/features/testing.md`.
- The help is now in plain words. It's fine to copy commands from `pino --help`, except the two memory-check options (`--verify-rc`, `--debug-rc`), which share one description until a small fix lands.
- A failing assert makes a good "errors are a feature" example for the site:
  ```
  [FAIL] first changes count  (test/more_test.pino:9)
         math is broken
         `add(1, 1) == 3` was false.
           add(1, 1) is 2
  ```

## 2026-11-24 · compiler (074 landed: the CLI is `pino`)

- The new compiler's executable is now **`pino`** (`3927893`). For README and site copy: `pino init [name]`, `pino` (runs `main.pino`), `pino run [--vm | --c] [file]`, `pino build`, `pino compile`, `pino watch`, `pino check`, `pino repl`, `pino play [game]`, `pino play update`, `pino version`, `pino update`, `pino explain <code>`. `pino --help` prints the full list.
- `pino test` is coming in 080 (`[PASS]`/`[FAIL]` output, asserts that show values, leak warnings). Don't document it yet.
- The help's wording gets a plain-language pass in 080. Copy the help text after that, not now.

## 2026-11-24 · compiler (070 landed: the playground API)

- **`Pino.Wasm` is on `main`** (`404211c`): `pino-csharp-re/Pino.Wasm/`, a .NET browser-wasm project (`Microsoft.NET.Sdk.WebAssembly`), not in `Pino.sln`.
- **Build:** `dotnet publish pino-csharp-re/Pino.Wasm -c Release` (needs the `wasm-tools` workload). The output is `pino-csharp-re/Pino.Wasm/bin/Release/net10.0/publish/wwwroot/_framework/` (`dotnet.js` and the assemblies; about 3.1 MB with Brotli).
- **JS contract:** the main assembly is `Pino.Wasm.dll`. After `getAssemblyExports("Pino.Wasm.dll")`:
  - `exports.Pino.Wasm.Playground.Run(code: string): string` returns JSON `{"stdout": string, "stderr": string, "exitCode": number}`. Diagnostics and panics are in `stderr`, in the same plain-sentence text as the CLI.
  - `exports.Pino.Wasm.Playground.Evaluate(code: string): string` returns stdout, or stderr on failure. It has the old playground's shape, for an easy switch.
- In the browser, `readline()` sees an empty stdin, file I/O returns an `Err` ("File access is not available in the browser playground."), and `sleep`/`clear` do nothing. A program that runs about 2,000,000 loop steps stops with "Program stopped because it ran too many steps (possible infinite loop)." The call is synchronous, so run it in a Web Worker to keep the page responsive.
- The playground can drop the legacy `pino-csharp` WASM now. The compiler router deletes `pino-csharp/` after 075.

## 2026-11-23 · compiler (owner answers)

- The owner's answers for the site are in SITE.md, under "Owner answers": a likely full redesign, Opus for UI, Neon Low for side tasks, and the uncommitted `CoreTruths.astro` edit doesn't block.
- **`README.md` is now the site router's.** The compiler router won't edit it. When a compiler change makes the README wrong (commands, install, status), it writes the change here for the site router.

## 2026-11-23 · compiler

- **Set-up.** The site router owns `pino-site-next/**`. The compiler router owns the compiler, `spec/`, `conformance/`, `games/` and the **content** of `docs/guide/`. `README.md` belongs to the site router (see the entry above).
- **Language facts:** take them from `spec/`, `docs/guide/` and `README.md`. Every snippet on the site must pass `pino-re check` (`dotnet run --no-build --project pino-csharp-re/Pino.Cli -- check <file>`).
- **The current state, for the site's status page:**
  - the new compiler runs the whole language on a VM and on native C, with 657 conformance cases passing on both
  - the legacy compiler is being retired
  - 0.4 will ship the new compiler as `pino`, with TCC bundled, so nothing else is needed to install
- **Coming from the compiler side:**
  - 070: `Pino.Wasm` for the playground; its API goes here when it lands
  - 074: the CLI commands (`init`, `run`, `build`, `watch`, `test`, `play`, `repl`, `version`, `update`)
  - 075: release assets and installers
  - 079: `docs/guide/patterns.md`
- **The games** now live in `games/`. `pino.games/` is legacy, downloaded by 0.3's `pino play update`.
