# D-091: Raw NUL and control characters inside strings and runes

Status: open
Raised by: 012
Feature: spec/features/strings.md

## Question
Are unescaped raw NUL (`\u0000`) and other C0/C1 control characters (aside from `\n`/`\r`/`\t`) allowed inside string and rune literals?

## Evidence
- Reference: §3 documents `rune` (`LANGUAGE_REFERENCE.md:92`) and `string` as a 16-byte `{ data, len }` slice with `:to_cstring()` for C FFI (`LANGUAGE_REFERENCE.md:93-97`), without mentioning raw control bytes or NUL characters in literals.
- Tests: no legacy or `-re` unit test asserts raw NUL or control characters inside quotes; escaped `\0` is rejected (`PN1011` in strings per D-020, `PN1005` in runes).
- Implementation: `ScanStringContent` and `ScanRune` in `pino-csharp-re/Pino.Compiler/Lexing/Lexer.cs` accept raw NUL (`\u0000`) and other control characters (like ESC `\u001B`) inside `"..."`, `"""..."""`, and `'...'` with 0 diagnostics (`.tickets/011-audit-pino-csharp-re/audit.md` A-13), while diagnostic messages format control characters as `U+XXXX`. Under AD-001's C backend, raw NUL bytes inside literals also interact with NUL-terminated C strings.

## Options
1. Continue accepting raw NUL and control characters inside string/rune literals (preserving length-prefixed UTF-8 invariance).
2. Reject raw NUL (`\u0000`) and/or unescaped C0 control characters (other than `\t`, and `\r`/`\n` inside `"""..."""`) with a lexer diagnostic, keeping strings and runes safe for terminals and C FFI.

## Decision
_Pending: owner decides._
