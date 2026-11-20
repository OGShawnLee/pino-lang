# D-030: Result of `readline()` at end of input

Status: accepted
Raised by: 006
Feature: spec/stdlib/builtins.md

## Question
What must `readline(prompt?)` return when standard input is closed or exhausted?

## Evidence
- Reference: `readline` "reads a line of text from standard input" and returns `string`; end of input is not mentioned (`LANGUAGE_REFERENCE.md:921-925`).
- Tests: none. `StdlibTests` has no `readline` test (`pino-csharp.tests/StdlibTests.cs`).
- Implementation: the tree-walker returns `Console.ReadLine()`, which is .NET `null` at end of input (`pino-csharp/Evaluator/Evaluator.BuiltIns.cs:17-24`); the checker still types the call as `string` (`pino-csharp/Checker/Checker.cs:52`). The VM reuses the same built-in. The C runtime returns an empty string `""` when it reads EOF with no data (`runtime/runtime.c:56-84`).

With stdin closed, this program never ends on the tree-walker, because the result never equals `"q"`. The conformance runner closes stdin, so there is deliberately **no conformance case** for it (it would hang):

```pino
for {
  if readline("q? ") == "q" { break }
}
```

Bounded variant that was run with empty stdin:

```pino
val line = readline("q? ")
if line == "q" { println("equals q") } else { println("not q") }
var n = 0
for n < 3 {
  if readline("") == "q" { break }
  n = n + 1
}
println(n)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `q? not q` then `3` (result is `null`, never `"q"`) |
| VM | `q? not q` then `3` |
| C | `q? not q` then `3` (result is `""`) |

## Options
1. End of input returns an empty string `""`: matches the C runtime and the declared `string` type; a loop that waits for `"q"` still never ends, so programs must test for `""` themselves.
2. End of input returns `null` and the return type becomes `string?` (or `Option[string]`): honest about EOF; changes the signature and every caller.
3. End of input returns a `Result` or panics: forces callers to handle EOF explicitly; breaks the current `val name = readline(...)` usage in the reference.

## Decision
Option 3 (`Result` variant), decided by the owner on 2026-11-19: `readline(prompt?)` returns `Result[string, string]`, returning `Result::Failure` at end of input and on read errors. **Not decided:** the exact error text is not decided, so `.err`/`.out` cases must not depend on it. Consequence: `spec/stdlib/builtins.md` updates `readline` (`BUILTIN-9`) to return `Result[string, string]` with `[no-case: ...]`.
