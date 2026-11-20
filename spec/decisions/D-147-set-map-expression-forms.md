# D-147: `set[...]` and `map[...]` in expression position and brace-less empty `map[K, V]`

Status: accepted
Raised by: 016
Feature: spec/features/sets.md, spec/features/maps.md

## Question
In expression position, are `set[...]` and `map[...]` always collection forms (preventing indexing a variable named `set` or `map`), and is brace-less empty `map[K, V]` allowed?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 4 (item 3) and 9 (OQ-7).
- Spec: `D-102` already accepts brace-less empty `set[T]` and `map[K, V]`; `set[i]` (variable index) has the same token sequence as empty `set[T]` when `T` is an identifier.

## Options
1. Treat `set[...]` and `map[...]` as collection forms in primary expression position (cannot index a variable named `set` or `map` there; member calls like `xs:map(f)` after `:` stay valid), and allow brace-less empty `map[K, V]`.
2. Allow `set`/`map` as indexable variable names and require `{}` for empty sets and maps.

## Decision
Option 1: in expression position, `set[...]` and `map[...]` are always collection forms (a variable named `set` or `map` cannot be indexed there, while member calls like `xs:map(f)` are unaffected), and an empty map is written `map[K, V]` without braces like `set[T]` and `[]T`, decided by the owner on 2026-11-19 (ticket 016, OQ-7).