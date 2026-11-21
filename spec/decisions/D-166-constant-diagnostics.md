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

**Extended on 2026-11-20 (ticket 031, OQ-7 and OQ-8), Option 1 for each, decided by the owner:**
- **Compile-time constant divisor (OQ-7).** A divisor is a compile-time constant zero when the checker can fold it to `0` or `0.0` from literals, constant arithmetic and unary operations, and immutable `val`s initialized with constant expressions, so `val z = 0` followed by `1 / z` is a checker error. A `var`, a parameter and a call result are never folded. Normative rule: TYPE-E1.
- **Statically unreachable `match` arms (OQ-8).** A `match` arm is statically unreachable, and warns, when it is (a) a duplicate literal, enum or union pattern, (b) an arm after the catch-all `else` or after every enum member or union variant is already covered, or (c) an arm whose pattern can never equal a constant-folded subject (`match 1 { when 2 { ... } }`). A bare identifier is not a catch-all pattern: it is a checker error (D-034). All of these stay warnings, never errors. Normative rule: MATCH-11.

**Extended on 2026-11-20 (033 follow-up), decided by the owner:** confirmed that `else` is the only catch-all arm in `match`, so "after a catch-all" in MATCH-11 means after `else` (a bare identifier in a `when` arm is a checker error, D-034).
