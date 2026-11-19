# D-023: Indexing a string

Status: open
Raised by: 005
Feature: spec/features/runes.md

## Question
What does `s[i]` mean for a string: the i-th rune (Unicode code point), the i-th UTF-16 code unit, or the i-th byte?

## Evidence
- Reference: §3 says a string is an "Immutable UTF-8 text slice" and `len` counts runes (`LANGUAGE_REFERENCE.md:90`, `LANGUAGE_REFERENCE.md:1029-1032`). String indexing is not documented.
- Tests: `ArithmeticTests.TestRuneSurrogatesAndEmojiIndexing` rebuilds `"A🟥B"` with `for i in text:len { result = result + text[i] }` and expects the same text (`ArithmeticTests.cs:80-92`). That holds for rune indexing with a rune `len`, and for UTF-16 indexing with a UTF-16 `len`.
- Implementation: the tree-walk interpreter indexes the .NET string, so it addresses UTF-16 code units and `len` is the UTF-16 length (`Evaluator.Expression.cs:683-685`); the C backend works on UTF-8 bytes (see STRLIB-1 known failure).

Minimal snippet:

```pino
val s = "a🌲b"
println(s:len)
println(s[0] == 'a')
println(s[1] == '🌲')
println(s[2] == 'b')
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `4`, `True`, `False`, `False` (the emoji is two UTF-16 units) |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | `6`, `True`, `False`, `False` (the emoji is four bytes) |

## Options
1. Index by rune, like `len` and the `for ch in s` loop: `s[1]` is `'🌲'`; indexing becomes O(n) on UTF-8 storage unless an index is kept.
2. Index by byte and make `len` the byte count: O(1) and matches UTF-8 storage, but contradicts the rune-based `len` in the reference.
3. Do not allow `s[i]`; use iteration and `substring`: removes the question, at the cost of a common idiom.

## Decision
_Pending: owner decides._ No spec rule describes `s[i]` until then.
