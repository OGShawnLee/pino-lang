# Changelog — 012 Re-Audit Fixes & Owner Decisions (D-020 / D-021)

## Outcomes by Finding (A-01..A-16)

| ID | Outcome | Summary |
| :--- | :--- | :--- |
| A-01 | fixed | Changed `TokenKind` to `ushort`, added `TokenFlags : ushort` (`None`, `LeadingNewline`, `LeadingWhitespace`, `LeadingComment`, `Missing`), kept `Unsafe.SizeOf<Token>() == 12`, set `Leading*` & `Missing` flags in `Lexer`, and added `LexResult.GetLeadingTrivia(int)`. |
| A-02 | fixed | Applied accepted **D-021 (Option 1)**: removed `$` from `IsIdentifierStart`/`ScanNumber`; `$` outside strings emits `PN1001`, and `"$x$y"` produces two adjacent interpolations. |
| A-03 | fixed | Applied accepted **D-020 (Option 1)**: documented strict string escapes (`\n`, `\t`, `\r`, `\"`, `\\`, `\$`) and `PN1011` on unknown escapes in `strings.md` & `D-020`; tagged tests and skipped the two legacy-escape `.pino` files in `LexerCorpusTests`. |
| A-04 | fixed | Marked incomplete floats (`1._`, `1_._`) as `TokenKind.Error` (`PN1002`), and emitted a single `PN1003` when trailing identifier letters follow (`123_abc`, `1._foo`). |
| A-05 | fixed | Added `DiagnosticCodes.InvalidRuneSurrogate` (`PN1014`) and rejected unpaired UTF-16 surrogates in rune literals (`'\uD83D'`, `'\uDC00'`) as `TokenKind.Error`. |
| A-06 | fixed | Added `DiagnosticCodes.MultilineStringInSingleLineInterpolation` (`PN1015`), kept single-line `$(...)` line-end recovery active, and kept `PN1009` spans bounded to their line. |
| A-07 | fixed | Added `DiagnosticSeverity.Hint`, `DiagnosticTags`, `RelatedLocation`, optional defaulted `Related`/`Tags`/`Help` on `Diagnostic`/`DiagnosticBag`, central `DiagnosticCodes` catalog (`PN1001`–`PN1015`, `PN1008` retired), and replaced `PINO*` strings in tests. |
| A-08 | fixed + decision opened | Updated `strings.md` (`STR-8`), `lexical.md` (`LEX-8`), and `spec/grammar.ebnf` (`lex`, `str` sections only) for `"""..."""` multi-line strings and rune escapes (`\t`, `\r`, `\\`); added `conformance/strings/multiline.*`; opened **D-092** and **D-093**. |
| A-09 | fixed | Added `TriviaKind.ByteOrderMark` and consumed leading `\uFEFF` at offset 0 as trivia instead of `PN1001`. |
| A-10 | fixed | Documented UTF-16 code-unit offsets and 1-based `LinePosition` coordinates on `SourceSpan`, `LinePosition`, `SourceText`; added clamped `GetOffset(LinePosition)` and `TryGetOffset(LinePosition, out int)`. |
| A-11 | fixed | Updated `ScanRune` recovery probe to skip `\<non-newline>` pairs and stop at line ends so `'a b'`, `'  '`, and `'a\''` emit one `PN1006`. |
| A-12 | decision opened | Preserved non-greedy `"""` closing behavior, pinned `"""a""""` and `"""a"""""` in `LexerStringTests` (`// D-090`), and opened **D-090**. |
| A-13 | fixed + decision opened | Added `Lexer.FormatForMessage` to format control characters and unpaired surrogates as `U+XXXX` across lexer diagnostics, and opened **D-091** without changing literal acceptance. |
| A-14 | fixed | Pre-sized `SourceText.ComputeLineStarts` via `ArrayPool<int>.Shared` and `Lexer` token/trivia buffers via `PooledList<T>` + `ImmutableCollectionsMarshal.AsImmutableArray`. |
| A-15 | fixed | Expanded `LexerCorpusTests` error-token code accounting (`PN1001`, `PN1003`–`PN1007`, `PN1014`), included `conformance/` and `pino.games/` in corpus scanning, and added tests for 10+ digit floats, emoji/surrogates in strings, lone `\r`, and 10k `$()` nesting. |
| A-16 | no change | Note only (parity suite `LexerParityTests` remains unchanged in scope for post-D-020/D-021 alignment). |

## Decisions Recorded & Opened

- **Accepted (owner on 2026-11-19):**
  - `spec/decisions/D-020-string-escape-sequences.md` — Option 1 (`\n`, `\t`, `\r`, `\"`, `\\`, `\$`; unknown escape is compile error).
  - `spec/decisions/D-021-dollar-in-identifiers.md` — Option 1 (`$` removed from identifiers; `"$x$y"` interpolates `x` then `y`).
- **Opened (`Status: open`):**
  - `spec/decisions/D-090-multiline-string-trailing-quotes.md` — How `"""` closes when 4+ quotes follow (`"""a""""`, `"""a"""""`).
  - `spec/decisions/D-091-control-chars-in-strings-and-runes.md` — Whether raw NUL and control characters are allowed inside string and rune literals.
  - `spec/decisions/D-092-numeric-underscore-grouping.md` — Whether numeric `_` separators require 3-digit grouping (`_[0-9]{3}`) or any single `_` between digits.
  - `spec/decisions/D-093-keyword-in-simple-interpolation.md` — Whether `$name` interpolation may use a keyword spelling (`"$if"`).

