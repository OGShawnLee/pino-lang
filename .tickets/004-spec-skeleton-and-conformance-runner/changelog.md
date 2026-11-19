# 004 — Change log

## Files created (all new; nothing existing was edited)

Spec:
- `spec/SPEC.md` (index, source-of-truth rule, 21 feature rows, 5 stdlib rows, conventions)
- `spec/TEMPLATE.md`
- `spec/grammar.ebnf` (21 section markers; only `loop` has productions)
- `spec/features/loops.md` (14 rules LOOP-1..LOOP-14, `Errors: None documented.`)
- `spec/decisions/README.md`, `spec/decisions/TEMPLATE.md`
- `spec/decisions/D-001-loop-header-commas.md`
- `spec/decisions/D-002-it-as-call-argument.md`
- `spec/decisions/D-003-condition-loops-undocumented.md`
- `spec/decisions/D-004-map-iteration.md`
- `spec/decisions/D-005-c-keyword-identifiers.md`

Conformance:
- `conformance/README.md`, `conformance/STATUS.md` (generated)
- `conformance/loops/known-failures.txt`
- `conformance/loops/*.pino` + `.out`: 21 cases (infinite-break, infinite-return, times-implicit-it, times-it-call-argument, in-range, in-vector, in-string-runes, in-string-rune-type, in-string-unicode, in-string-char-keyword, indexed-vector, indexed-string, indexed-range, in-map-keys, in-map-pairs, break-in-vector, continue-times, continue-indexed-string, while-condition, while-bool-variable, nested-loops). No `.err` case exists because the sources document no loop error (the `.err` path was verified with a temporary probe, since reverted).

Runner (`pino-csharp.tests/Conformance/`):
- `ConformanceRunner.cs`, `ConformanceTests.cs`, `SpecLintTests.cs`

Ticket: this `changelog.md`.

## Test totals
`dotnet test` in `pino-csharp.tests/`: 359 passed, 0 failed (289 existing + 63 conformance theory cases = 21 cases x 3 engines, + `GenerateStatusReport` + 6 lint facts). `conformance/STATUS.md`: loops TreeWalk 20/21, VM 13/21, C 17/21.

## Known failures recorded (`conformance/loops/known-failures.txt`): 13 total
TreeWalk (1):
- `times-it-call-argument.pino`: D-002, `it` inside a call argument is parsed as an implicit lambda (prints `Pino.PinoLambda`).

VM (8):
- `times-it-call-argument.pino`: D-002 (prints `null`).
- `in-vector.pino`, `indexed-vector.pino`, `break-in-vector.pino`: NullReferenceException iterating a vector literal.
- `in-map-keys.pino`, `in-map-pairs.pino`: map loop body never runs (prints 0).
- `continue-times.pino`: the VM compiler has no `continue` (prints 15 instead of 9).
- `infinite-break.pino`: the VM compiler has no `break`, so the loop never ends (10 s timeout).

C (4):
- `times-it-call-argument.pino`: D-002 (TCC "cannot cast 'struct <anonymous>' to 'struct <anonymous>'").
- `in-string-char-keyword.pino`: D-005, `char` emitted unmangled (TCC "too many basic types").
- `in-string-rune-type.pino`: `type()` builtin unsupported by the C backend ("implicit declaration of 'type'").
- `in-string-unicode.pino`: C iterates string bytes, not code points (8 iterations for `"🌲Pino"` instead of 5). No decision opened: LOOP-6 is documented, so this is a plain bug.

## Decisions opened (all `Status: open`)
- D-001: commas optional in loop header `for i x in v`? (required by the task; no rule or case for the comma-less form)
- D-002: `it` in `for N` bodies used as a call argument becomes an implicit lambda (the reference's own `println("... $it")` example is broken on every engine).
- D-003: the condition loop `for cond { }` is asserted by tests but not in the reference's "4 variants" (LOOP-13 is test-sourced).
- D-004: map iteration is test-backed but undocumented in §5; order unspecified (LOOP-9, LOOP-10).
- D-005: identifiers that are C keywords (`char`, the reference's own loop variable) break the C backend.

## Deviations and judgment calls
1. **Extra `-I<repo root>` for TCC.** The generated C contains `#include "runtime/runtime.h"`. The CLI resolves it only because it writes `pino_output.c` into the repo root (its CWD). The runner writes to a temp directory (as the ticket requires), so it passes `-I"<root>"` in addition to the arguments of `Program.cs` line 621. No change to `pino-csharp/` was needed.
2. **Timeout scope for C:** the 10 s budget applies separately to the TCC compile and to running the produced exe.
3. **In-process engines cannot be killed on timeout.** TreeWalk and VM run on a 64 MB-stack background thread; on timeout the runner reports `timeout` and abandons the thread (it is a background thread and dies with the test host). Today only `VM infinite-break.pino` triggers this, so one spinning thread stays alive for the rest of that test run (extra CPU load, no output).
4. **`PinoAssertException`/other exceptions** use `Exception.Message`; `PinoPanicException` uses `PanicMessage`. `main` returning `Failure`/`None` is turned into a failure with the same text `Program.HandleMainResult` prints.
5. **Rules added beyond the six listed topics:** LOOP-8 (`for i, v in N`), LOOP-9/10 (map iteration), LOOP-13 (condition loops) and LOOP-14 (nesting) are asserted by existing xUnit tests (source rule (b)), so they are specified; the reference-vs-tests gaps are in D-003 and D-004.
6. **Lint extras:** SpecLintTests also reports a case whose first line is not a `# covers:` header (reported under the "covered IDs exist" check). Rule lines are only read inside `## Rules` and `## Errors`, so `- D-001: ...` lines under `## Open questions` are never mistaken for rules.
7. LOOP-12 generalizes `continue` beyond the string loop that the cited test uses; the test is the only source that mentions `continue`.
8. `spec/TEMPLATE.md` was written exactly as given in the ticket; the conventions that the ticket lists under it live in `spec/SPEC.md` ("Conventions") instead.

9. **`.gitignore` ignores `*.out`.** The 21 `conformance/loops/*.out` files were staged with `git add -f` (explicit paths only). `conformance/README.md` documents this for future tickets.

## Build warnings
None from `pino-csharp.tests/Conformance/` (checked with `dotnet build --no-incremental`). The build shows 44 pre-existing warnings elsewhere (for example CS8605 in `UnionTests.cs`).

## Verification run by the worker
- Temporary probes (all reverted): changed `.out` (theory cases fail on every engine), a probe line in known-failures (`Known failure now passes: remove ...`), `LOOP-99` uncovered (lint names `LOOP-99`), `LOOP-98 [no-case]` (lint passes), a temporary `.err` case (passes on all three engines).
- No `.c`/`.exe` left in the repo and no `pino_output.c`; the temp directory `pino-conformance/` is emptied after every C run.

## Blockers
None. `pino-csharp/` was not touched.
