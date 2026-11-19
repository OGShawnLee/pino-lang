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

## Errors
- STR-E1: Assigning to an element of a string (`s[0] = 'x'`) is rejected because strings are immutable. [src: LANGUAGE_REFERENCE.md §3 "Strings and Interpolation"]
- STR-E2: Any escape sequence in a string literal other than `\n`, `\t`, `\r`, `\"`, `\\` and `\$` is a compile error. [src: spec/decisions/D-020]

## Examples
Conformance cases: `conformance/strings/`.

## Open questions
- D-023: Indexing a string (`s[i]`) is not documented; the tree-walk interpreter yields a rune addressed by UTF-16 code unit.
- D-025: `+` between a string and a non-string (`"n=" + 1`, `1 + "n"`) concatenates, and `"a" < "b"` raises a .NET `FormatException` in the tree-walk interpreter; the reference documents neither.
- D-090: How a `"""..."""` multi-line string closes when four or five trailing quotes follow (`"""a""""`, `"""a"""""`).
- D-091: Whether raw NUL and other C0/C1 control characters are allowed inside string and rune literals.
- D-093: Whether simple `$name` string interpolation may use a keyword spelling such as `"$if"`.
