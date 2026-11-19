# PROGRESS

Work tracker for Pino Lang. Invariants live in [CONTEXT.md](CONTEXT.md) and [LANGUAGE_REFERENCE.md](../LANGUAGE_REFERENCE.md).

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

## Draft tickets
*(None — `.tickets/` is empty)*

## Next steps
- [ ] **Sync `.agents/AGENTS.md`:** Replace outdated syntax/workflow notes with a concise pointer to `.routing/CONTEXT.md`.
- [ ] **Modularize `TranspilerC.cs`:** Split 235 KB single file into partial classes and resolve CS8604 nullable warnings.
- [ ] **Update `ARCHITECTURE.md`:** Replace stale "transpiles to C#" references with the actual C99 + TCC + Boehm GC pipeline.
- [ ] **Self-Host (`projects/pino-compiler/`):** Update stale test fixtures (`checker_test.pino`, `parser_test.pino`) for `Constant` union schema, and advance TACKY -> C codegen when requested.
- [ ] **Data Science & FFI:** Design n-dimensional array/tensor primitives and standardize C FFI (`proposals/ffi_standardization_proposal.md`).

## Architectural gaps & tech debt
- [ ] **`TranspilerC.cs` monolith:** 235 KB single file with CS8604/CS0184 compiler warnings.
- [ ] **PinoVM parity:** VM lags behind Tree-Walk and C Transpiler on structs, unions, and complex stdlib methods.
- [ ] **Language specs:** Decide on explicit variable type annotations (`val x: int = 10`), struct value-vs-reference semantics, and `null` deprecation vs `Option[T]`.
- [ ] **Self-host test drift:** `projects/pino-compiler/test/checker_test.pino` and `parser_test.pino` expect old `Expression::Constant(int)` instead of `Constant` union.
