# D-081: `panic` output format and backtrace

Status: open
Raised by: 008
Feature: spec/features/errors.md

## Question
What must `panic(message)` print, and must every engine print the stack backtrace the reference describes?

## Evidence
- Reference: `panic` "prints a clean red stack backtrace containing all calling stack frames to `stderr`, and exits the process with a non-zero code (`101`)" (`LANGUAGE_REFERENCE.md:761`). It gives no text format.
- Tests: `ErrorHandlingTests.TestPanicHaltAndBacktrace` asserts the message and that `CallStack` contains the frame names `first` and `second` (`pino-csharp.tests/Execution/ErrorHandlingTests.cs:9-27`); it does not assert the printed format.
- Implementation: the CLI prints `thread 'main' panicked at '<message>'`, then `stack backtrace:` and numbered frames, in red, and exits with 101 (`pino-csharp/Program.cs:436-447`). The conformance runner reports only the panic message for the in-process engines, so the backtrace and exit code are not observable there (`ERR-5` is checked on the C engine only and `ERR-21` is `[no-case]`).

```pino
fn first() {
  second()
}
fn second() {
  panic("crash message")
}
first()
```

| Engine | Actual result |
| :--- | :--- |
| tree-walk | failure message `crash message` (CLI: `thread 'main' panicked at 'crash message'`, `stack backtrace:`, frames `second`, `first`, exit 101) |
| VM | failure message `crash message` (same CLI output as tree-walk) |
| C | `exit code 101`, stderr `thread 'main' panicked at 'crash message'`, no backtrace |

Also, `return panic("no value")` in a function returning `int` (the `any` result type, ERR-6) fails to compile on the C backend: TCC error `cannot cast from/to void`.

## Options
1. Specify the first line `thread 'main' panicked at '<message>'` plus a numbered backtrace; make the C backend print frames.
2. Specify only the message and exit code 101; leave the backtrace as an engine-quality feature.

## Decision
_Pending: owner decides._
