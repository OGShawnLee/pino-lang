# 005 — Change log

Branch: `t3/migrate-spec-values-ticket-005` (worktree). One commit: `docs(spec): migrate values (lexical, variables, types, runes, strings, equality)`.
All 8 features are migrated; nothing is unfinished; no blockers.

## Files created or edited

Created:
- `spec/features/lexical.md`, `variables.md`, `types.md`, `runes.md`, `strings.md`, `equality.md`
- `spec/stdlib/string.md`, `spec/stdlib/regex.md`
- `spec/decisions/D-010` to `D-029` (17 files, see below)
- `conformance/lexical/` (+ `modules/utils.pino` helper), `variables/`, `types/`, `runes/`, `strings/`, `equality/`, `stdlib-string/`, `stdlib-regex/`, each with `known-failures.txt`
- `.tickets/005-spec-migrate-values/changelog.md`

Edited:
- `spec/grammar.ebnf`: only the sections `lex`, `var`, `type`, `rune`, `str`, `eq` (all other sections untouched)
- `spec/SPEC.md`: only the `State` cell of the 8 rows (`pending` -> `migrated`)

Not touched: `pino-csharp/`, `pino-csharp.tests/`, `pino-csharp-re/`, `runtime/`, `tooling/`, `projects/`, `conformance/STATUS.md`, `LANGUAGE_REFERENCE.md`, `.routing/`, other `spec/` and `conformance/` files.

## Rule and case counts

| Feature | Rules (incl. errors) | Cases | Known failures TreeWalk / VM / C |
| :--- | :--- | :--- | :--- |
| lexical (LEX) | 16 (LEX-1..15, E1) | 18 | 1 / 8 / 2 |
| variables (VAR) | 11 (VAR-1..8, E1..E3) | 14 | 0 / 5 / 3 |
| types (TYPE) | 7 (TYPE-1..7) | 8 | 0 / 0 / 3 |
| runes (RUNE) | 7 (RUNE-1..7) | 11 | 0 / 0 / 7 |
| strings (STR) | 7 (STR-1..6, E1) | 14 | 0 / 6 / 2 |
| equality (EQ) | 13 (EQ-1..13) | 15 | 0 / 12 / 0 |
| stdlib/string (STRLIB) | 18 (STRLIB-1..18; 18 is `[no-case]`) | 19 | 1 / 19 / 2 |
| stdlib/regex (RGX) | 7 (RGX-1..7) | 7 | 0 / 6 / 1 |

## Known failures (summary; the reason for each line is in the `known-failures.txt` of the directory)

- **TreeWalk:** `lexical/optional-commas-import` (parser ends a comma-less import list after the first name); `stdlib-string/len-unicode` (`:len` counts UTF-16 code units: `"🌲":len` is 2, the reference says 1).
- **VM** (experimental, no vector/map/struct/enum/union/module/method/`match` support): every case that needs one of those; also `==` on vectors, maps and structs compares by reference (equality cases); `%=` gives a wrong result; `val` reassignment and string element assignment are not rejected; `str()` of vectors and structs prints `null`.
- **C:** `int` is 32 bit (`int-literal`, `int-64-bit`); the `type()` and `rune()` builtins do not exist (types, runes, strings, regex cases); `str('🌲')` prints `2`; `+=` on strings does not compile; `s[0] = 'x'` fails in TCC (never satisfies an `.err` case); `val` reassignment is not rejected; `:len` counts bytes (`len-unicode`); `:to_cstring()` does not compile; comma-less import list fails like on TreeWalk.

## Known bug capture (`conformance/strings/`)

`interpolate-q-regression.pino` contains exactly `val q = 3` / `println("q = $q")` and expects `q = 3`. **The `Q = 3` bug does not reproduce** on any engine (tree-walk, VM and C all print `q = 3`, also through the CLI), so nothing is listed as a `TreeWalk` known failure. `interpolate-variable.pino` repeats the same two lines as a second check.

## Decisions opened (all `Status: open`, each with tree-walk, VM and C evidence)

