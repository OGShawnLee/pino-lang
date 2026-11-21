# D-017: Where is a `val` reassignment detected?

Status: accepted
Raised by: 005
Feature: spec/features/variables.md

## Question
Must the checker reject `name = value` on a `val` before any code runs (as the reference's "Compiler/Checker ERROR" says), and must every engine therefore reject it?

## Evidence
- Reference: §2 marks `# name = "Vincent"      # Compiler/Checker ERROR` for a reassigned `val` (`LANGUAGE_REFERENCE.md:70`).
- Tests: no xUnit test asserts the error. VAR-E1 is therefore backed by the reference only.
- Implementation: only the tree-walk environment raises `RUNTIME ERROR: Cannot reassign constant variable` when the assignment executes (`Environment.cs:49`, `Environment.cs:122`). The shared checker does not look at the constness of the target. Output printed before the assignment is therefore not suppressed.

Minimal snippet:

```pino
val name = "a"
name = "b"
println(name)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Cannot reassign constant variable 'name'.` |
| VM | `b` (the assignment succeeds) |
| C | `b` (the assignment succeeds) |

## Options
1. Add a checker error for assignment to a `val` (for example `TYPE CHECK ERROR: Cannot assign to constant 'name'.`): matches the reference and makes all engines agree; the case then needs the new message.
2. Keep the run-time error and fix the reference wording: no checker change, but VM and C must also enforce it, and a bad assignment on an unexecuted path goes unnoticed.
3. Accept both: the checker reports it for straightforward cases and the run-time check stays as a safety net.

## Decision
Option 1, decided by the owner on 2026-11-19: assigning to a `val` is a checker (compile-time) error. Consequence: `VAR-E1` requires a checker error before execution, and VM/C acceptance (plus TreeWalk runtime-only detection) are recorded as known failures.

**Extended on 2026-11-21 (AD-003 / 043, MQ-1, MQ-2), decided by the owner:** `val` is **deeply (transitively) immutable** — neither reassignment (`VAR-E1`) nor any interior mutation (field write, index write, mutating method call, or passing as `mut`) at any depth is permitted (`VAR-1`, `VAR-E10`). `var` remains the sole mutable local binding form, and `mut` is a reserved modifier keyword rather than a third binding form (`LEX-2`). See `D-177-value-semantics-cow-and-mut.md`.
