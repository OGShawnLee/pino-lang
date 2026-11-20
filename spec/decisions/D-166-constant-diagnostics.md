# D-166: Constant diagnostics (errors and warnings)

Status: accepted
Raised by: 028
Feature: spec/features/types.md

## Question
Which statically detectable problems are checker errors, and which are warnings?

## Evidence
- Audit 028 section 3.2: the self-host rejects division and remainder by a constant zero (`projects/pino-compiler/modules/Checker.pino:2613-2620, 2634-2641`, message "Division by Zero Error: '10 / 0' will always produce an error at runtime.") and flags `for false` (`Checker.pino:3203-3207`) and statically redundant `match` arms (`Checker.pino:2972-2978, 2987-2990`).
- Audit 028 section 7, question 4 asked whether these are hard errors, warnings or deferred.
- Legacy: `10 / 0` fails only at run time ("Attempted to divide by zero.") and only when executed; the C backend crashes with exit code `-1073741676`.

## Options
1. Constant zero divisor is an error; `for false` and unreachable `match` arms are warnings; warnings never change the exit status.
2. All of them are errors.
3. All of them are warnings.

## Decision
Option 1, decided by the owner on 2026-11-20:
- A division or modulo whose divisor is a compile-time constant zero is a checker **error**.
- `for false` and statically unreachable `match` arms are checker **warnings**.
- Warnings never change the exit status.
- The conformance runner observes errors only, so the warning rules carry `[no-case: ...]`.
