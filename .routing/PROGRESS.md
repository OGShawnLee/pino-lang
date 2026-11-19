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
- [x] **Batch "Normative spec + conformance" (`004`–`010`, `c69edfe`..`0e5a7f3`):** `spec/` (26 feature/stdlib files with rule IDs, `grammar.ebnf`), `conformance/` runner over TreeWalk/VM/C (1481 tests pass), spec lint, 60 open decisions in `spec/decisions/`, and agent docs pointing at `spec/SPEC.md`. Workers: Sonnet 5.5 High (004–008), Gemini 4 Hydrogen Medium (009, 010). Status per feature: `conformance/STATUS.md`.
- [x] **`011-audit-pino-csharp-re` (Gemini 4 Neon Medium, read-only):** Verdict "solid with fixes": 0 critical, 7 major, 8 minor, 1 note (`.tickets/011-audit-pino-csharp-re/audit.md`). Router reproduced all 7 majors. Fuzzer: 64,425 inputs, 0 crashes/gaps; corpus 441 files OK; `-re` lexer 11.5x faster than legacy.
- [x] **`012-re-audit-fixes` (`f7b2db5`, Gemini 4 Hydrogen Medium):** All 011 findings resolved: `TokenFlags` (Token stays 12 B), `DiagnosticCodes` catalog `PN1001`–`PN1015` + Hint/Related/Tags/Help, UTF-16 docs + `GetOffset`, D-020/D-021 applied (accepted), robustness fixes, allocations 23.7 MB → 6.0 MB. `-re` 1140 tests, legacy 1493 tests pass. Opened D-090–D-093. **Owner TODO:** double-escape regex strings in `projects/pino-compiler/modules/Lexer.pino` and `projects/c-compiler/modules/lexer.pino` (D-020).
- [x] **D-010 accepted (option 3, `a8b5ea3`, follow-up in the 012 thread):** asymmetric `-` spacing in comma-optional lists is an error (LEX-17, LEX-E2). Legacy engines listed as known failures; enforced by the future `-re` parser.

## In progress
*(None)*

## Architecture decisions
- **AD-001 — Single IR, two consumers (accepted by owner, 2026-11-19).** `pino-csharp-re` lowers the checked AST once into a Pino IR (TACKY-style, like `projects/pino-compiler/`). Two consumers read that IR: an IR interpreter/VM (dev loop, REPL, WASM playground, platforms without TCC) and a C emitter (TCC for dev builds, GCC/Clang for release). No engine re-implements semantics from the AST. Legacy `pino-csharp/` (Tree-Walk, VM, `TranspilerC`) stays as the reference oracle and is retired once `-re` matches Tree-Walk on the conformance suite. Legacy VM is frozen: no new investment.

## Next steps
- [ ] **`-re` Pino IR design (AD-001):** After `-re` parser + checker, design the IR and lowering; add `-re` as a conformance engine. Precompile Boehm GC once for faster `--c` builds.
- [ ] **Decision batch (owner):** Settle the open decisions in `spec/decisions/` by topic (commas/unary minus, rune arithmetic, type annotations, `null` vs `Option`, struct semantics, generics syntax, closures, entry point, builtins). Then ticket the engine fixes they imply.
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
