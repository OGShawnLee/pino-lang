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
- **CI:** `.github/workflows/ci.yml` (Windows). Pushed 2026-11-23 and green on GitHub (owner).

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

- **076 method `else` scope** (`68a4e88`, Neon Medium, **the first warm reuse**, on the 066 thread):
  - Root cause: Phase 2 cloned the method body scope and re-parented only the `if` chain, so `else` bodies stayed on the discarded scope. The fix re-parents the body scope in place, deleting about 35 lines of cloning code.
  - It also fixed orphaned `or { }` blocks and `for` headers inside methods.
  - 6 regression tests, plus `conformance/structs/method-else-scope`; `show_status` is restored in `jade_temple`.
  - Router verification: `-re` 6365/6365, the `bug3` probe prints `3`, and no story literal was lost.
  - **Warm-rule verdict (one data point):** 22 minutes in total, about 6 of them to find the root cause and fix it; the rest was the test suites. No fix rounds, and a deeper root cause than asked. It was good.

- **077 retire legacy testing** (`e9260e9`, Neon Medium, fresh thread; one pass):
  - one ReVm/ReC runner, spec lint and the `STATUS.md` generator in `Pino.Compiler.Tests/Conformance/`, each case running once per engine
  - the changelog maps every old check to its new place
  - `pino-csharp.tests/` is deleted, along with the duplicate VM/C/checker conformance tests and 30 legacy `known-failures.txt` files (1,328 entries)
  - CI dropped the legacy suite and the `tooling/gc` step; the docs were updated, plus one line in `ARCHITECTURE.md`
  - Router verification: a broken `.out` fails on both engines (worker probe); `-re` 5487/5487 in main; the suite takes about 3.5 minutes, down from about 4.5
  - `STATUS.md`: ReVm and ReC 657/657

- **070 REPL and `Pino.Wasm`** (`404211c`, Neon Medium, fresh thread; one round back):
  - `pino-re repl` on `VmSession`: persistent globals, each chunk runs once (round 1 replayed earlier chunks; `val t = time()` changed). Additive `IrLowerer.LowerReplChunk`, `RcInsertPass.RunIncremental`.
  - `pino-csharp-re/Pino.Wasm/` (browser-wasm, outside `Pino.sln`, so CI needs no `wasm-tools`): `Playground.Run` → JSON `{stdout, stderr, exitCode}`, plus a legacy-shaped `Evaluate`. Contract in HANDOFF.
  - Router verification: REPL probes (`time()` stable, `readline` not re-read, 3M-step loop once, `--verify-rc` 0 leaks); `-re` 5505/5505.

- **074 CLI parity, `pino-re` → `pino`** (`3927893`, Neon Medium, **warm reuse of the 070 thread**; ended its turn early twice, one wrong claim corrected (`assert` "not a builtin"), help guard added mid-ticket):
  - `init`, no-argument run, `main.pino` defaults, `compile`, `watch`, `play`/`play update` (games at tag `v<version>`), `version`, `update` (OS and architecture assets, injectable fetcher); `pino test` is a placeholder until 080
  - `IrFunction.Source`: panics in imported modules name their own file on both engines
  - one table of commands and options drives the parser and the help; a test fails when an option is missing from the help
  - Router verification: 5528/5528 on the branch; probes of help, `init` (twice), no-args, `run --c`, `check`, `test`, an unknown option. Help wording still has jargon → reworded in 080.

- **081 parallel conformance tests** (`add43e6`, Hydrogen Medium, fresh thread; one pass, rebased by the router onto 074):
  - 8 conformance shard classes (stride over `DiscoverCases()`), a test that they cover each case exactly once, a thread-safe `RepoRoot`, and the fourth timing guard now scaled
  - **Suite: 2 min 13 s alone (router measurement), down from about 3.5** (about 6 under load); the old single theory was 333 s of serial work
  - Router verification: 5529/5529 on the rebased branch; the worker showed a broken `.out` fails on both engines, and `STATUS.md` regenerates with no diff
  - Same day, owner-approved process changes: workers iterate with filtered tests and run the full suite once; the router runs it once per ticket.

