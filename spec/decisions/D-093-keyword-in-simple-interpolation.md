# D-093: Keyword spellings in simple `$name` string interpolation

Status: accepted
Raised by: 012
Feature: spec/features/strings.md

## Question
Is a keyword spelling after `$` inside a string literal (such as `"$if"`) lexed as an `Identifier` token, or is it disallowed?

## Evidence
- Reference: §3 describes `$name` variable interpolation (`LANGUAGE_REFERENCE.md:127-140`) without specifying whether keyword spellings after `$` lex as keywords or identifiers.
- Tests: legacy `LexerTests.TestStringInterpolation` only tests `$name` and `$room` (`pino-csharp.tests/LexerTests.cs:118`); `-re` `LexerStringTests.SimpleInterpolationNeverProducesKeywordTokens` explicitly asserts that `"$if"` emits `Identifier("if")` rather than `IfKeyword` (`pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerStringTests.cs:236-242`).
- Implementation: both legacy `pino-csharp/Lexer.cs:270-275` (`new Token(TokenType.Identifier, identStr)`) and `-re` `pino-csharp-re/Pino.Compiler/Lexing/Lexer.cs:395-401` emit `Identifier` directly for `"$if"`, whereas `LEX-2` (`spec/features/lexical.md:11`) and `grammar.ebnf:97` (`"$" , IDENT`) state that keywords are not identifiers (`.tickets/011-audit-pino-csharp-re/audit.md` A-08).

## Options
1. Document that `$name` accepts any `[a-zA-Z_][a-zA-Z0-9_]*` word as an identifier-lookup name (leaving `Identifier` error-tolerant for the parser/checker, or treating keywords as missing variables).
2. Reject keyword spellings directly after `$` (requiring `$(...)` or rejecting `"$if"` as invalid interpolation syntax matching `"$" , IDENT`).

## Decision
Option 2: a keyword spelling right after `$` in a string (`"$if"`) is an error whose message suggests `\$` for a literal dollar or `$(...)` for an expression, decided by the owner on 2026-11-19. Consequence: simple interpolation `$name` accepts only non-keyword identifiers.
