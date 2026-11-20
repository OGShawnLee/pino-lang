# D-164: Integer division `//` and the floored modulo

Status: accepted
Raised by: owner (2026-11-20)
Feature: spec/features/types.md

## Question
How is integer division spelled, and how do `//` and `%` treat negative operands?

## Evidence
- D-019 closed with "how integer division is spelled remains open".
- `LANGUAGE_REFERENCE.md` defines no integer-division operator.
- Legacy: `//` does not parse (`PARSER: Expected expression, got Operator: / (Division)`), and `%` truncates toward zero: `-7 % 2` is `-1` and `7 % -2` is `1` on every engine.
- Existing conformance cases use `%` only with non-negative operands (checked 2026-11-20), so no `.out` changes.

## Options
1. `//` is floor division and `%` the matching floored modulo (Python semantics), so `a == (a // b) * b + a % b` always holds.
2. `//` truncates toward zero and `%` keeps the sign of the dividend (C semantics).
3. No integer-division operator; use `int(a / b)`.

## Decision
Option 1, decided by the owner on 2026-11-20:
- `//` is floor division and `%` is the matching floored modulo, so `a == (a // b) * b + a % b` always holds. `-7 // 2` is `-4`, `-7 % 2` is `1`, `7 // -2` is `-4`, `7 % -2` is `-1`.
- Both operators sit at precedence level 7 with `*` and `/`.
- `//=` exists.
- `//` also works on floats and returns the floored float (`7.0 // 2` is `3.0`); with mixed `int`/`float` operands the operands widen per D-019.
- Division or modulo by zero follows the existing `/`/`%` rules (D-166 for a constant zero divisor).
- This changes `%` on negative operands to the floored result.