- **080 `pino test`** (`b54917c`, Neon High, fresh thread; ended its turn early twice, nudged):
  - D-188 and `spec/features/testing.md` (TEST-1..14, TEST-E1..E2); test blocks lowered to test entries; per-test fresh state, captured output, leak lists on both engines (VM shadow RC, C debug RC); `--vm`, `--c`, `--strict`, `--filter`; 12 `conformance/testing/` cases; `docs/guide/testing.md`; help reworded in plain words
  - Beyond scope, accepted: the parser never filled `assert(cond, msg)`'s message (now parsed, with tests); `RcInsertPass` keeps values live into panic-only blocks so failed asserts can print them (all functions; leaves a dead `_ = panicOnly;`)
  - Router verification: 5623/5623; by hand on both engines: failure then pass, fresh state, message before values, output under `[FAIL]`, panic location, `--filter`, `run` ignores tests
  - Follow-ups: the VM and C engines word the index panic differently ("Index 5 is out of range for a vector…" vs "Index 5 out of range for vector…"; only stdout is compared); `--verify-rc` and `--debug-rc` share one help description.

- **082 assert value lines and help polish** (`c8d882d`, Hydrogen Medium, fresh thread; one pass): value lines align with "was false" (owner caught the two-space indent that the router's 080 format introduced); TEST-6 and a D-188 note; distinct `--verify-rc`/`--debug-rc` help; dead `panicOnly` parameter removed. Router verification: 5623/5623 (2 min 13 s while 075 was building); output checked.

- **075 release 0.4** (`1bb7a56`, Neon High, fresh thread; stopped once to ask about the missing `LICENSE`, correctly):
  - `release.yml`: win-x64, linux-x64, osx-x64 (`macos-15-intel`) and osx-arm64; trimmed single-file `pino`; TinyCC pinned on Windows and built from the pinned source on Linux (SHA256 `de23af78…ceb9c`); `SHA256SUMS`; a smoke test per archive; publishes only on `v*` tags (`workflow_dispatch` builds and tests)
  - `ci.yml`: Linux and macOS jobs with the system compiler; `CCompilerDriver` looks next to the executable first, falls back to `cc`/`clang`/`gcc`, and prints an install hint per OS; installers install the whole `~/.pino/`; MIT `LICENSE`, `THIRD_PARTY_NOTICES.md`
  - Router verification: 5636/5636 after rebasing onto 082; worker's local Windows install and smoke test from outside the repo (8 MB zip, 17 MB `pino.exe`)
  - **Not yet checked: Linux and macOS** (needs the owner's push, CI, and a manual release-workflow run). **Before tagging:** `runtime/re.c` is SLRE-derived with its licence header stripped; some SLRE versions are GPL-2.0. Owner to confirm the origin, or replace it.

- **083 runtime texts** (`d9fe3a6`, Hydrogen Medium, fresh thread; one pass, rebased by the router onto 075):
  - D-189; TYPE-20 (float form, kept as .NET style by the owner), TYPE-21/22 (`int()`/`float()` failures), VEC-E4, MAP-E1, STRLIB-E1/E2, BUILTIN-9/13/17 (`readline` at end of input, `write_file`, `IOError` payloads)
  - C now uses the VM's plain-sentence wording everywhere; the ReVm-vs-ReC differential compares runtime-panic stderr (a deliberate mismatch fails); 5 new cases, 3 `.err` files tightened from fragments to full texts
  - Router verification: 5671/5671 after the rebase (2 min 6 s)

## In progress

- **First GitHub run (owner, 2026-11-24):** CI passed with Node 20 deprecation warnings; the release workflow failed on Linux building TinyCC 0.9.27 (`bcheck.c`: `__malloc_hook` undeclared; glibc 2.34 removed it).
- **085 Linux system cc and Node 24 actions** (`d27956b`, Hydrogen Medium; one pass). **Owner: Linux doesn't bundle TinyCC**; `FindBundledTcc` returns null off Windows. Actions bumped: checkout v7, setup-dotnet v6, cache v6, upload-artifact v7, download-artifact v8, action-gh-release v3 (each `using: node24`, with links in the changelog). Router verification: 5672/5672. Needs the owner's GitHub rerun.
- **086 regex engine** (`8eb8765`, Neon High, fresh thread; ended its turn early once). It replaces `runtime/re.c` (origin unknown: the owner can't trace it, and Flash 3.8 later edited it).
  - Owner decisions: RE2/Go syntax with a linear-time guarantee; `replace` is literal; 0.4 waited for this engine. D-190 settles D-029.
  - Written from scratch in C (`runtime-re/pino_regex.c`) and C# (`Pino.Vm/Regex/`): parser and AST, Thompson program, Pike VM, literal fast path, an ASCII lazy DFA (1,024-state cap), and a generated Unicode simple case-folding table (`tools/gen-casefold.py`)
  - 20,000-case fuzzer (C# == C, and both against .NET ECMAScript); linear-time tests; 30 regex conformance cases; `re.c`/`re.h` deleted. `runtime/` now holds only the legacy `runtime.c`/`runtime.h` (gone in 084).
  - Worker benchmark, 10 MB, `find_all` (not reproduced by the router): literal 2.1 ms (C) / 1.6 ms (C#), email 73/87 ms (.NET compiled 43, Go 604), identifier/number 63/84 ms (Go 1,474); the old `re.c` took 278 ms and 2.9 s, and crashed on the third.
  - Router verification: 5830/5830; by hand on both engines: `(?i)ñandú`, empty-match replace, `find_all`, `match_prefix`, `(a*)*b` on 60,000 characters, and the exact backreference panic.
  - Follow-up: a UTF-8 byte-level DFA for non-ASCII text (the Pike VM handles it today, still in linear time).
- **Performance bug found in the 086 review:** `s = s + "a"` 60,000 times takes about 5.8 s natively (`--c`, TCC) against about 1.4 s on the VM. The C runtime seems to copy the string on every append instead of extending a unique one in place. Ticket it with the first performance batch (related to 068 reuse).
- **Second GitHub run (owner, 2026-11-24):** the release workflow (manual `workflow_dispatch`) built and smoke-tested all four archives. CI passed on Windows and Linux; macOS failed 3 regex tests because Clang rejects the duplicate `PinoReProg` typedef (`pino_regex.h` and `pino_rt.h`) under `-std=c99 -Werror`.
- **087 macOS typedef fix** (`f8c09dc`, Neon High on the 086 thread at the owner's request; one pass). `pino_rt.h` now uses `struct PinoReProg*`; the C runtime tests pass `-Wpedantic` so gcc on Windows and Linux catches C99 violations Clang rejects. `runtime-re/*.c` is clean under `gcc -std=c99 -pedantic-errors`. Router verification: 5830/5830. Needs the owner's macOS CI rerun.
- **Owner, 2026-11-24: Pino's licence is MIT** (`Copyright (c) 2026 OGShawnLee`; the owner may change the name line). 075 adds `LICENSE`. macOS x64 builds on `macos-15-intel` (`macos-13` was retired).

- **075 release 0.4** (Neon High, fresh thread, in parallel with 082; no shared files):
  - archive per OS: `pino`, `runtime-re/`, `runtime/re.c` and `re.h`, `tooling/tcc/`, `games/`, `LICENSE`, `THIRD_PARTY_NOTICES.md`
  - TCC per OS: win64 pinned; Linux built from the pinned 0.9.27 source; macOS uses the system `clang`
  - `CCompilerDriver` prefers the executable's folder and falls back to the system C compiler, with an install hint per OS
  - `release.yml` gets four assets, `SHA256SUMS` and a smoke test per archive; `ci.yml` adds Linux and macOS jobs; the installers install the whole folder and verify it
  - **Linux and macOS can only be checked on GitHub:** the owner pushes the branch and runs the workflows; expect a fix round
  - `pino-csharp/` is deleted later (084). The site doesn't use the legacy WASM (no reference in `pino-site-next/`).
  - Note for the owner: on Linux, `--c` still needs the C library headers; on macOS, Apple's command line tools. The VM (`pino run`) needs nothing.

## Next (in order)

1. **080 `pino test`** (done, above). Owner decisions, 2026-11-24, which become D-188 in the ticket:
   - a failed `assert` shows both sides' values (pytest-style, compiler-generated)
   - the first failure stops only that test
   - leaks warn by default, listed one per line under the result; `--strict` makes them failures
   - expected-panic tests come after 0.4
   - the help must list every option (074 adds a guard test)

   Original proposal: how `test "name" { }` blocks run. Router proposal, matching 0.3: each test runs alone after the module's top level, a failed `assert` or panic fails that test and the rest go on, `[PASS]`/`[FAIL] test "<name>"` lines, a summary, exit `1` on any failure; `pino test` with no file scans `*_test.pino` and `test/`. Then lowering and a runner on both engines (Neon Medium).
2. **075 release** (Neon High):
   - `release.yml` publishes `Pino.Cli` with TCC bundled per OS: win64 0.9.27; Linux built from pinned source; macOS tested, falling back to system clang
   - add `osx-arm64`, and Linux/macOS CI jobs
   - installers and the TinyCC LGPL notice. The README commands table and the "upgrading from 0.3" note are now the site router's: hand them over through HANDOFF.md
   - **no light variant** (owner agreed with the router)
   - `CCompilerDriver` finds TCC under the **repo root** (`tooling/tcc`); an installed `pino` must find it next to its own executable
   - asset names must match what 074's `update` looks for: `pino-{windows,linux,macos}-{x64,arm64}`
   - then delete legacy `pino-csharp/`
3. **A small spec ticket** (Hydrogen Medium): the float print rule (TYPE-10), plus the texts the VM had to invent (`readline` at EOF, `int()`/`float()` failures, I/O errors) and `write_file` returning `Success(path)`.
   - **Add: every runtime panic text is the same on both engines.** Found in the 080 review: the index panic differs. Compare panic stderr in the conformance differential, and spell each text in the spec.
   - *(The help lines and the dead code moved to 082.)*
   - Starts after 082 (both touch conformance expectations). Becomes ticket 083.
   - Then **084: delete `pino-csharp/`** and the legacy parts of `runtime/` (keep `re.c`/`re.h`), after 075 lands; `pino.games/` stays for 0.3's `play update`.
4. **The owner tags `v0.4.0`.**

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

- `projects/c-compiler/`: the owner commits it after all tickets (they're finishing that chapter).
- **The site** has its own router (`site-router` skill, `.routing/SITE.md`). The owner answered its questions: likely a full redesign, Opus for UI, Neon Low for side tasks, and **README belongs to the site router**. It's ready to start. Old note on the `pino-site-next/` refresh: align it with the README and remove the "AI-slop" feel. The owner must commit their site changes first and choose the model and how much design freedom. The router hasn't read the site.
- A Proyecto Guiado proposal (Spanish, one page), once the owner shares the course details.

## Tech debt

- `ARCHITECTURE.md` and `LANGUAGE_REFERENCE.md` are stale against `spec/` and the new compiler. Rewrite or retire them with the 0.4 docs.
- Self-host corrections are **the owner's, by hand, never ticketed**. The list is in the archive under "Pending self-host corrections": D-020 regex escapes, `=>` → `->`, `k: v` map entries, and others.
- Three timing guards are flaky under load (now scaled in CI). **A fourth:** `ParserFuzzTests.SeededRandomTokenSequences_MaintainAllInvariants` (500 ms per input; 574.5 ms on 2026-11-24 while a worker was building; passes alone). Is it scaled by `PINO_TEST_TIME_SCALE`? Check in the test-speed ticket. Possible TCC temp-dir flake in `CEmitterConformanceTests`.
- `Option`/`Result` can be shadowed by a user `struct Option` with no guard (nit from 054).
