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

- [x] **`013-scan-self-host-syntax` (Gemini 4 Hydrogen Medium, read-only):** 79 syntax forms inventoried across self-host, legacy and spec (`.tickets/013-scan-self-host-syntax/inventory.md`); router spot-checked. The owner settled most of its questions (see the decision batch below).

## Active tickets (owner away; router verifies and integrates by rebase + fast-forward, no push)
- **Dispatched 2026-11-19** after the owner updated Antigravity: 014, 016, 017 and 018 are running (014, 017 and 018 in worktrees; 016 read-only in the main checkout). 015 is written after 014 is integrated.
- [ ] **014-spec-record-syntax-decisions** (Hydrogen Medium, worktree): record the syntax decisions in `spec/`, rewrite the affected conformance cases, log legacy failures as known failures.
- [ ] **015-spec-record-semantics-decisions** (Hydrogen Medium, worktree): same for semantics and the stdlib. Starts after 014 is integrated (both rewrite cases).
- [x] **016-re-parser-design** (Neon Medium, read-only; router-verified): `.tickets/016-re-parser-design/design.md`. Proposes a typed immutable AST plus the lossless `LexResult` (no Roslyn green/red trees), recursive descent with a 9-level Pratt expression parser, `TokenFlags` predicates for D-010/D-058/D-001/trailing commas, sync-set recovery with `PN2001`–`PN2020`, no incremental parsing yet, and a 4-ticket split (019 AST + `->` token, 020 expressions/types, 021 statements/declarations, 022 corpus + fuzzer). **Awaiting owner approval and 8 owner questions (OQ-1..OQ-8):**
  - `or { }` precedence
  - `in`/`is`/`=` levels
  - whether a newline ends a bare `return` or a postfix `(`
  - comma-less untyped lambda params `fn (n m)` (ambiguous with `fn (n Type)`; the self-host misparses it)
  - `Variant(A B)` ambiguity
  - `set[i]` vs `set[T]`
  - D-055/D-076 and the pending 013 items.
- [x] **017-repo-cleanup-inventory** (`69110fc`, Hydrogen Medium): `ARCHITECTURE.md` rewritten (legacy pipeline, `-re` as planned per AD-001, self-host, spec/conformance; router checked its paths). 39 leftover candidates in `.tickets/017-repo-cleanup-inventory/inventory.md` (18 duplicates, 13 orphans, 6 stale, 1 superseded, 1 generated), almost all in the gitignored `.context/`. A commented-out deletion block awaits owner approval. Note: 14 of the "exact duplicates" differ only in line endings (LF vs CRLF).
- [ ] **018-re-lexer-apply-decisions** (Hydrogen Medium, worktree): apply D-090, D-091 and D-093 to the `-re` lexer.
- Owner decision (2026-11-19): existing conformance cases are rewritten into the decided language; legacy failures go to `known-failures.txt` citing the decision.

## Next steps
- [ ] **`-re` Pino IR design (AD-001):** After `-re` parser + checker, design the IR and lowering; add `-re` as a conformance engine. Precompile Boehm GC once for faster `--c` builds.
- [ ] **Pending self-host corrections (owner, by hand; never ticketed to workers):**
  - D-020: double the backslashes in regex string literals (`"\+"` → `"\\+"`, etc.), because unknown escapes are now errors. Files: `projects/pino-compiler/modules/Lexer.pino` and `projects/c-compiler/modules/lexer.pino`. The `-re` corpus test skips exactly these two files until they are fixed.
  - Precedence: `Parser.pino:386` binds unary `-`/`not` tighter than postfix `[i]`/`:`; per the decision, `-arr[0]` must negate the element.
  - Braces: `Parser.pino:684,688` accept unbraced `if`/`else` branches; per the decision, braces are always required.
  - Union payloads: support positional `Variant(Type)` alongside named `Variant(name Type)`, all-or-nothing per union.
  - Root `main.pino`: old spellings `fn (int, int) int` (line 16; now `fn (int, int) -> int`) and the bare set `{ 1, 2, 3 }` (line 31; now `set[int] { 1, 2, 3 }`).
