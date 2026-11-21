# D-060: Wording of the vector validation errors

Status: accepted
Raised by: 007
Feature: spec/features/vectors.md

## Question
The reference gives exact messages for the static vector validation errors, and the checker emits different ones. Which wording is normative?

## Evidence
- Reference: §4 lists `TYPE CHECK ERROR: Vector length cannot be negative`, `TYPE CHECK ERROR: Vector capacity cannot be negative` and `TYPE CHECK ERROR: Vector capacity cannot be less than length` (`LANGUAGE_REFERENCE.md:305-307`).
- Tests: `DataStructureTests.TestVectorCompileTimeValidation` (`DataStructureTests.cs:578-600`) only asserts that `Exception` is thrown for the three cases; it does not check the text.
- Implementation: the checker emits `Vector 'len' cannot be negative, got <n>.`, `Vector 'cap' cannot be negative, got <n>.` and `Vector 'cap' (<c>) cannot be less than initial 'len' (<l>).` (`pino-csharp/Checker/Checker.Expression.cs:350-356`).

Snippet and output (identical on all engines, the checker is shared):

```pino
val items = []int { len: -5, init: 0 }
```

`TYPE CHECK ERROR: Vector 'len' cannot be negative, got -5.`

The rules VEC-E1 to VEC-E3 only require the `TYPE CHECK ERROR` prefix plus the words common to both wordings (`cannot be negative`, `cannot be less than`).

## Options
1. Make the reference's wording normative and change the checker.
2. Make the checker's wording normative and update the reference.
3. Keep requiring only the common words (current rules).

## Decision
Option 3, decided by the owner on 2026-11-20 (ticket 031, OQ-11): the spec keeps requiring only the phrases both wordings share (`cannot be negative`, `cannot be less than`). VEC-E1 to VEC-E3 are unchanged.
