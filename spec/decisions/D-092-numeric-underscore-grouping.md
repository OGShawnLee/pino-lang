# D-092: Numeric underscore grouping rules

Status: accepted
Raised by: 012
Feature: spec/features/lexical.md

## Question
Must underscore separators in integer and float literals occur in 3-digit groups (`_[0-9]{3}`), or is a single `_` allowed between any two digits (including in the fractional part)?

## Evidence
- Reference: §3 shows `1_000_000` with comment `(supports underscores)` (`LANGUAGE_REFERENCE.md:90`) and does not define a grouping restriction or fractional underscores.
- Tests: legacy `LexerTests.TestLiterals` does not test underscores (`pino-csharp.tests/LexerTests.cs:90`); `-re` `LexerCoreTests.ValidNumbersLexWithoutDiagnostics` asserts `1_000`, `1_000_000`, and `1_000.000_1` (`pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerCoreTests.cs:182-185`), and `LexerCoreTests.LexingSameSourceTwiceIsDeterministic` uses `1_0`.
- Implementation: legacy `pino-csharp/Lexer.cs:82-83` enforces `^[0-9]{1,18}(_[0-9]{3})*$` and `^[0-9]{1,9}(_[0-9]{3})*\.[0-9]{1,9}(_[0-9]{3})*$` (requiring 3-digit groups on both integer and fractional parts); `spec/grammar.ebnf:28-29` allows 3-digit groups on `INT` and none on `FLOAT` fractions; `-re` `Lexer.cs:601-617` accepts any single `_` between two ASCII digits on both integer and fractional parts (`.tickets/011-audit-pino-csharp-re/audit.md` A-08).

Minimal snippet:

```pino
println(1_0)
println(1_000.000_1)
```

## Options
1. Allow a single `_` between any two digits in integer and fractional parts (matching current `-re` `Lexer.cs` and `LexerCoreTests`): ergonomic for hex-free byte pairs or custom precision, avoids brittle lexer errors.
2. Enforce 3-digit grouping (`_[0-9]{3}`) matching legacy regexes (and update `grammar.ebnf` `FLOAT` to agree or disallow fractional underscores): stricter readability check, breaks existing `-re` test `1_000.000_1` and `1_0`.

## Decision
Option 1: a single `_` between any two digits in the integer and fractional parts is allowed, and a leading, trailing or doubled `_`, or a `_` next to `.`, is an error, decided by the owner on 2026-11-19. Consequence: literals such as `1_0` and `1_000.000_1` are valid, while `_1`, `1_`, `1__0`, `1_.0`, and `1._0` are rejected by the lexer.
