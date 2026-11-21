# D-176: No user-defined overloading

Status: accepted
Raised by: 031 (OQ-37)
Feature: spec/features/functions.md

## Question
May a program declare several functions with the same name and different parameter lists?

## Evidence
- Design 031 §12 OQ-37: `VECLIB-11, 13, 15, 23` (1-argument and 2-argument callbacks); `BUILTIN-5, 6, 16` (`rand`, `int`, `float`); D-018 (a name cannot be redeclared in one scope).

## Options
1. No user-defined overloading in Pino; the standard library uses closed signature lists for 1- and 2-argument callbacks and for `int`/`float`/`rand`/`readline`/`rune`.
2. Add general function overloading to Pino.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-37): Pino has no user-defined overloading. Declaring a second function with the same name in the same scope is a checker error even when its parameter list differs; standard-library members that accept several argument shapes are fixed by the spec. Normative rule: FN-E13.
