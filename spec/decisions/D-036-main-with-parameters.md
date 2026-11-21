# D-036: `fn main` with parameters

Status: open
Raised by: 006
Feature: spec/features/entry-point.md

## Question
What must happen when the root file defines `fn main(a int)`, a `main` that takes parameters?

## Evidence
- Reference: "A `fn main()` is defined ... The `main()` function is automatically called" (`LANGUAGE_REFERENCE.md:460-471`). Parameters are not mentioned.
- Tests: all tests use `fn main()` or `fn main {` with no parameters.
- Implementation: `Program.RunFile` calls `main` only when its arity is 0, so a `main` with parameters is silently never called (`pino-csharp/Program.cs:419-424` and `429-434`). The checker accepts the declaration. The C backend compiles `main` itself, so it runs the body.

```pino
fn main(a int) {
  println("main ran")
}
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | nothing, exit code 0 |
| VM | nothing, exit code 0 |
| C | `main ran` |

## Options
1. Reject a `main` with parameters in the checker: clear error; the engines then agree.
2. Allow it and define what the arguments are (for example command-line arguments): needs a design.
3. Keep today's behavior and document that `main` is only called without parameters.

## Decision
_Pending: owner decides._

Note (2026-11-20, ticket 031, OQ-18): the design of `main` arguments is still wanted, so this decision stays **open**. Interim rule decided by the owner: a `fn main` with parameters is a checker error until D-036 is designed (MAIN-E4). Option 1 above is therefore in force as a stopgap, and relaxing it is part of the future design.
