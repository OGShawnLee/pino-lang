# D-025: `+` and `<` on strings with other operands

Status: accepted
Raised by: 005
Feature: spec/features/strings.md

## Question
What do `"n=" + 1`, `1 + "n"` and `"a" < "b"` mean: implicit conversion to text, a type error, or a lexicographic comparison?

## Evidence
- Reference: §3 documents only `rune + rune` producing a string and string interpolation as the way to insert values into text (`LANGUAGE_REFERENCE.md:102-140`). Operators between strings and other types, and string ordering, are not documented.
- Tests: no test asserts them. `ArithmeticTests.TestRuneSurrogatesAndEmojiIndexing` only joins strings (`ArithmeticTests.cs:80-92`).
- Implementation: the checker types any `+` with a string operand as `string` (`Checker.Expression.cs:1036-1040`); the evaluator converts the other operand to text. Relational operators go through numeric conversion (`Evaluator.Expression.cs:1039`), which fails on text.

Minimal snippet:

```pino
println("n=" + 1)
println(1 + "n")
println("a" < "b")
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `n=1`, `1n`, then `System.FormatException: The input string 'a' was not in a correct format.` |
| VM | `n=1`, `1n`, then the same `System.FormatException` |
| C | `n=1`, `1n`, `True` |

## Options
1. Make `"n=" + 1` a checker error and require `str(1)` or interpolation; add lexicographic `<`, `<=`, `>`, `>=` for strings (by code point): strict and predictable.
2. Keep the implicit conversion in `+` and add string ordering: convenient; hides type mistakes.
3. Keep `+` as is and reject string ordering with a checker error instead of a .NET exception.

## Decision
Option 1, decided by the owner on 2026-11-19: `"n=" + 1` (`string + non-string`, either order) is a checker error (use interpolation or `str`); strings support `<`, `<=`, `>`, `>=`, compared lexicographically by code point. Consequence: `spec/features/strings.md` adds `STR-10` for lexicographic comparison and `STR-E5` forbidding `string + non-string`, and legacy behavior is recorded as known failures.

Extended on 2026-11-21 (036 follow-up), decided by the owner: `string + rune` (`s + ch`, where `s` is a `string` and `ch` is a `rune`) appends the rune and gives a `string` (STR-11), and `s += ch` works too via VAR-4. Every other `string + non-string` mix stays an error under STR-E5, including `string + int`. `rune + string` (with the rune on the left) is not decided.

Extended on 2026-11-21 (042 follow-up), decided by the owner: `rune + string` (`ch + s`, where `ch` is a `rune` and `s` is a `string`) prepends the rune and gives a `string` (STR-12). Every other `string + non-string` mix stays an error under STR-E5, including `int + string` and `string + int`; `rune + rune` stays an error under RUNE-E2.
