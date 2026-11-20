# D-160: `Option::None` as the initializer of a `val`

Status: accepted
Raised by: owner (2026-11-20)
Feature: spec/features/variables.md

## Question
Is `val x = Option::None` (or `val x Option[T] = Option::None`) allowed, given that a `val` can never be reassigned?

## Evidence
- Owner's root `main.pino:20` writes the empty case as `var nullish Option[string] = Option::None`, with `var`.
- Audit 028 section 6, capability 7 (`.tickets/028-audit-self-host-checker/audit.md`): the `-re` checker should "reject `val` initialized to `Option::None`"; the self-host checker does not implement this (it has no `Option` at all, audit section 2.5).
- Legacy: `conformance/unions/generic-decorator.pino:9` declared `val o2 = Option::None` and the legacy engines accept it.
- A `val` binds once (VAR-1), so a `val` that holds `Option::None` can never hold a value.

## Options
1. Reject a `val` whose initializer is `Option::None` with a checker error; the empty variable is written `var x Option[T] = Option::None`.
2. Allow it (a constant `None`), keeping `val` and `var` uniform.

## Decision
Option 1, decided by the owner on 2026-11-20: a `val` whose initializer is `Option::None` is a checker error, because it can never hold a value. `var x Option[T] = Option::None` is how an "empty" variable is written.
