# D-054: Reserved words as parameter names (`val`)

Status: open
Raised by: 007
Feature: spec/features/functions.md

## Question
The reference's `each` example names a lambda parameter `val`, which is a keyword. Should it be accepted, or should the example change?

## Evidence
- Reference: `[10, 20]:each(fn(val int, idx int) { println("Index $idx: $val") })` (`LANGUAGE_REFERENCE.md:1177-1179`). The README's closure example does the same: `fn (val int) { return val * factor }` (`README.md:190-195`).
- Tests: no test uses a keyword as a parameter name.
- Implementation: the lexer maps `val` to `KeywordType.Constant` (`pino-csharp/Lexer.cs:13`), and `ConsumeParameters` requires an identifier token (`pino-csharp/Parser/Parser.cs:236-242`, `:299`).

Snippet:

```pino
[10, 20]:each(fn(val int, idx int) {
  println("Index $idx: $val")
})
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `PARSER: Expected Identifier, got Keyword: val (Constant)` |
| VM | same parse error |
| C | same parse error |

The conformance case `conformance/stdlib-vector/each-index.pino` uses `item` instead of `val`.

## Options
1. Fix the reference and README examples (rename the parameter). No compiler change.
2. Allow keywords in parameter position: parser change, and it needs a rule for which keywords.

## Decision
_Pending: owner decides._
