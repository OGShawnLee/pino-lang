# 008 — Change log

Worker: Claude Sonnet 5.5. Branch `t3/spec-migrate-types-errors`. All six features migrated; no feature left unfinished.

## Files created or edited
- Edited: `spec/grammar.ebnf` (only the sections `struct`, `iface`, `enum`, `union`, `gen`, `err`), `spec/SPEC.md` (only the `State` cell of the six 008 rows, `pending` to `migrated`).
- Created: `spec/features/structs.md`, `interfaces.md`, `enums.md`, `unions.md`, `generics.md`, `errors.md` (each with `Status: pending regeneration (ticket 009)`).
- Created: `conformance/{structs,interfaces,enums,unions,generics,errors}/` (123 `.pino` cases with `.out`/`.err`, plus a `known-failures.txt` per directory).
- Created: `spec/decisions/D-070` to `D-081` and `D-083` (13 files).
- Created: this file.

## Rule counts
Positive rules / error rules (`[no-case]` rules counted in the first number).

| Feature | Rules | Error rules | `[no-case]` | Cases |
| :--- | :--- | :--- | :--- | :--- |
| structs | 14 | 11 | 0 | 25 |
| interfaces | 5 | 5 | 0 | 10 |
| enums | 9 | 1 | 0 | 8 |
| unions | 19 | 7 | 0 | 25 |
| generics | 11 | 9 | 0 | 26 |
| errors | 21 | 7 | 2 (ERR-20, ERR-21) | 29 |

## Test results
- `dotnet test` (full): 728 passed, 0 failed (359 before this ticket; the 123 new cases add 369 tests).
- Checkpoint after every feature was folded into one run per phase: `--filter "Conformance|SpecLint"` passed (439/439 after all six features were in; the only failures before were two coverage-ID typos in my own enum case, fixed).

## Known failures (count of listed cases per engine; every line has a reason)
| Feature | TreeWalk | VM | C |
| :--- | :--- | :--- | :--- |
| structs | 0 | 14 | 6 |
| interfaces | 0 | 5 | 5 |
| enums | 0 | 7 | 1 |
| unions | 0 | 18 | 2 |
| generics | 0 | 12 | 5 |
| errors | 1 | 18 | 6 |

Grouped by cause:
- **VM, structs/unions/enums/interfaces/generic structs (about 56 cases):** the VM has no struct, union or enum support; every such case fails with `RUNTIME ERROR: Value is not callable.`, a NullReferenceException (generic structs/functions), `Stack underflow` (field assignment), or wrong output (`null`/empty).
- **VM, errors (18):** the VM compiler does not support `?` or `or { ... }` ("not supported in bytecode compilation yet"); a `main` returning `Option::None` succeeds silently.
- **C, structs/interfaces/generics:** `self`/`this` is not emitted (`'self' undeclared`); bound methods and function-typed fields (`field not found`, `operation on a struct`); interface-typed parameters, fields and map values (TCC `',' expected`, `';' expected`); generic methods (`invalid type`, `';' expected`); generic interface (`',' expected (got "*")`); a generic function with a lambda (`cannot cast`).
- **C, unions/enums:** a redeclared generic `Option` (`struct or union expected`); `is` inside a generic function (`identifier expected`); `type()` builtin missing.
- **C, errors:** `main` returning `Result`/`Option` leaves a garbage exit code (3 cases: main-failure, main-none, main-success); `?` on Result in `main` (`undefined symbol _Failure_construct`); `or` with `yield` of a string (`cannot cast ... to 'void *'`); `return panic(...)` (`cannot cast from/to void`).
- **TreeWalk (1, runner limitation, not an engine bug):** `errors/panic-exit-code.pino` (rule ERR-5, exit code 101) is listed for TreeWalk and VM because the in-process runner reports only the panic message. It is genuinely checked on C (`exit code 101`).

All static checker rejections (the `err-*.pino` cases) behave identically on all three engines because the checker runs before engine dispatch; none is a known failure.

## Decisions opened (all `Status: open`)
- D-070: struct value vs reference semantics (evidence: tree-walk and C both share the instance; VM unsupported). Required.
- D-071: `null` vs `Option[T]` (cross-linked to the ticket-005 `null` decision, ID unknown at the time; the router should replace the range reference "D-010 to D-029" with the concrete ID). Required.
- D-072: two generic syntaxes, `@generic[T]` vs inline `[T]` (inline only works on struct and union; fn/interface reject it). Required.
- D-073: implicit field access vs parameter/local shadowing (parameter or local wins on TreeWalk and C; no source defines it, so no rule). Required.
- D-074: the reference's `fn f() T => expr` form is rejected by the parser (also for top-level functions; overlaps ticket 007).
- D-075: bare sibling method call inside a method fails on all engines.
- D-076: `instance:field[index]` is a parse error.
- D-077: unknown enum member is only a run-time error on TreeWalk, silent on VM, TCC error on C.
- D-078: omitted struct fields (TreeWalk run-time error on read, C prints 0; proposal 001 is Draft, evidence only).
- D-079: `err:to_string()` from the reference's Tactic 1 example works on no engine.
- D-080: `main` failure handler text/exit code (`Error: <payload>`, `Error: Option::None`; VM and C do not implement it correctly).
- D-081: `panic` output format and backtrace (C prints no backtrace).
- D-083: whether method signatures must match for non-generic interface satisfaction (checker rejects mismatches; no source says so).
D-082 and D-084 to D-089 are unused.

## Notes for the router
- Spot-check candidates (rule, source): STRUCT-5 (reference §7 "Structs", `DataStructureTests.TestStructSelfThisImmediateSync`), UNION-16 (reference §7 "Variant Testing"), GEN-5 (`proposals/004`, `CheckerTests.TestFunctionGenericsExplicitCall`), ERR-8 (reference §8 "The Bubble Operator", `ErrorHandlingTests.TestBubbleOperatorSuccessAndFailure`), ERR-E3 (`CheckerTests.TestUnionBubbleMismatchingErrorTypes`). Proposals cited are only 004 and 005 (plus 001 and 009 mentioned in decisions as evidence). Proposal 009 is not cited as a source anywhere; the `is` rules cite the reference.
- `is`/`is not` is documented in the reference and covered by UNION-15 to UNION-19 and ENUM-9, even though proposal 009 still reads `Proposed`.
- Error rules state the message substring that the current checker produces (ThrowsAny tests do not pin a message); the substring in the `.err` file is longer than the substring in the rule text. If the owner wants messages to stay unspecified, the rules can be weakened to "is rejected" and the `.err` files shortened.
- STRUCT-13's cited test is named `TestMethodCallOnMethodTypedStaticFieldThrows` but asserts success.
- The reference's example `main { ... }` and `case` keyword in §8 Tactic 2 (`case AppError::DatabaseError`) differ from the `when` keyword used everywhere else; I used `when` (as tests do). Not opened as a decision because match belongs to ticket 006.
- Printing a `bool` shows `True`/`False` on all engines; cases avoid printing booleans directly (they use a `yes`/`no` helper). That belongs to ticket 005.
- Printing an enum value directly (`println(Direction::North)`) works on tree-walk and C but only `str()` is asserted by tests, so cases use `str(...)`.

## Unfinished features
None.

## Blockers
None. No edits were needed in `pino-csharp/` or `pino-csharp.tests/`. `tooling/` was copied into the worktree (gitignored, never staged).
