# 006 — Change log

Worker: Claude Sonnet 5.5. Branch `t3/spec-migrate-control-flow`. Spec and conformance only; no compiler or test-project file was touched.

## Files created or edited
Created:
- `spec/features/conditionals.md`, `spec/features/match.md`, `spec/features/entry-point.md`
- `spec/stdlib/builtins.md`
- `spec/decisions/D-030-readline-end-of-input.md` through `D-038-int-float-return-result.md` (9 files)
- `conformance/conditionals/` (7 cases), `conformance/match/` (16 cases + `known-failures.txt`), `conformance/entry-point/` (8 cases), `conformance/stdlib-builtins/` (10 cases + `known-failures.txt`)
- `.tickets/006-spec-migrate-control-flow/changelog.md`

Edited:
- `spec/grammar.ebnf`: only the sections `if`, `match` and `main`.
- `spec/SPEC.md`: only the `State` cell of the 4 rows of this ticket (`migrated`).

Every spec file has `Status: pending regeneration (ticket 009)`.

## Rule counts per feature
| Feature | Rules | Errors | Total | `[no-case]` | Cases |
| :--- | :--- | :--- | :--- | :--- | :--- |
| conditionals (`IF`) | 6 | 2 | 8 | 0 | 7 |
| match (`MATCH`) | 9 | 5 | 14 | 0 | 16 |
| entry-point (`MAIN`) | 4 | 3 | 7 | 0 | 8 |
| stdlib/builtins (`BUILTIN`) | 14 | 0 | 14 | 6 | 10 |

`builtins.md` lists all 16 functions in `## Signatures`. `panic` and the conversions `int`, `float`, `rune`, `str`, `type` have a signature row only and link to `features/errors.md` and `features/types.md`, so they have no `BUILTIN` rule.

## Test totals
- Before: `dotnet test` = 359 passed, 0 failed.
- After: `dotnet test` = 482 passed, 0 failed (123 new conformance and lint theories: 41 cases x 3 engines).
- Checkpoints (`Conformance|SpecLint` filter) passed after each of the four features.

## Known failures (`known-failures.txt`)
conformance/match (all `.out` cases on the VM, one on C):
- VM `stmt-first-match`, `stmt-reference-example`, `stmt-multiple-values`, `stmt-optional-commas`, `stmt-else`: the VM compiles `match` statements to nothing, so no arm runs.
- VM `enum-statement`, `expr-return-and-init`: "Value is not callable" on enum member access `Enum::Member`.
- VM `expr-arrow`, `expr-block-yield`, `expr-block-yield-local`: a `match` expression evaluates to `null`.
- C `expr-block-yield-local`: a `val` declared inside a yield block of a top-level match expression is emitted as an assignment without a declaration (TCC: undeclared).

conformance/stdlib-builtins:
- C `println-args`: only the first argument of `println` with several arguments is printed.
- C `println-returns-null`, `rand-types`, `time-type`, `regex-value`: `type()` is not supported by the C backend (known from ticket 004).
- C `time-value`: `runtime/runtime.c` `pino_time()` uses `QueryPerformanceCounter` on Windows (milliseconds since boot), not the Unix epoch, which breaks BUILTIN-7.
- VM `regex-value`: "Value is not callable" on `r:pattern`.

conformance/conditionals and conformance/entry-point: no known failures on any engine.

## Decisions opened (all `Status: open`)
- D-030: `readline()` result at end of input (required; no conformance case, it hangs).
- D-031: `match` statement without `else` over a non-enum, non-union subject: reference says optional, the checker rejects it, runtime behavior undefined (required). MATCH-E5 is written for the expression form only (proposal 006 section 4.3).
- D-032: non-`bool` `if` conditions are accepted and truthy.
- D-033: `if ... then a else b` accepts mismatched branch types; `match` rejects them.
- D-034: a bare identifier in a `when` arm is a catch-all binding, not a comparison.
- D-035: `else => expr` after an `if` block is accepted by the parser, undocumented.
- D-036: `fn main(a int)` is silently not run by the CLI/tree-walk/VM but runs on the C backend.
- D-037: `bool` prints as `True`/`False` (tests) while the source spelling is `true`/`false`; reference silent.
- D-038: `int(...)` and `float(...)` return `Result` in the implementation, plain numbers in the reference (may overlap a ticket 005 decision).

## `[no-case]` rules
All in `stdlib/builtins.md`:
- BUILTIN-9 `readline`: needs stdin (empty in the runner); EOF is D-030.
- BUILTIN-10 `sleep`: effect is wall-clock time.
- BUILTIN-11 `clear`: effect is on the terminal.
- BUILTIN-12 `read_file`, BUILTIN-13 `write_file`, BUILTIN-14 `file_exists`: depend on file-system state.

`rand` (BUILTIN-5, -6) and `time` (BUILTIN-7) are covered by deterministic property cases (range checks, `type()`, epoch lower/upper bound).

## Entry point evidence
`LANGUAGE_REFERENCE.md` lines 460-471 and `Program.RunFile` (`pino-csharp/Program.cs:391`) agree: the whole file is executed, then `main` is called automatically. The only disagreement is parameters (D-036). The `hasExplicitMainCall` branch of `RunFile` is unreachable in practice because the checker already rejects a top-level `main()` call (MAIN-E1). `main` failure handling (`Failure`/`None` results) is left to `features/errors.md`.

## Observations outside this ticket (not decided here)
- Float literals with 10 or more integer digits (for example `1000000000.0`) fail to lex ("Expected expression, got Illegal"). This belongs to the lexical area (ticket 005). The `time()` case uses integer literals to avoid it.
- `println(1000000.0)` prints `1000000` (float without fraction), and `null` is not usable as an identifier or literal in the tree-walker (`type(null)` fails with "Variable 'null' is not declared"), both for the types/lexical owner.
- `return` with no value inside `fn main` followed by another statement still ran the next statement in the tree-walker probe (`return` then `println("b")` printed both); belongs to functions (ticket 007).
- C keyword identifiers (for example a function named `double`) fail on the C backend; already tracked by D-005.

## Unfinished features
None. All four features are migrated.

## Blockers
None. `tooling/` was copied into the worktree (it is gitignored and not staged).
