# Strings and interpolation

Status: tree-walk 14/14 | VM 8/14 | C 12/14   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `str`.
A string literal is text between double quotes. Inside it, `$name` inserts the value of a variable and `$(expr)` inserts the value of an expression. Strings are immutable. The builtin `str(value)` returns the textual representation of any value. Methods and properties of strings (`:len`, `:upper()`, ...) are specified in `stdlib/string.md`.

## Rules
- STR-1: A `string` is immutable: methods such as `:upper()` return a new string and leave the receiver unchanged. [src: LANGUAGE_REFERENCE.md §3 "Strings and Interpolation"; LANGUAGE_REFERENCE.md §10 "String Methods"]
- STR-2: In a string literal, `$name` is replaced by the value of the variable `name`, so with `val age = 24` the literal `"Hello, my age is $age"` is `"Hello, my age is 24"`. [src: LANGUAGE_REFERENCE.md §3 "Strings and Interpolation"; LexerTests.TestStringInterpolation]
- STR-3: In a string literal, `$(expr)` is replaced by the value of the expression `expr`, so `"The sum is: $(x + y)"` with `x = 10` and `y = 20` is `"The sum is: 30"`; `expr` may contain member access such as `player:attack` or a method call. [src: LANGUAGE_REFERENCE.md §3 "Strings and Interpolation"; LexerTests.TestComplexStringInterpolation]
- STR-4: A string literal may contain several interpolations mixed with plain text, and each is replaced in order. [src: LexerTests.TestStringInterpolation]
- STR-5: `str(v)` returns the textual representation of `v` as a `string`: `str(12)` is `"12"`, `str([1, 2, 3])` is `"[1, 2, 3]"` and `str` of a struct instance `User { name: "Test" }` is `User { name: "Test" }`. [src: LANGUAGE_REFERENCE.md §9 "`str(val)`"; StdlibTests.TestStrFunction]
- STR-6: The `+` operator joins two strings, so `"a" + "b"` is `"ab"`. [src: ArithmeticTests.TestRuneSurrogatesAndEmojiIndexing]
- STR-7: String literals support the escape sequences `\n`, `\t`, `\r`, `\"`, `\\` and `\$`. [src: spec/decisions/D-020]
- STR-8: Multi-line string literals delimited by `"""..."""` may span lines and support escapes and `$name` / `$(expr)` interpolation. [src: LexerStringTests.MultilineStringSpansLines]
- STR-9: In a run of 3–5 `"` characters at the end of a `"""..."""` multi-line string literal, the last three close it and one or two quotes stay inside the string. [src: spec/decisions/D-090-multiline-string-trailing-quotes.md]
- STR-10: Strings support `<`, `<=`, `>`, `>=`, compared lexicographically by Unicode code point. [src: spec/decisions/D-025-string-with-other-types.md]
- STR-11: Concatenating a `string` and a `rune` with `+` (`s + ch`) appends the `rune` and gives a `string`, and `s += ch` updates `s` through VAR-4. [src: spec/decisions/D-025-string-with-other-types.md]
- STR-12: Concatenating a `rune` and a `string` with `+` (`ch + s`) prepends the `rune` and gives a `string`. [src: spec/decisions/D-025-string-with-other-types.md]

Note (non-normative): Repeatedly appending to an unshared local `var` string (`s = s + x` or `s += x`) is amortized linear in total bytes appended on both engines; strings remain values (D-184), so any other holder keeps its previous value.

## Errors
- STR-E1: Assigning to an element of a string (`s[0] = 'x'`) is rejected because strings are immutable. [src: LANGUAGE_REFERENCE.md §3 "Strings and Interpolation"]
- STR-E2: Any escape sequence in a string literal other than `\n`, `\t`, `\r`, `\"`, `\\` and `\$` is a compile error. [src: spec/decisions/D-020]
- STR-E3: Raw NUL (`\u0000`) and other unescaped C0 control characters inside a string literal are rejected as a lexer error; tab (`\t`) is allowed, and line breaks are allowed only inside `"""..."""`. [src: spec/decisions/D-091-control-chars-in-strings-and-runes.md]
- STR-E4: A keyword spelling directly after `$` in a string literal (`"$if"`) is rejected with a message suggesting `\$` for a literal dollar or `$(...)` for an expression. [src: spec/decisions/D-093-keyword-in-simple-interpolation.md]
- STR-E5: Concatenating a `string` with a non-`string` other than `string + rune` (STR-11) or `rune + string` (STR-12) using `+` (such as `string + int` or `int + string`) is rejected by the checker; use string interpolation or `str(v)` instead. [src: spec/decisions/D-025-string-with-other-types.md]

## Examples
Conformance cases: `conformance/strings/`.

## Open questions
None.