## Allocation Benchmark (External Release Probe, A-14)

Measured outside the repository (`%TEMP%/pino-re-bench`, Release build, 20 measured iterations after 5 warmup iterations over a 3x corpus of 129 files: 1,639,914 UTF-16 chars, 51,277 lines, 265,759 tokens, 166,731 trivia):

| Metric | Before | After |
| :--- | :--- | :--- |
| `SourceText` allocated / payload | 712.7 KB / 200.3 KB (3.56x) | 251.6 KB / 200.3 KB (1.26x) |
| `LexOnly` allocated / payload | 23,510.6 KB / 5,068.2 KB (4.64x, 90.6 B/tok) | 5,846.7 KB / 5,068.2 KB (1.15x, 22.5 B/tok) |
| Combined (`SourceText` + `Lexer.Lex`) | 23.66 MB | 5.96 MB |
| Combined mean latency & throughput | 27.12 ms (60.46 Mchars/s, 9.80 Mtok/s) | 21.15 ms (77.55 Mchars/s, 12.57 Mtok/s) |

## Files Touched

### `pino-csharp-re/`
- `pino-csharp-re/Pino.Compiler/Text/LinePosition.cs`
- `pino-csharp-re/Pino.Compiler/Text/SourceSpan.cs`
- `pino-csharp-re/Pino.Compiler/Text/SourceText.cs`
- `pino-csharp-re/Pino.Compiler/Diagnostics/DiagnosticSeverity.cs`
- `pino-csharp-re/Pino.Compiler/Diagnostics/DiagnosticTags.cs` (new)
- `pino-csharp-re/Pino.Compiler/Diagnostics/RelatedLocation.cs` (new)
- `pino-csharp-re/Pino.Compiler/Diagnostics/DiagnosticCodes.cs` (new)
- `pino-csharp-re/Pino.Compiler/Diagnostics/Diagnostic.cs`
- `pino-csharp-re/Pino.Compiler/Diagnostics/DiagnosticBag.cs`
- `pino-csharp-re/Pino.Compiler/Lexing/TokenKind.cs`
- `pino-csharp-re/Pino.Compiler/Lexing/TokenFlags.cs` (new)
- `pino-csharp-re/Pino.Compiler/Lexing/Token.cs`
- `pino-csharp-re/Pino.Compiler/Lexing/TriviaKind.cs`
- `pino-csharp-re/Pino.Compiler/Lexing/LexResult.cs`
- `pino-csharp-re/Pino.Compiler/Lexing/Lexer.cs`
- `pino-csharp-re/Pino.Compiler.Tests/Text/SourceTextTests.cs`
- `pino-csharp-re/Pino.Compiler.Tests/Diagnostics/DiagnosticBagTests.cs`
- `pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerCoreTests.cs`
- `pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerStringTests.cs`
- `pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerRecoveryTests.cs`
- `pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerParityTests.cs`
- `pino-csharp-re/Pino.Compiler.Tests/Lexing/LexerCorpusTests.cs`

### `spec/` & `conformance/`
- `spec/decisions/D-020-string-escape-sequences.md`
- `spec/decisions/D-021-dollar-in-identifiers.md`
- `spec/decisions/D-090-multiline-string-trailing-quotes.md` (new)
- `spec/decisions/D-091-control-chars-in-strings-and-runes.md` (new)
- `spec/decisions/D-092-numeric-underscore-grouping.md` (new)
- `spec/decisions/D-093-keyword-in-simple-interpolation.md` (new)
- `spec/features/lexical.md`
- `spec/features/strings.md`
- `spec/grammar.ebnf` (`lex` and `str` sections only)
- `conformance/strings/escapes-valid.pino` (new)
- `conformance/strings/escapes-valid.out` (new, gitignored; staged with `git add -f`)
- `conformance/strings/escape-unknown.pino` (new)
- `conformance/strings/escape-unknown.err` (new)
- `conformance/strings/interpolate-adjacent.pino` (new)
- `conformance/strings/interpolate-adjacent.out` (new, gitignored; staged with `git add -f`)
- `conformance/strings/multiline.pino` (new)
- `conformance/strings/multiline.out` (new, gitignored; staged with `git add -f`)
- `conformance/strings/known-failures.txt`

## `.pino` Files Requiring Owner Hand-Edit for D-020

Both files still contain regex-style escape sequences (such as `\s`, `\d`, `\w`) inside string literals, which now violate D-020 (`PN1011`). They were left untouched and skipped in `LexerCorpusTests`:
1. `projects/pino-compiler/modules/Lexer.pino`
2. `projects/c-compiler/modules/lexer.pino`

## Verification

- `dotnet build pino-csharp-re/Pino.sln`: 0 warnings, 0 errors.
- `dotnet test pino-csharp-re/Pino.sln`: 1140 passed, 0 failed, 0 skipped, 1140 total.
- `dotnet format pino-csharp-re/Pino.sln --verify-no-changes`: exited 0.
- `cd pino-csharp.tests; dotnet test`: 1493 passed, 0 failed, 0 skipped, 1493 total (includes conformance & spec lint).
- All 9 router reproduction inputs (`A-01`, `A-02`, `A-04`, `A-05`, `A-06`, `A-07`, `A-09`, `A-11`, `A-13`) verified directly through `Lexer.Lex`.

## Blockers

None.
