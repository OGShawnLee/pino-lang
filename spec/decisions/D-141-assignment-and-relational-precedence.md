# D-141: Precedence of assignment, `in`/`not in`, and `is`/`is not`

Status: accepted
Raised by: 016
Feature: spec/features/variables.md, spec/features/types.md, spec/features/vectors.md, spec/features/maps.md, spec/features/unions.md

## Question
Is assignment an expression or statement-only, and at which precedence levels do `in`/`not in` and `is`/`is not` bind?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 2 and 9 (OQ-2).
- Spec: `spec/grammar.ebnf` previously modeled assignment as a statement (`assignment`) and did not place `in`/`not in` or `is`/`is not` in the precedence hierarchy (`UNION-18` only showed `is` is tighter than `and`).
- Implementation: legacy (`pino-csharp/Parser/Parser.Expression.cs:169,347,366,384`) and self-host (`projects/pino-compiler/modules/Parser.pino:42,47,506`) parse assignment as a right-associative expression at the loosest level, `in`/`not in` at relational `<` level, and `is`/`is not` at equality `==` level.

## Options
1. Confirm `in`/`not in` at `<` level, `is`/`is not` at `==` level, and make assignment statement-only.
2. Confirm `in`/`not in` at `<` level, `is`/`is not` at `==` level, and keep assignment (`=`, `+=`, `-=`, `*=`, `/=`, `%=`) as a right-associative expression at the loosest precedence level.

## Decision
Option 2: assignment (`=`, `+=`, `-=`, `*=`, `/=`, `%=`) is a right-associative expression at the loosest precedence level (`a = b = 0` is valid), `in`/`not in` bind at the `<` level, and `is`/`is not` bind at the `==` level, decided by the owner on 2026-11-19 (ticket 016, OQ-2).

**Extended on 2026-11-21 (AD-003 / 043, MQ-4 Option 2, MQ-16), decided by the owner:** `===` and `!==` are removed from equality precedence level 4 (`TYPE-13`, `D-178`), and target path subexpressions in compound and nested assignments (`v[f()] += 1`, `a:b[f()] = g()`) are evaluated **exactly once** left-to-right before reading or mutating the target slot (`VAR-4`, `D-182`). See `D-178-remove-identity-operators.md` and `D-182-union-payload-and-loop-variable-immutability.md`.