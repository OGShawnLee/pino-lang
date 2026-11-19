# D-017: Where is a `val` reassignment detected?

Status: open
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
_Pending: owner decides._ VAR-E1 states only that the assignment is an error; the VM and C cases are recorded as known failures.
