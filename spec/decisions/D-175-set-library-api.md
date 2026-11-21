# D-175: The `set` standard-library API

Status: accepted
Raised by: 031 (OQ-36)
Feature: spec/stdlib/set.md

## Question
Which members does a `set[T]` have, and what do they return?

## Evidence
- Design 031 §12 OQ-36 (audit 028 Q8): `spec/features/sets.md` defines no methods; `projects/pino-compiler/modules/Environment.pino:156-217` defines `length`, `add(T) -> void`, `remove(T) -> void`, `any`, `all` and `each`. VECLIB-6 returns `[]T` from `push`; MAPLIB-6 returns `Option[V]` from `remove`.

## Options
1. `len`/`length -> int`, `x in s` / `x not in s -> bool`, `add(T) -> bool` (true if newly inserted) or `void`, `remove(T) -> bool` (true if it existed), `any`/`all`/`each` (1-arg callback `fn (T) -> ...`).
2. Match `Environment.pino` exactly (`len`/`length -> int`, `add(T) -> void`, `remove(T) -> void`, `any`/`all`/`each`, plus `in`/`not in`).
3. Chainable `add(T) -> set[T]` (like `vec:push`) and `remove(T) -> bool`, plus `len`/`length`, `any`/`all`/`each`, `in`/`not in`.

## Decision
Option 2, decided by the owner on 2026-11-20 (ticket 031, OQ-36): a set has `len` and its alias `length` (`int`), `add(T)` and `remove(T)` (both return `void`), `any`, `all` and `each` (each takes a callback with one parameter of the element type), and the operators `in` and `not in`. Normative rules: `spec/stdlib/set.md` (SETLIB-1 to SETLIB-8).

**Not decided, so no rule is written (logged for the owner):** what `remove` does when the element is absent, and the order in which `each`, `any` and `all` visit the elements of a set (D-004 leaves the order of maps unspecified). The cases avoid both.

**Extended on 2026-11-20 (033 follow-up), decided by the owner:** `s:remove(x)` for an absent element silently does nothing (returns `void` and leaves `s` unchanged), and sets iterate in insertion order (covering `for x in s`, `any`/`all`/`each`, and `str(s)`; removing an element and inserting it again puts it at the end). Normative rules: SETLIB-5 to SETLIB-7, SETLIB-9, SET-6, LOOP-18.

**Extended on 2026-11-21 (AD-003 / 043, MQ-3, MQ-12), decided by the owner:** `set[T]` has value semantics with copy-on-write (`SET-7`, `D-177`), and the mutating set methods `s:add(x)` and `s:remove(x)` (`mut fn`) require a mutable receiver place (`SETLIB-3`, `SETLIB-4`). See `D-181-vector-push-void-and-stdlib-mutation.md`.
