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
- **Parser batch (dispatched 2026-11-20, Hydrogen Medium, worktrees):** the owner approved the 016 design with amendments A1–A5 (see the end of `design.md`):
  - A1: column-1 recovery for an unclosed `{`
  - A2: half-typed code parses into useful nodes
  - A3: edit-locality property test
  - A4: the checker must be error-tolerant too
  - A5: Rust/Elm-style rendered diagnostics plus Pino's voice

  Done (integrated by rebase + fast-forward, not pushed; main: legacy 1685/1685, `-re` 1369/1369, 0 warnings, format clean):
  - [x] `021-re-parser-syntax-tree` (`1deed2d`):
    - 90 sealed node types in `Pino.Compiler/Syntax/`, with `GetChildren`/`DescendantTokens` and no reflection
    - `PN2001`–`PN2020` and `LexResult.FindTokenIndex`
    - all 9 A2 shapes tested
    - Visibility is a `VisibilitySyntax` node, because `@pub_testing` lexes as `@` + identifier.
  - [x] `025-re-diagnostic-renderer` (`17104a2`):
    - `DiagnosticRenderer`, `DiagnosticRenderOptions`, `DiagnosticVoice` and `DisplayWidth`
    - 20 golden cases, each with a color twin
    - end-to-end and robustness tests on the lexer's real diagnostics
    - `PinoVoice.Notes` is `FrozenDictionary.Empty`. **The owner writes Pino's lines** by turning it into `new Dictionary<string, string> { ["PN1011"] = "..." }.ToFrozenDictionary()`; `PinoVoiceTests` checks that every key is a real code.
    - No CLI prints it yet; the future `-re` driver will.
  - [x] `026-spec-match-statement-block-arms` (`85f5095`):
    - D-148 and MATCH-E7
    - `match_statement` has block-only arms in `grammar.ebnf`
    - The self-host scan found no statement-position arrow arms.
    - 3 known-failure lines

  - [x] `022-re-parser-expressions-and-types` (`150acf2`, Hydrogen **High**; the Medium run was cancelled per the owner's model rule):
    - `Pino.Compiler/Parsing/` Pratt parser with the 9-level table, types, patterns, and a temporary block subset (expression, `yield`, `return` per D-142; marked `// 023:`)
    - D-010, D-055 (speculative type arguments with rollback), D-058, D-076, D-140–D-145 and D-147
    - `PN2010` `=>` recovery keeps both tokens (`SecondaryArrowToken` added to 5 nodes)
    - 114 tests with an S-expression printer and a token-accounting assertion
    - Main: `-re` 1483/1483, 0 warnings.
    - Checker hand-offs: assignment-target validity; `fns[i](9)` parses as type arguments and must be reinterpreted as index-then-call when `i` is a value.

  - [x] `023-re-parser-statements-and-declarations` (`70302c1`, Hydrogen High):
    - full statements and declarations, the public `Parser.Parse(SourceText|LexResult)`, sync recovery, A1, and the statement/declaration rows of A2
    - New `MatchStatementSyntax` (D-148); `assert` parens are optional.
    - Every `PN2001`–`PN2020` code has a test.
    - Smoke test: the self-host `Parser.pino` and all 466 conformance files parse with no exception and full token accounting; 439 of them have 0 diagnostics.
    - The four open owner decisions are parsed **permissively** with no diagnostic (marked `// open decision:`): standalone blocks, `void` as a type, `pub`/`@pub_testing` on declarations and fields, and typeless defaults. The checker will enforce whatever the owner decides.
    - Main: `-re` 1980/1980, 0 warnings.
    - Spec nits found:
      - `grammar.ebnf` `program_item` still allows `val IDENT [type_name] = expr`, contradicting D-012 and `variable_decl`
      - no `assert_statement` production exists (the parser follows legacy and the self-host: `assert <expr>`)

  - [x] `024-re-parser-corpus-and-fuzzer` (`bacb561`, Hydrogen High). **The `-re` parser is complete.**
    - **Corpus:** 508 files. Every positive (`.out`) conformance case parses with 0 diagnostics; 22 `projects/` files are allowlisted with exact codes and reasons, and stale entries fail the test.
    - **Fuzzer:** 16,276 exhaustive inputs, 25,000 seeded inputs and 9 nesting attacks at 10,000 levels; 0 failures, worst case 145 ms.
    - **Edit locality (A3):** 3,732 edits, 566 verified, 0 violations. 3,166 were skipped because no column-1 declaration follows the edit, which is common in script-style conformance files.
    - **Parser fixes the fuzzer found:**
      - depth guards (`PN2020`) on blocks, vectors, lambdas and match bodies, which were stack overflows at 10,000 levels
      - one parser diagnostic per offset
      - inline-generic recovery dropping tokens
    - Main: `-re` 2515/2515 in 10 s, 0 warnings, including the owner's uncommitted `c-compiler` edits.

  Next: the `-re` checker design (AD-001; must satisfy A4), or a `-re` CLI driver so `DiagnosticRenderer` output and Pino's voice show up in the terminal. Owner's choice.
*(None)*

## Architecture decisions
- **AD-001 — Single IR, two consumers (accepted by owner, 2026-11-19).** `pino-csharp-re` lowers the checked AST once into a Pino IR (TACKY-style, like `projects/pino-compiler/`). Two consumers read that IR: an IR interpreter/VM (dev loop, REPL, WASM playground, platforms without TCC) and a C emitter (TCC for dev builds, GCC/Clang for release). No engine re-implements semantics from the AST. Legacy `pino-csharp/` (Tree-Walk, VM, `TranspilerC`) stays as the reference oracle and is retired once `-re` matches Tree-Walk on the conformance suite. Legacy VM is frozen: no new investment.

- [x] **`013-scan-self-host-syntax` (Gemini 4 Hydrogen Medium, read-only):** 79 syntax forms inventoried across self-host, legacy and spec (`.tickets/013-scan-self-host-syntax/inventory.md`); router spot-checked. The owner settled most of its questions (see the decision batch below).

## Batch 014–019 (completed while the owner was away; integrated by rebase + fast-forward, not pushed)
- [x] **014-spec-record-syntax-decisions** (`d344e1b`, Hydrogen Medium): closed D-001, D-035, D-051, D-052/074, D-053, D-058, D-072, D-090–D-093, and created accepted D-100–D-108 (arrow, fn types, sets, union payloads, struct shorthand, `self`, braces, precedence, trailing commas). 101 cases rewritten (no `.out` changed), 22 new (10 `.out`, 12 `.err`), new `spec/features/sets.md`, 247 known-failure lines (TreeWalk 113, VM 49, C 85). Legacy suite: 1577/1577 pass in main. Owner review:
  - the D-072 interpretation: `@@Generic` keeps every old decorator capability, including fns, unions, interfaces and `T is Shape` bounds
  - some `.err` substrings are weak (for example `Expected`); the `-re` parser's `PN2xxx` messages should replace them
- [x] **015-spec-record-semantics-decisions** (`4da1d88`, Hydrogen Medium): closed D-011, D-013/071, D-015/038, D-017, D-018, D-019 (integer-division spelling still open), D-022/037, D-023, D-025, D-026, D-028, D-030 (error text undecided), D-031, D-032, D-056, D-059, D-061 (moot), D-070, and created D-120 (`for x in map` binds the value). 10 cases rewritten, 42 `.out` updated (`true`/`false`, float `.0`, `Option` returns), the obsolete `println-returns-null` case removed, 19 new cases. Known-failure lines: TreeWalk 54, VM 34, C 52. Legacy suite 1631/1631 in main.
  - Tech debt: RUNE-E2 (`rune + rune` error) has `[no-case]` because the legacy C backend stack-overflows in `ProcessStringAddition` and crashes the test host. `ConformanceTests.RunCase` runs every engine before consulting `known-failures.txt`, so one crashing engine takes down the whole run. Fix in the runner: skip known-failure engines up front, or isolate them in a separate process.
  - The 019 corpus positive assertion classified `STR-E5` (a checker error) as a lexer error. Fixed in `7f080fa` with an explicit allowlist of lexer-error rules. Main: `-re` 1257/1257.
- [x] **019-re-corpus-expected-error-cases** (`71fcba7`, Hydrogen Medium): `.err` conformance cases are skipped by the lexes-clean corpus test but still go through the well-formedness invariant, `PN1016` (runes only) was added to the error-token invariant, and a positive assertion checks that every lexer-error case (LEX-E3, RUNE-E1, STR-E2..E4) actually produces a diagnostic. Main: `-re` 1221/1221, format clean.
- [x] **016-re-parser-design** (Neon Medium, read-only; router-verified): `.tickets/016-re-parser-design/design.md`. Proposes a typed immutable AST plus the lossless `LexResult` (no Roslyn green/red trees), recursive descent with a 9-level Pratt expression parser, `TokenFlags` predicates for D-010/D-058/D-001/trailing commas, sync-set recovery with `PN2001`–`PN2020`, no incremental parsing yet, and a 4-ticket split (019 AST + `->` token, 020 expressions/types, 021 statements/declarations, 022 corpus + fuzzer). **Owner answered OQ-1..OQ-8 on 2026-11-19** (table at the end of `design.md`). The answers are recorded in `spec/` by ticket 020; overall design approval is pending before the parser tickets (021+).
- [x] **020-spec-record-parser-decisions** (`42fac13`, Hydrogen Medium): new accepted D-140 (`or { }` postfix at `?` level), D-141 (assignment is a right-associative expression at the loosest level; `in` at `<`, `is` at `==`), D-142 (OQ-3 revised 2026-11-20: `return` at the end of a line continues; bare only before `}`/EOF), D-143 (`return` in `match` expression block arms; `when X -> return` is MATCH-E6; ERR-E5 now allows `yield` in match arms), D-144 (newline before `(`), D-145 (untyped lambda params need commas), D-146 (positional payloads need commas), D-147 (`set[`/`map[` always collection forms; brace-less empty `map[K, V]`); D-055 and D-076 accepted; D-072 confirmation line. `grammar.ebnf` has the full 9-level precedence table. 14 new rules, 2 reworded, 17 new cases (15 `.out`, 2 `.err`), 41 known-failure lines (TreeWalk 13, VM 14, C 14). Main: legacy 1682/1682, `-re` 1291/1291. Router notes: the `.err` substrings are weak (`err-arrow-return`: "Expected expression"; `err-index-set-var`: "Expected") and should be tightened once PN2xxx messages exist; SET-4 mentions a nonexistent `xs:set(...)` member (harmless, fix on the next spec touch); a few known-failure reasons name the nearest decision rather than the arrow change (D-100).
  Original questions:
  - `or { }` precedence
  - `in`/`is`/`=` levels
  - whether a newline ends a bare `return` or a postfix `(`
  - comma-less untyped lambda params `fn (n m)` (ambiguous with `fn (n Type)`; the self-host misparses it)
  - `Variant(A B)` ambiguity
  - `set[i]` vs `set[T]`
  - D-055/D-076 and the pending 013 items.
- [x] **017-repo-cleanup-inventory** (`69110fc`, Hydrogen Medium): `ARCHITECTURE.md` rewritten (legacy pipeline, `-re` as planned per AD-001, self-host, spec/conformance; router checked its paths). 39 leftover candidates in `.tickets/017-repo-cleanup-inventory/inventory.md` (18 duplicates, 13 orphans, 6 stale, 1 superseded, 1 generated), almost all in the gitignored `.context/`. A commented-out deletion block awaits owner approval. Note: 14 of the "exact duplicates" differ only in line endings (LF vs CRLF).
- [x] **018-re-lexer-apply-decisions** (`d62e6c7`, Hydrogen Medium): D-090 (last 3 quotes close), D-091 (`PN1016`, control characters, shown as `U+XXXX`), D-093 (`PN1017`, keyword after `$`), D-092 tests tagged, `->` lexed as `Arrow` and `=>` split into `Assign` + `Greater`. `-re`: 1172 tests pass in the main checkout, build has 0 warnings, format is clean. The router reproduced every spec check with a probe. Notes:
  - Gemini (Antigravity, updated version) ends its turn while a long command is still running; this needed two nudges. Always check the branch before trusting a "completed" report.
  - Line endings: fixed by `.gitattributes` (`ea11e10`).
  - Nit: the PN1017 message suggests `$(if)`, which is not a meaningful expression.
- Owner decision (2026-11-19): existing conformance cases are rewritten into the decided language; legacy failures go to `known-failures.txt` citing the decision.

## Next steps
- [ ] **`-re` Pino IR design (AD-001):** After `-re` parser + checker, design the IR and lowering; add `-re` as a conformance engine. Precompile Boehm GC once for faster `--c` builds.
- [ ] **Pending self-host corrections (owner, by hand; never ticketed to workers):**
  - D-020: double the backslashes in regex string literals (`"\+"` → `"\\+"`, etc.), because unknown escapes are now errors. Files: `projects/pino-compiler/modules/Lexer.pino` and `projects/c-compiler/modules/lexer.pino`. The `-re` corpus test skips exactly these two files until they are fixed.
  - Precedence: `Parser.pino:386` binds unary `-`/`not` tighter than postfix `[i]`/`:`; per the decision, `-arr[0]` must negate the element.
  - Braces: `Parser.pino:684,688` accept unbraced `if`/`else` branches; per the decision, braces are always required.
  - Union payloads: support positional `Variant(Type)` alongside named `Variant(name Type)`, all-or-nothing per union.
  - Arrows: the self-host source still writes `=>` in about 137 places (match arms such as `Checker.pino:47`, map entries). `=>` has left the language (`->` everywhere); this matters once `-re` replaces legacy as the self-host's compiler.
  - (OQ-3 needs no fix: on 2026-11-20 the owner revised it so `return` at the end of a line continues onto the next line, keeping `Parser.pino:264` valid.)
  - OQ-5: untyped lambda parameters need commas. `main.pino:20` `fn (n m)` becomes `fn (n, m)`, and `Parser.pino:925-936` currently reads `fn (n m)` as one param `n` of type `m`.
  - Old syntax found by the 024 corpus (the exact files are in `ParserCorpusTests.Allowlist`; each file's entry disappears from the allowlist once it's fixed, because stale entries fail the test):
    - map entries `k: v` → `k -> v` (MAP-E4), in `Parser.pino`, `Lexing.pino`, `Environment.pino` and several `test/*.pino`
    - `@generic[...]` → `@@Generic[...]` (D-072), in `test/union_equality_test.pino`, `union_payload_test.pino` and `vector_generic_mapping_test.pino`
    - `=>` → `->` (see the arrows item above)
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
- [x] **Repository cleanup** (`4ca2ce0`, owner-approved from the 017 inventory): untracked `.idea/`; deleted `.vscode/tasks.json`, `build_wasm.ps1` and `projects.cmd`; moved `game-proposals/` to `pino.games/proposals/`. On disk only: removed the `.context/` doc and proposal copies (6 unreferenced images kept, still gitignored), `scratch_test_c.exe`, `issue.md` and the empty `.gemini/`. The owner's private images in `.vscode/` stay gitignored on purpose: never move, track or name them. `.gitattributes` (`*.cs text eol=lf`) added in `ea11e10`.
- [ ] **`LANGUAGE_REFERENCE.md`:** close the unterminated code fence at line 219; the file is now out of date versus `spec/` (arrows, `null`, printing, etc.) and needs a rewrite or retirement.
- [ ] **Data Science & FFI:** Design n-dimensional array/tensor primitives and standardize C FFI (`proposals/ffi_standardization_proposal.md`).

## Architectural gaps & tech debt
- [ ] **Checker error tolerance (design 016 A4):** the `-re` checker design must give error and missing nodes an `error` type that is compatible with everything and never produces further diagnostics, so intellisense keeps working past a mistake.
- [ ] **`TranspilerC.cs` monolith:** 235 KB single file with CS8604/CS0184 compiler warnings.
- [ ] **PinoVM parity** *(won't fix per AD-001; legacy VM frozen)*: VM lags behind Tree-Walk and C Transpiler on structs, unions, and complex stdlib methods.
- [ ] **Language specs:** Decide on explicit variable type annotations (`val x: int = 10`), struct value-vs-reference semantics, and `null` deprecation vs `Option[T]`.
- [ ] **Self-host test drift:** `projects/pino-compiler/test/checker_test.pino` and `parser_test.pino` expect old `Expression::Constant(int)` instead of `Constant` union.
