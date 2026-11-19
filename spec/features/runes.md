# Runes

Status: pending regeneration (ticket 009)

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

## Errors
None documented.

## Examples
Conformance cases: `conformance/runes/`.

## Open questions
- D-011: `'a' + 'b'` produces the string `"ab"`, `'A' + 32` produces a rune and `'c' - 'a'` produces an int: three result types for the same operator.
- D-023: Indexing a string (`s[i]`) yields a rune in the tree-walk interpreter, but it addresses UTF-16 code units, so `"a🌲b"[1]` is half of a surrogate pair; the reference does not document string indexing.
