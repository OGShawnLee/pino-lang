# D-080: What the `main()` failure handler prints and returns

Status: accepted
Raised by: 008
Feature: spec/features/errors.md

## Question
When `main` returns a `Failure` or `None`, what exactly must the program write to `stderr`, what exit code must it return, and must every engine do it (the VM and the C backend do not today)?

## Evidence
- Reference: §8 "The `main()` Entry Point Failure Handler" says the runtime "prints the failure message directly to `stderr` in red" and exits with code `1` (`LANGUAGE_REFERENCE.md:886-893`). It does not give the text for `Option::None`, and does not say whether the text carries a prefix.
- Tests: none asserts the handler.
- Implementation: the CLI writes `Error: <payload>` for a `Failure` and `Error: Option::None` for `None`, then exits with 1 (`pino-csharp/Program.cs:461-476`). The conformance runner mirrors this text for the in-process engines (`pino-csharp.tests/Conformance/ConformanceRunner.cs:237-247`). The cases `conformance/errors/main-failure.pino`, `main-none.pino`, `main-success.pino` and `main-bubble.pino` only check that the failure message is reported, not the prefix.

```pino
fn main() Result[int, string] {
  println("start")
  return Result::Failure("boom")
}
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `start`, then the run fails with `Error: boom` |
| VM | `RUNTIME ERROR: Value is not callable.` (and for `Option::None` the run succeeds silently after `start`) |
| C | `start`, then exit code is a garbage value (for example `9665864`) with empty stderr; a successful `Result::Success(0)` also gives a garbage non-zero exit code |

## Options
1. Specify the exact text `Error: <payload>` (and `Error: Option::None`) and exit code 1; fix the VM and the C backend to match.
2. Specify only "the failure message goes to stderr and the exit code is 1", leaving the prefix open.
3. Drop the special `Option` handling: only `Result` failures end the program.

## Decision
Option 1, decided by the owner on 2026-11-21 (approval of `056-re-ir-design`, `IQ-12`; recorded in `D-186`): when `main` returns `Result::Failure(e)` or `Option::None`, the runtime writes `Error: <str(e)>` or `Error: Option::None` to `stderr` and exits with code 1. Normative rule: `ERR-24`. The VM and the C backend are legacy engines and stay as they are (`pino-csharp/` is frozen).
