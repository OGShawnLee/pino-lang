# D-168: `int`/`float` widening in collection literals

Status: accepted
Raised by: 028
Feature: spec/features/vectors.md

## Question
What is the type of a literal that mixes `int` and `float` elements, such as `[1, 2.5]`?

## Evidence
- Audit 028 section 2.7: the self-host rejects `[1, 2.5]` because every element must equal the first element's type (`projects/pino-compiler/modules/Checker.pino:1552-1580`, run #13a).
- Audit 028 section 7, question 6.
- D-019 / TYPE-9: `int` widens implicitly to `float` in arithmetic and comparison.
- Legacy: `println([1, 2.5])` is accepted.

## Options
1. A vector literal mixing `int` and `float` is `[]float`; the same goes for sets and map values.
2. Reject the mix.

## Decision
Option 1, decided by the owner on 2026-11-20: a vector literal mixing `int` and `float` elements is `[]float` (`[1, 2.5]`), consistent with D-019. The same goes for sets (`set[float] { 1, 2.5 }`) and map values (`map[string, float] { "a" -> 1, "b" -> 2.5 }`).
