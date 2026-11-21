# D-170: Run-time division and modulo by zero

Status: accepted
Raised by: 031 (OQ-4)
Feature: spec/features/types.md

## Question
What happens when `/`, `//` or `%` runs with a divisor of zero that the checker could not fold?

## Evidence
- Design 031 §12 OQ-4: `029/changelog.md:13`; D-164 cites "existing `/`/`%` rules", which do not exist in `spec/features/types.md`; the legacy TreeWalk engine throws `"Attempted to divide by zero."` and the C backend crashes.
- D-166 only covers a divisor that is a compile-time constant zero (a checker error, TYPE-E1).
- ERR-3 and ERR-5: a `panic` halts the program and exits with code 101.

## Options
1. Run-time division or modulo by zero (`int` or `float`) panics (exit code 101) on all engines.
2. `int` division or modulo by zero panics, while `float / 0.0` yields IEEE `±Inf` or `NaN`.
3. Leave the run-time behavior unspecified until the IR and runtime are designed.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-4): run-time division or modulo by zero panics on every engine, for `/`, `//` and `%` and for `int` and `float` operands alike: it ends the program like `panic` does (ERR-3, ERR-5). Float division by zero does not yield `Inf` or `NaN`. A divisor that is a compile-time constant zero stays a checker error (D-166). Normative rule: TYPE-E4.
