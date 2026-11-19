# D-020: String escape sequences

Status: accepted
Raised by: 005
Feature: spec/features/strings.md

## Question
Which escape sequences does a string literal support, and what happens to an unknown escape such as `\q`?

## Evidence
- Reference: §3 documents the rune escapes `'\n'` (`LANGUAGE_REFERENCE.md:89`) but no escape sequence inside string literals. §3 "Strings and Interpolation" explains `$` interpolation only (`LANGUAGE_REFERENCE.md:123-140`).
- Tests: `LexerTests.TestRuneLiterals` asserts `'\n'` and `'\''` (`LexerTests.cs:209-236`). No test asserts a string escape.
- Implementation: `LexStringLiteral` handles `\n`, `\t`, `\r`, `\"`, `\\` and `\$`; any other backslash is kept as a literal backslash (`Lexer.cs:197-213`). Rune literals accept only `\n`, `\t`, `\r`, `\'` and `\\` and raise `Invalid escape character` otherwise (`Lexer.cs:339-345`).

Minimal snippet:

```pino
println("a\tb")
println("x\qy")
println("cost: \$5")
println("q\"q")
println("back\\slash")
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `a<TAB>b`, `x\qy`, `cost: $5`, `q"q`, `back\slash` |
| VM | same as tree-walk |
| C | same as tree-walk |

## Options
1. Document exactly the escapes the lexer handles (`\n \t \r \" \\ \$`) and make an unknown escape an error, as for runes: consistent with rune literals.
2. Document the same set and keep an unknown escape as literal text: lenient, but typos pass silently.
3. Add more escapes (`\0`, `\u{...}`, `\'`) before documenting: larger change; `\'` is currently valid for runes only.

## Decision
Option 1, decided by the owner on 2026-11-19: string literals support `\n`, `\t`, `\r`, `\"`, `\\` and `\$`, and any other escape sequence is a compile error.
