# PROGRESS

The current state of Pino Lang. Rules live in [CONTEXT.md](CONTEXT.md) and [spec/SPEC.md](../spec/SPEC.md).

The full history up to 2026-11-23 (every ticket's verification notes, owner answers, audits) is in [archive/PROGRESS-2026-11-19-to-2026-11-23.md](archive/PROGRESS-2026-11-19-to-2026-11-23.md). Search it before re-deciding anything. Each ticket's `changelog.md` in `.tickets/` holds the details.

**Keep this file short.** Record a finished ticket as one bullet of three to five lines (SHA, model, what it did, router verification, notes for later tickets). When "Done" grows past about 40 tickets, fold the oldest into the archive.

## Where we are (2026-11-23)

- **The new compiler (`pino-csharp-re/`) runs the whole language on two engines:** the IR VM (`Pino.Vm`, the default for `pino-re run`) and C99 (`CodeGen/C`, `pino-re run --c` / `pino-re build`, TCC by default, GCC/Clang with `--release`).
  - Both pass **656/656 conformance cases** with 0 known failures and identical output (differential test, plus a 220-seed fuzzer), with 0 leaks under shadow-RC (VM) and `-DPINO_DEBUG_RC` (C).
  - `-re` suite: 6348 tests, green in a clean worktree.
- **Memory:** reference counting step 1 (simple and correct), no garbage collector. Perceus, reuse and arenas come next (067–069).
- **The legacy compiler (`pino-csharp/`) is being disposed of (owner, 2026-11-23): stop testing it.** It is still published by `release.yml` and used by the site's WASM playground until 070 and 075 replace those. Delete it after them.
- **Goal: the Pino 0.4 release with the new compiler.**
- **CI:** `.github/workflows/ci.yml` (Windows). It has never run on GitHub; the next push runs it.

## Architecture decisions (accepted)

- **AD-001: one IR, two consumers.** `-re` lowers the checked AST once into PIR. The VM (dev loop, REPL, WASM) and the C emitter both read that IR, and no engine re-implements semantics from the AST.
- **AD-002 (amended 2026-11-21): RC directly, no GC phase.**
  - Perceus-style RC (borrowing, reuse, drop specialization), validated by VM shadow counts and the C debug mode.
  - A cycle collector only for closures that capture a `var`, and only if needed.
  - **Automatic arenas with no syntax:** escape analysis over four escape points: return, a `mut` param or `self`, a closure capture, and a global or `WritesModuleState`.
  - `Pool[T]` / `Handle[T]` for linked data.
  - Atomic RC only for values that cross threads, designed together with concurrency.
- **AD-003: value semantics.**
  - Structs and collections are values with copy-on-write.
  - `val` is deeply immutable.
  - `mut` marks fields, methods (`mut fn`), interface members and parameters (`f(mut a)` at the call site).
  - `===` is removed.
  - Plain data can't form cycles.
- **Owner direction:** Go-like microservices and games. "The best balance of simplicity and performance for the people we love." Rust-class speed for normal code is the target, with concurrency that keeps RC non-atomic by moving values between tasks.

## Done (recent; older tickets are in the archive)

- **059–062, the IR (Neon):**
  - `IR/` core, printer and verifier (059, `5607024`)
  - lowering of the core language (060, `fc30e8d`)
  - closures, interfaces, generics and inline callback loops (061, `8ca812d`)
  - RC step 1 with a post-RC verifier stage (062, `c7b22d6`)
- **064 `runtime-re/`** (`67dcd7c`, Sonnet High): a C99 RC runtime, with a debug mode that poisons freed memory and reports leaks.
- **065 C emitter and CLI** (`dfde22d`, Neon High; one nudge): `build`, `run --c`. Floats print like .NET (`1E+17`, `1E-05`, plus `.0`). That rule is provisional; the spec ticket is pending.
- **063 `Pino.Vm`** (`cfddc28`, Sonnet High; one rebase): a register VM with a 16-byte `PinoValue`, `PlaceRef` and the shadow-RC tracker. `run [--vm | --c]`, VM by default.
- **071 semicolons** (`5e77d03`, Sonnet Medium; two review rounds): `;` is ignored with `hint[PN1018]`, `pino-re explain`, and the PN3411 wording.
- **072 CI** (`dca3076`, Sonnet Medium): TinyCC 0.9.27 is a pinned download (SHA256 `34A72194…FAABB2`), and `PINO_TEST_TIME_SCALE` scales three timing guards.
  - It still runs the legacy suite and embeds `tooling/gc`; 077 removes both.
  - Possible flake: TCC "could not write case.exe" once in `CEmitterConformanceTests`.
- **073 games** (`92a03bd`, Sonnet Medium): ported copies in `games/`, with no story text lost; `pino.games/` is frozen for 0.3 users. `GamesTests` plays scripted sessions.
- **`1b578f1` (router, with the owner's OK):** recorded the case-only rename of `projects/pino-compiler/modules/Lexer.pino` and `Parser.pino`. That was the old "worktree-only" failure. **Workers now have zero allowed test failures.**
- **README** rewritten and pushed (`eee5915`). The story is option B, and Anahí is not named in it.

- **066 ReVm/ReC in conformance** (`56f41c1`, Neon Medium; blocked once and asked, correctly):
  - ReVm and ReC are in the shared runner, with five columns in `STATUS.md`; each passes 656/656 with 0 known failures
  - the ReVm-vs-ReC differential passes 421/421, and the fuzzer 220 seeds
  - OQ-42 is settled: 24 `.err` files moved to the `-re` wording, `match/err-union` and `types/err-void-collection-element` were fixed per the spec, and `CheckerConformanceTests` now matches lex/parse `.err` text
  - Router verification of the rebased branch (PowerShell): `-re` 6350/6350, conformance 3576/3576, 0 warnings

## In progress

- **076 checker bug** (Neon Medium, **reusing the warm 066 thread**, branch `t3/checker-else-scope-076`): inside a struct method, an `else`/`else if` body can't see fields, `self` or earlier locals (PN3001). It also restores `show_status` in `games/jade_temple.pino`.

## Next (in order)

1. *(076 is in progress, above.)*
2. **077 retire legacy testing** (to draft; Neon Medium, reuse the same thread):
   - move the conformance runner and spec lint into `pino-csharp-re` tests, keeping only ReVm and ReC
   - drop the legacy known-failure lines and the legacy `STATUS.md` columns
   - remove the legacy suite and the embedded `tooling/gc` from `ci.yml`
   - CONTEXT's quality gate changes with it
3. **070 REPL and a separate `Pino.Wasm`** (Neon Medium).
4. **074 CLI parity and the rename `pino-re` → `pino`** (Neon Medium): `init`; `run` defaulting to `main.pino`; `watch`, `test`, `compile`; `play` from `games/`, pinned to the release tag; `version`, `update`. Panics inside imported modules must name the right file (063 open point).
5. **075 release** (Neon High):
   - `release.yml` publishes `Pino.Cli` with TCC bundled per OS: win64 0.9.27; Linux built from pinned source; macOS tested, falling back to system clang
   - add `osx-arm64`, and Linux/macOS CI jobs
   - installers, the README commands table, an "upgrading from 0.3" note, and the TinyCC LGPL notice
   - **no light variant** (owner agreed with the router)
   - then delete legacy `pino-csharp/`
6. **A small spec ticket** (Hydrogen Medium): the float print rule (TYPE-10), plus the texts the VM had to invent (`readline` at EOF, `int()`/`float()` failures, I/O errors) and `write_file` returning `Success(path)`.
7. **The owner tags `v0.4.0`.**

After the release-critical path, or alongside it:
- **078 idiomatic games refactor** (owner wants it): `mut fn` methods and `mut` params. Text and numbers stay locked.
- **079 a `docs/guide/patterns.md` chapter** ("how do I…": change an element, update through functions, game state, when to use `Pool`, coming from Python/JS). Write it from what 078 teaches.
- **Friendliness tickets:**
  - a "lost write" warning (a copy that's changed and never stored back)
  - one PN3221 per field instead of one per use
  - `readline() or ""`
  - `rand(n / 2 + 1)` pointing at the `/`
- **067–069 Perceus, reuse, arenas** (Neon High). 067 can run in parallel with 070.
- **Planned performance tickets** (after 067):
  1. inlining function-value callbacks
  2. bounds-check elimination
  3. `restrict`
  4. layout control (`[N]T`, SIMD; needs owner syntax decisions)
  5. a hidden-copy diagnostic
  6. benchmarks against C, Rust and Go, seeded by `benchmarks/lexing/`

## Open owner decisions

- **`with mut s = xs[i] { ... }`:** a block-scoped mutable alias that lowers like a `mut` argument, under D-184 exclusivity. The owner finds it interesting; decide after 078 shows whether it's still needed. `Pool` doesn't solve this case.
- **Letting a local shadow a same-named parameter** (`var ship = ship`, blocked today by VAR-E4).
- `fn main {` without parentheses is **intended** (FN-1).
- Sized numeric types (`i8`…`f64`): deferred.
- Concurrency design: after the IR work. Values can't race; closures that capture a `var` don't cross threads; RC is atomic only on crossing.

## Waiting on the owner

- **Push:** 19+ commits are local. The first push runs CI on GitHub.
- `projects/c-compiler/`: the owner commits it after all tickets (they're finishing that chapter).
- **The `pino-site-next/` refresh:** align it with the README and remove the "AI-slop" feel. The owner must commit their site changes first and choose the model and how much design freedom. The router hasn't read the site.
- A Proyecto Guiado proposal (Spanish, one page), once the owner shares the course details.

## Tech debt

- `ARCHITECTURE.md` and `LANGUAGE_REFERENCE.md` are stale against `spec/` and the new compiler. Rewrite or retire them with the 0.4 docs.
- Self-host corrections are **the owner's, by hand, never ticketed**. The list is in the archive under "Pending self-host corrections": D-020 regex escapes, `=>` → `->`, `k: v` map entries, and others.
- Three timing guards are flaky under load (now scaled in CI). Possible TCC temp-dir flake in `CEmitterConformanceTests`.
- `Option`/`Result` can be shadowed by a user `struct Option` with no guard (nit from 054).
