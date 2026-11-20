# D-142: `return` across newlines and bare `return`

Status: accepted
Raised by: 016
Feature: spec/features/functions.md

## Question
Does a newline immediately after `return` terminate a bare `return`, or can the returned expression start on the following line?

## Evidence
- Design: `.tickets/016-re-parser-design/design.md` sections 3.6 and 9 (OQ-3).
- Self-host: `projects/pino-compiler/modules/Parser.pino:264-267` writes `return` at the end of a line followed by the returned expression on the following lines.
- Legacy: `pino-csharp/Parser/Parser.Statement.cs:416` parses an expression whenever `IsExpression(stream)` is true, ignoring newlines.

## Options
1. Newline terminates bare `return` (`return\nfoo()` is a bare `return` followed by `foo()`).
2. Ignore newlines after `return` (`return\n  expr` returns `expr`; bare `return` only before `}` or statement-only keywords).
3. Revised Option 2 variant: `return` at the end of a line continues onto the next line like a trailing binary operator; `return` is bare only when the next token is `}` or end of file.

## Decision
Option 3: `return` at the end of a line continues onto the next line like a trailing binary operator (so `Parser.pino:264-267`'s `return\n  expr` is valid), and `return` is bare only when the next token is `}` or end of file, decided by the owner on 2026-11-20 (ticket 016, OQ-3 revised).