| ID | One line |
| :--- | :--- |
| D-010 | Optional commas vs unary minus: `[a -b]` prints `[3]`, not `[5, -2]` (required) |
| D-011 | `rune + rune` produces a string while `'A' + 32` is a rune and `'c' - 'a'` an int; C crashes on `'a' + 'b'` (required) |
| D-012 | Explicit type annotation syntax: neither `val x: int = 10` nor `val x int = 10` parses (required) |
| D-013 | `null` listed as a primitive but no engine has the literal; cross-reference to ticket 008 `Option[T]` (required) |
| D-014 | The `snake_case`/`PascalCase` naming convention is "strict" in the reference but not enforced anywhere |
| D-015 | `int(v)`/`float(v)` return `Result` on every engine; the reference says plain `int`/`float` |
| D-017 | `val` reassignment is a run-time error on tree-walk only; the reference says "Compiler/Checker ERROR"; VM and C accept it |
| D-018 | Redeclaring a name in the same scope: accepted (and may change type) on tree-walk and VM, TCC error on C |
| D-019 | `int`/`float` mixing, `i = 2.5` on an `int` variable, `5 / 2`, `1 == 1.0`; C truncates, interpreters promote |
| D-020 | String escape sequences are undocumented (`\n \t \r \" \\ \$` work; unknown escapes stay literal) |
| D-021 | `$` is an identifier character, so `"$x$y"` reads one variable `x$y` |
| D-022 | `println` prints `True`/`False` and `1` for `1.0`; the reference writes `true` and `42.0` |
| D-023 | String indexing `s[i]` is undocumented; UTF-16 units on tree-walk, bytes on C |
| D-025 | `"n=" + 1`, `1 + "n"` concatenate; `"a" < "b"` throws a .NET `FormatException` on tree-walk and VM, is `True` on C |
| D-026 | `===` on strings and floats is undocumented; tree-walk gives `True`, C does not compile |
| D-028 | `substring` out of range: error on tree-walk, clamped on C |
| D-029 | Regex syntax and invalid patterns: .NET regex on tree-walk (throws), own matcher on C (accepts `[`) |

IDs D-016, D-024 and D-027 were reserved while drafting and are intentionally unused (never renumbered). No decision is `accepted` or `rejected`.

## Judgment calls the router may want to review

- **Rule vs. decision.** I wrote a rule (plus case and `known-failures.txt` lines) whenever the reference is explicit and the engine behavior looks like a missing feature or a defect: comma-less imports (LEX-12; all three engines fail), rune-based `:len` (STRLIB-1; tree-walk and C fail), `val` reassignment (VAR-E1; VM and C fail), 64-bit `int` (C fails). I opened a decision instead when the reference describes something the engines do differently on purpose or consistently (`int()`/`float()` returning `Result` -> D-015), when the reference is silent, or when a required decision covers the behavior.
- **No rule for the required-decision topics.** `rune + rune` (D-011), comma-less `[a -b]` (D-010), type annotations (D-012) and `null` (D-013) have no rule; the neighbouring behavior is covered (RUNE-2, RUNE-3, LEX-9).
- **Observing runes.** `println` of a rune and of a boolean is not asserted by any xUnit test for runes, so rune cases print through `str(...)`, distances or `==`. Boolean output uses `True`/`False`, asserted by `StdlibTests` (see D-022).
- **Message text.** `.err` files use the engine message text (`Cannot reassign constant variable`, `Cannot assign type 'string' to target of type 'int'`, `expected type 'int', but got 'string'`, `Cannot assign to index`, `Expected Identifier`); the rules state only that the program is rejected, because no source documents the wording.
- **`match` cases** in `lexical/` carry an `else` branch because the checker requires one for non-enum values, although the reference says `else` is optional (belongs to ticket 006).
- **Grammar.** Productions reference `expression`, `statement`, `block` and `type_expr` forms that other tickets define; `block` is deliberately not defined in section `var` to avoid clashing with ticket 006.

## Checkpoint and final results

- After each feature the conformance + lint filter passed (final: 388 passed, 0 failed).
- Full `dotnet test` in `pino-csharp.tests/`: 677 passed, 0 failed (baseline before this ticket: 359 passed; +318 = 106 new cases x 3 engines; the spec lint tests are unchanged in number).
