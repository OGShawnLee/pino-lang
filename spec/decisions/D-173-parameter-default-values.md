# D-173: Parameter default values

Status: accepted
Raised by: 031 (OQ-20)
Feature: spec/features/functions.md

## Question
Does the checker accept default values for parameters, typed (`x int = 1`) or typeless (`x = 1`)?

## Evidence
- Design 031 §12 OQ-20: `PROGRESS.md:63, 194` (the `-re` parser parses default values permissively and defers typeless default parameters); the self-host implements them (`audit.md §2.4`).

## Options
1. The checker rejects parameter default values until the feature is designed.
2. The checker supports both typed (`x int = 1`) and typeless (`x = 1`) default parameters now, resolved in the callee's scope.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-20): a parameter default value, typed or typeless, is a checker error until the feature is designed. The parser still parses the syntax so that the checker can report it. Normative rule: FN-E12.
