# PROGRESS

Work tracker for Pino Lang. Invariants live in [CONTEXT.md](CONTEXT.md) and [spec/SPEC.md](../spec/SPEC.md).

## Done
- [x] **Lexer & Scanner:** Tokenization, string interpolation (`$var`, `$(expr)`), numeric separators, and `rune` literals.
- [x] **Modular Parser:** Recursive-descent + precedence climbing for expressions, statements, types, and `@(...)` labeled tuples.
- [x] **Static Type Checker & Monomorphizer:** Scoped type inference, structural interface checks, tagged unions, and compile-time generics.
- [x] **Tree-Walk Evaluator:** Full language execution engine with scoped environments and built-ins.
- [x] **PinoVM (Core Subset):** Stack-based bytecode VM for basic arithmetic, control flow, and functions (lacks complex structs/unions).
- [x] **C Transpiler (`TranspilerC.cs`):** AST-to-C99 emission, zero-copy `PinoString`, TCC + Boehm GC native builds (~2-4 KB binaries), and native `match` switch optimization (`83186da`).
- [x] **Core Test Suite:** 289 passing xUnit tests in `pino-csharp.tests/`.
- [x] **CLI Tooling & Games:** `pino init`, `run`, `watch`, `test`, `repl`, and `play` station.
- [x] **Context Brain & Harness:** Created `.routing/` (`CONTEXT.md`, `PROGRESS.md`), `.tickets/`, and Pino-adapted `.claude/skills/` (`router`, `ticket`).
- [x] **Self-Host Fixes (`projects/pino-compiler/`):** Fixed call-signature pattern shadowing (`1d81407`), added single-var `for-in` map iteration (`8bf7282`), `set:each` signature (`fb95353`), and `Lexing.pino` prototype (`12e738a`).
- [x] **`001-re-solution-and-diagnostics`:** Scaffolded `pino-csharp-re/Pino.sln` with `SourceSpan`, `LinePosition`, `SourceText`, `DiagnosticBag`, and 15 passing tests.
- [x] **`002-re-token-and-lexer-core`:** Implemented `TokenKind`, `Trivia`, `Token`, `LexResult`, and span-based `Lexer` core (`PN1001`–`PN1008`, 144 passing tests).
- [x] **`003-re-lexer-strings-and-suite`:** Mode-stack string & `"""..."""` interpolation (`PN1009`–`PN1013`), strict escape errors, legacy parity suite, real-corpus smoke tests, and `.editorconfig` formatting (`270` passing tests).

## In progress
*(None)*

## Active batch
Batch "Normative spec + conformance" (worker: Claude Sonnet 5.5 High). Approved by owner on 2026-11-19. 005–008 rebased onto `main` as linear commits (`d9d2578`): 1454 tests pass, 0 failed. Paused for owner review before 010 and 009.
- [x] **(completed, `c69edfe`)** `004-spec-skeleton-and-conformance-runner`: `spec/` skeleton, `grammar.ebnf` sections, decision template, xUnit conformance runner (TreeWalk/VM/C), spec lint, loops pilot. **Blocks 005–009.** Loops: TreeWalk 20/21, VM 13/21, C 17/21; D-001–D-005 open.
- [x] **(completed, `1c2e102`)** `005-spec-migrate-values`: lexical, variables, types, runes, strings, equality, stdlib string/regex (D-010–D-029). 677 tests pass; D-010–D-029 open (D-016, D-024, D-027 unused).
- [x] **(completed, `fc12681`)** `006-spec-migrate-control-flow`: conditionals, match, entry point, stdlib builtins (D-030–D-049). 482 tests pass; D-030–D-038 open.
- [x] **(completed, `1dd8bbc`)** `007-spec-migrate-functions-and-data`: functions, tuples, vectors, maps, modules, stdlib vector/map (D-050–D-069). 644 tests pass; D-050–D-065 open. MOD rules temporarily `[no-case]` (runner bug → 010).
- [x] **(completed, `d9d2578`)** `008-spec-migrate-types-and-errors`: structs, interfaces, enums, unions, generics, errors (D-070–D-089). 728 tests pass; D-070–D-081, D-083 open. D-071 must be cross-linked to 005's `null` decision after merge.
- [x] **(completed, `2acbabc`, Gemini 4 Hydrogen)** `010-fix-conformance-modules-discovery`: runner skips `conformance/modules/` itself; fix and restore MOD coverage. **After merge, before 009.** 1481 tests pass; MOD coverage restored; D-051→D-010, D-071→D-013 linked.
- [ ] **(in-progress, Gemini 4 Hydrogen)** `009-routing-point-to-spec`: CONTEXT/router/ticket/AGENTS point to `spec/SPEC.md`, reference note, regenerate STATUS. **After 005–008.**

Known bugs to be captured as conformance known-failures in this batch: `for idx char in` rejected by parser (004, D-001); curried lambda passed to `map` fails TCC (007); `readline` at EOF loops forever (006 decision).

## Architecture decisions
- **AD-001 — Single IR, two consumers (accepted by owner, 2026-11-19).** `pino-csharp-re` lowers the checked AST once into a Pino IR (TACKY-style, like `projects/pino-compiler/`). Two consumers read that IR: an IR interpreter/VM (dev loop, REPL, WASM playground, platforms without TCC) and a C emitter (TCC for dev builds, GCC/Clang for release). No engine re-implements semantics from the AST. Legacy `pino-csharp/` (Tree-Walk, VM, `TranspilerC`) stays as the reference oracle and is retired once `-re` matches Tree-Walk on the conformance suite. Legacy VM is frozen: no new investment.

## Next steps
- [ ] **`-re` Pino IR design (AD-001):** After `-re` parser + checker, design the IR and lowering; add `-re` as a conformance engine. Precompile Boehm GC once for faster `--c` builds.
- [ ] **Sync `.agents/AGENTS.md`:** Replace outdated syntax/workflow notes with a concise pointer to `.routing/CONTEXT.md`.
- [ ] **Modularize `TranspilerC.cs`** *(deprioritized by AD-001: legacy is replaced, not refactored)*: Split 235 KB single file into partial classes and resolve CS8604 nullable warnings.
- [ ] **Update `ARCHITECTURE.md`:** Replace stale "transpiles to C#" references with the actual C99 + TCC + Boehm GC pipeline.
- [ ] **Self-Host (`projects/pino-compiler/`):** Update stale test fixtures (`checker_test.pino`, `parser_test.pino`) for `Constant` union schema, and advance TACKY -> C codegen when requested.
- [ ] **Repository cleanup:** Remove leftovers from previous migrations (e.g. `.context/proposals/` duplicates); close the unterminated code fence at `LANGUAGE_REFERENCE.md:219`.
- [ ] **Data Science & FFI:** Design n-dimensional array/tensor primitives and standardize C FFI (`proposals/ffi_standardization_proposal.md`).

## Architectural gaps & tech debt
- [ ] **`TranspilerC.cs` monolith:** 235 KB single file with CS8604/CS0184 compiler warnings.
- [ ] **PinoVM parity** *(won't fix per AD-001; legacy VM frozen)*: VM lags behind Tree-Walk and C Transpiler on structs, unions, and complex stdlib methods.
- [ ] **Language specs:** Decide on explicit variable type annotations (`val x: int = 10`), struct value-vs-reference semantics, and `null` deprecation vs `Option[T]`.
- [ ] **Self-host test drift:** `projects/pino-compiler/test/checker_test.pino` and `parser_test.pino` expect old `Expression::Constant(int)` instead of `Constant` union.
