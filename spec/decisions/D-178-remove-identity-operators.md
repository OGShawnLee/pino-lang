# D-178: Remove `===` and `!==` identity operators

Status: accepted
Raised by: 043 (MQ-4)
Feature: spec/features/equality.md, spec/features/types.md

## Question
What happens to the reference-identity operators `===` and `!==` (`EQ-10`, `EQ-11`, `EQ-12`, `D-026`) once structs, vectors, maps, sets, and unions adopt value semantics with copy-on-write (`D-177`)?

## Evidence
- Spec: `spec/features/equality.md` (`EQ-10`–`EQ-13`), `spec/features/types.md` (`TYPE-13`), `spec/decisions/D-026-identity-of-strings-and-floats.md`.
- Conformance: `conformance/equality/identity-{primitives,strings-and-floats,structs,vectors,maps}.pino`.
- Design `043` (§2, `MQ-4`): under CoW value semantics, physical pointer identity leaks whether an internal buffer copy has occurred; Roc forbids identity comparison entirely, and keeping `===` only on scalars offers little value once heap identity is gone.

## Options
1. Reject `===` / `!==` on value-semantic aggregates (`struct`, `[]T`, `map`, `set`, `union`) with a checker error (`PN3229`), keeping `===` on primitives and enums (`EQ-10`, `D-026`).
2. **Retire `===` and `!==` from the language entirely** (keep only structural `==` and `!=`).
3. Keep `===` as an alias for `==` on value types.

## Decision
Option 2, decided by the owner on 2026-11-21: `===` and `!==` are **removed from the language entirely**; only structural `==` and `!=` remain.

## Consequences
- Supersedes `D-026` (`D-026-identity-of-strings-and-floats.md`).
- Retires `EQ-10`, `EQ-11`, and `EQ-12` in `spec/features/equality.md`, rewords `EQ-13` for value-copy equality (`val b = a; a == b`), and adds `EQ-E1` (using `===` or `!==` is a parse error).
- Updates `TYPE-13` (`spec/features/types.md`), `D-141` (`spec/decisions/D-141-assignment-and-relational-precedence.md`), and `spec/grammar.ebnf` (removes `"==="`, `"!=="`, and `identity_eq`).
- Retires the five `conformance/equality/identity-*.{pino,out}` cases and their 15 lines in `conformance/equality/known-failures.txt`.