- [ ] **Decision batch (owner):** Settle the open decisions in `spec/decisions/` by topic (commas/unary minus, rune arithmetic, type annotations, `null` vs `Option`, struct semantics, generics syntax, closures, entry point, builtins). Then ticket the engine fixes they imply.
  - Decided 2026-11-19, not yet written into `spec/decisions/`, to be recorded in one ticket at the end of the session:
    - D-001: option 2 (the loop-header comma is optional).
    - D-051: accepted as following D-010 (no new rules).
    - D-058: option 2 (a line break before `[` ends the expression).
    - D-090: close on the last 3 quotes of a 3–5 quote run.
    - D-091: reject raw NUL and C0 controls (tab allowed; newlines only in `"""`).
    - D-092: a single `_` between any two digits.
    - D-093: a keyword after `$` is an error suggesting `\$` or `$(...)`.
    - D-052 / D-074 (duplicates): option 2 (named functions and methods are block-only, as in the self-host at `Parser.pino:962`; fix the reference's interface example). This supersedes an earlier answer that rested on a wrong premise; ticket 013 showed only lambdas have arrow bodies.
    - D-053: option 1 (lambdas take no return type; it is inferred, as in the self-host: `fn (n m) -> n + m`).
    - D-072: decorator only, spelled `@@Generic[T]` as in the self-host (`projects/pino-compiler/modules/Parser.pino:1264`); `@generic[...]` and inline `struct Pair[A, B]` are removed. Open sub-questions for the recording ticket: how bounds (`T is Shape`) are spelled, and whether `@@Generic` is allowed beyond `struct` (the self-host currently accepts only `struct`).
    - D-035: option 2 (the arrow `else` form exists only in `match`, spelled `else -> expr` per the arrow decision below; rejected after an `if` block).
    - Arrows (from ticket 013): `->` everywhere (lambdas `fn (x) -> expr`, map entries `k -> v`, function types, `when`/`else` match arms). `=>` leaves the language; map entries no longer use `k: v`.
    - Function types (from 013): `fn (int, int) -> int` as in the self-host (`Parser.pino:835`); `()` is optional and an omitted return means `void` (`fn` alone is `fn () -> void`). The owner's root `main.pino:16` (`fn (int, int) int`) is the old spelling.
    - Sets (from 013): `set[int] { 1, 2, 3 }`, with empty `set[int]` (`Parser.pino:328-375`); a bare `{ 1, 2, 3 }` is not a set literal.
    - D-011: option 2 variant (`rune + int` gives a rune, `rune - rune` gives an int; `rune + rune` is an error suggesting `"$a$b"` or `str(a) + str(b)`).
    - D-019: implicit widening of `int` to `float` in arithmetic and comparison (`1 == 1.0` is true); `/` always returns a float (`5 / 2` is `2.5`); assigning a float to an int `var` is an error. Open sub-question: how integer division is spelled (an operator or a function).
    - D-017 + D-018: checker errors for assignment to a `val` and for redeclaration in the same scope; shadowing in a nested block is still allowed.
    - D-059 + D-028: an out-of-range `v[i]` or `s:substring` panics at run time on every engine (the C runtime gains bounds checks).
    - D-022 + D-037: `println`/`str` print `true`/`false`, and whole floats keep `.0` (`1.0`).
    - D-026: `===` on strings and floats compares values, like the other scalars.
    - D-031: option 1 (`else` is required unless the match is exhaustive over an enum or union; fix the reference).
    - D-032: option 1 (an `if` condition must be `bool`; checker error).
    - D-013 + D-071: remove `null`; absence is only `Option[T]`, and `pop`/`find`/`remove` return `Option[T]` (the self-host uses no `null` at all). This likely makes D-061 (null map keys) moot, and points D-030 (readline at end of input) toward `Option`.
    - D-070: reference semantics for structs (document current behavior; the self-host relies on it).
    - D-056: closures capture enclosing `var`s by reference, per closure instance.
    - D-015 + D-038 (duplicates): `int`/`float` return a plain number for numeric arguments and a `Result` only for string arguments.
    - D-023: `s[i]` returns the i-th rune (code point), consistent with `len` and `for ch in s`.
    - D-025: `"n=" + 1` is a checker error (use interpolation or `str`); strings get lexicographic `<`, `<=`, `>`, `>=` by code point.
    - D-061: moot once `null` is removed; drop the sentence from the reference.
    - D-036: the owner wants command-line arguments delivered through `main`; a design is needed (signature, types, failure modes) before it is recorded.
    - D-030: option 3, `Result` variant: `readline(prompt?)` returns `Result[string, string]`, a `Failure` at end of input (and on read errors), consistent with `int`/`float` on strings. The exact error text is not decided.
    - Union payloads (from 013): both forms are supported. Named fields `Int(n int)` and positional `Int(int)` may each be used, but positional (anonymous) payloads are only allowed when **every** variant of that union uses them; mixing within one union is an error. The owner plans to implement it in the self-host by hand.
    - Struct shorthand (from 013): `Point { x }` means `Point { x: x }`.
    - `for x in map` (from 013): `x` binds the **value** (as in the self-host); this changes LOOP-9, which says key.
    - `self` only (from 013): methods bind `self`; `this` is removed.
    - Braces (from 013): `if`/`else` branches always need braces; the unbraced single-statement branch in the self-host (`Parser.pino:684,688`) is not part of the language (IF-E1 stands).
    - Precedence (from 013): postfix `[i]`, `:` and calls bind tighter than unary `-`/`not`, so `-arr[0]` negates the element (as in legacy; the self-host at `Parser.pino:386` currently binds unary tighter and needs the owner's fix).
    - Trailing commas (from 013): an error in every comma-separated list (`[1, 2,]`, `f(a,)`, `map[...] { 1 -> 2, }`), as in the self-host. Newlines already separate elements, so a trailing comma is never needed.
  - Delayed by owner: named call arguments `f(@name: expr)` (built in the self-host; the owner is unsure about the feature).
  - Pending from 013 (not yet decided): `pub` placement (fields vs top-level, `@pub_testing`), typeless parameter defaults, the `void` type, and `[]T { len/init/cap }` initializers.
  - **Ticket 013 inventory** (`.tickets/013-scan-self-host-syntax/inventory.md`) lists 19 remaining syntax questions. Note: the self-host parser accepts a *subset* (e.g. no comments, interpolation, imports, `?`, `or {}`), so a missing feature means "not implemented yet", not "removed". Only deliberate differences need decisions. What was decided from it and what is still pending are listed above.
  - Deferred by owner: **D-012** (explicit variable type annotations). The owner prefers full inference and likes neither `val x: int` nor `val x int`; leave it open and revisit later.
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
