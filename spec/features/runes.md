# Runes

Status: tree-walk 11/11 | VM 11/11 | C 4/11   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `rune`.
A `rune` is a single Unicode code point written between single quotes. Runes combine with `+` and `-` and compare with the relational operators. The builtins `rune(value)` and `str(value)` convert to and from runes.

## Rules
- RUNE-1: A `rune` is a 32-bit Unicode code point, written between single quotes (`'a'`, `'🌲'`, `'\n'`), and `type(r)` of a rune is `"rune"`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; ArithmeticTests.TestRuneConversionsAndUtilities]
- RUNE-2: Adding an integer to a rune, or subtracting an integer from a rune, yields a rune shifted by that many code points, so `'A' + 1` is `'B'` and `'z' - 2` is `'x'`. [src: LANGUAGE_REFERENCE.md §3 "Ergonomic Rune Arithmetic"; ArithmeticTests.TestRuneArithmeticShift]
- RUNE-3: Subtracting one rune from another yields the `int` distance between their code points, so `'c' - 'a'` is `2`. [src: LANGUAGE_REFERENCE.md §3 "Ergonomic Rune Arithmetic"; ArithmeticTests.TestRuneArithmeticShift]
- RUNE-4: Runes compare with `==`, `!=`, `<`, `<=`, `>` and `>=` by their numeric Unicode value. [src: LANGUAGE_REFERENCE.md §3 "Ergonomic Rune Arithmetic"; ArithmeticTests.TestRuneComparisons]
- RUNE-5: `rune(n)` with an `int` argument returns the rune whose code point is `n`. [src: LANGUAGE_REFERENCE.md §9 "`rune(val)`"; ArithmeticTests.TestRuneConversionsAndUtilities]
- RUNE-6: `rune(s)` with a `string` argument returns the first character of `s`, including characters outside the Basic Multilingual Plane. [src: LANGUAGE_REFERENCE.md §9 "`rune(val)`"; ArithmeticTests.TestRuneConversionsAndUtilities]
- RUNE-7: `str(r)` of a rune returns the one-character string for that rune. [src: ArithmeticTests.TestRuneConversionsAndUtilities]
- RUNE-8: Indexing a string `s[i]` returns the `rune` at zero-based Unicode code-point index `i`, consistent with `s:len` and `for ch in s`. [src: spec/decisions/D-023-string-indexing.md]

## Errors
- RUNE-E1: Raw NUL (`\u0000`) and other unescaped C0 control characters (except tab `\t`) inside a rune literal are rejected as a lexer error. [src: spec/decisions/D-091-control-chars-in-strings-and-runes.md]
- RUNE-E2: Adding two runes (`a + b`) is a compile error whose message suggests `"$a$b"` or `str(a) + str(b)`. [src: spec/decisions/D-011-rune-plus-rune-result-type.md]

## Examples
Conformance cases: `conformance/runes/`.

## Open questions
None.
