# D-171: `%` on floats

Status: accepted
Raised by: 031 (OQ-5)
Feature: spec/features/types.md

## Question
Is `%` defined on `float` operands, and with which semantics?

## Evidence
- Design 031 §12 OQ-5: `029/changelog.md:13`; D-164 enables `//` on floats (TYPE-15) but specifies `%` only for `int` (TYPE-14); the self-host requires `int % int` (`Checker.pino:2627-2647`).

## Options
1. Allow `%` on `float` (and mixed `int`/`float` via D-019) with a floored float modulo, so `a == (a // b) * b + a % b` holds for floats too.
2. Reject `%` on floats as a checker error (`int` only).

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-5): `%` is defined on `float` operands, and on a mix of `int` and `float` with the `int` widened per D-019. It is floored, matching `//` (D-164): `-7.5 % 2` is `0.5` and `7.5 % -2` is `-0.5`, and `a == (a // b) * b + a % b` holds for floats. Normative rule: TYPE-18.
