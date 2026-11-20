# D-146: Commas required between multiple positional union payload types

Status: accepted
Raised by: 016
Feature: spec/features/unions.md

## Question
How should `Variant(A B)` (two identifiers without a comma) be disambiguated between one named payload field `(A B)` and two positional payload types `(A, B)`?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 4 (item 6) and 9 (OQ-6).
- Spec: `spec/decisions/D-103-union-payload-forms.md` allows both named fields and positional types (all-or-nothing per union).

## Options
1. Require commas between multiple positional payload types (`Pair(int, string)`), while named fields stay comma-optional (`Variant(a B)` means one named field `a` of type `B`).
2. Disambiguate via `D-014` identifier casing.

## Decision
Option 1: multiple positional union payload types must be separated by commas (`Pair(int, string)`), while named fields stay comma-optional, so `Variant(a B)` means one named field `a` of type `B`, decided by the owner on 2026-11-19 (ticket 016, OQ-6).