# D-090: How does `"""` close when more than three quotes follow?

Status: open
Raised by: 012
Feature: spec/features/strings.md

## Question
How does a multi-line `"""..."""` string close when four or five consecutive `"` characters appear at the end (for example `"""a""""` or `"""a"""""`)?

## Evidence
- Reference: §3 describes double-quoted string literals (`LANGUAGE_REFERENCE.md:93`) and does not mention `"""..."""` multi-line strings.
- Tests: `LexerStringTests.MultilineStringAllowsInteriorQuotes` tests interior `"` and `""` (`pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerStringTests.cs:417-424`); legacy `pino-csharp/Lexer.cs` has no `"""..."""` form.
- Implementation: `ScanStringContent` closes a `MultilineString` at the first `"""` sequence (`pino-csharp-re/Pino.Compiler/Lexing/Lexer.cs:340-347`), matching non-greedy closing (`.tickets/011-audit-pino-csharp-re/audit.md` A-12):
  - `"""a""""` (4 trailing quotes) lexes as `"""a"""` followed by an unterminated `"` (`PN1009` at offset 7).
  - `"""a"""""` (5 trailing quotes) lexes with 0 diagnostics as `"""a"""` followed by an empty `""`.
  - `"""a\""""` (escaped trailing quote `\"`) lexes as a single multi-line string containing `a\"`.

## Options
1. Keep non-greedy closing at the first `"""` and document that a literal `"` right before `"""` must be escaped as `\"`: simple single lookahead rule matching current `-re` behavior.
2. Close on the last 3 quotes of a 3..5-quote run so 1 or 2 trailing quotes stay inside the `StringPart` (and 6 quotes still means `""""""` empty string or error): matches Python/Kotlin triple-quoted trailing-quote rules without needing `\"`.

## Decision
_Pending: owner decides._
