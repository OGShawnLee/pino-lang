# D-172: `int` to `float` widening in annotated declarations, arguments and returns

Status: accepted
Raised by: 031 (OQ-9)
Feature: spec/features/types.md

## Question
Does an `int` value widen to `float` in `val x float = 1`, as a call argument and as a returned value?

## Evidence
- Design 031 §12 OQ-9: `029/changelog.md:21`; D-019 widens `int` to `float` in arithmetic and comparison; D-168 widens `int` to `float` in collections; D-012 does not mention `val x float = 1`.
- D-033 and D-050 widen `int` and `float` branches and returns to `float`.

## Options
1. Allow `int` to `float` widening in annotated `val`/`var`, function arguments and explicit `float` returns (never `float` to `int`).
2. Require `val x float = 1.0` (widening only in arithmetic, comparisons, branches and collections).

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-9), extending D-012 and D-019: an `int` widens to `float` in an annotated `val` or `var` initializer (`val x float = 1`), as an argument for a `float` parameter, and as the value returned by a function declared to return `float`. A `float` never narrows to `int` in those positions. Normative rules: TYPE-19 and TYPE-E3.
