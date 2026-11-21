# Lexical structure

Status: tree-walk 17/18 | VM 10/18 | C 16/18   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `lex`.
A program is a sequence of tokens: keywords, identifiers, literals (integer, float, boolean, string, rune), operators and markers. A `#` starts a comment that runs to the end of the line. Commas that separate elements of lists and initialization blocks are optional when the elements are separated by whitespace or newlines.

## Rules
- LEX-1: A `#` outside a string or rune literal starts a comment that extends to the end of the line and is ignored. [src: LexerTests.TestSkipCommentsAndWhitespace; LANGUAGE_REFERENCE.md §2 "Variables and Constants"]
- LEX-2: The words `var`, `val`, `mut`, `fn`, `if`, `else`, `match`, `when`, `for`, `struct`, `enum`, `break`, `continue` and `return` are keywords, not identifiers; `mut` is used strictly as a modifier on struct fields, mutating methods, interfaces, parameters, function types, and call-site arguments, never as a variable-binding keyword. [src: LexerTests.TestIdentifiersAndKeywords; spec/decisions/D-177-value-semantics-cow-and-mut.md]
- LEX-3: A name made of ASCII letters, digits and underscores (starting with a letter or underscore, excluding `$`) that is not a keyword, such as `identifier_name`, `hero_name2` or `GLOBAL_LIMIT`, is an identifier. [src: LexerTests.TestIdentifiersAndKeywords; CheckerTests.TestScopeLeakageGlobalPreservation; spec/decisions/D-021]
- LEX-4: An integer literal is a sequence of decimal digits, optionally separated by a single `_` between any two digits as in `1_000_000` or `1_0`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LexerTests.TestLiterals; spec/decisions/D-092-numeric-underscore-grouping.md]
- LEX-5: A float literal is decimal digits, a `.` and decimal digits, such as `3.14159` or `1_000.000_1`, with a single `_` allowed between any two digits in both the integer and fractional parts; a leading `-` is the unary minus operator and produces a negative value such as `-0.005`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LexerTests.TestLiterals; ArithmeticTests.TestUnaryOperators; spec/decisions/D-092-numeric-underscore-grouping.md]
- LEX-6: `true` and `false` are the two boolean literals. [src: LexerTests.TestLiterals]
- LEX-7: A string literal is text delimited by double quotes. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LexerTests.TestLiterals]
- LEX-8: A rune literal is a single Unicode code point delimited by single quotes, such as `'a'` or `'🌲'`, or one of the escapes `'\n'`, `'\t'`, `'\r'`, `'\''` and `'\\'`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LexerTests.TestRuneLiterals; LexerCoreTests.ValidRunesLexAsRuneLiteral]
- LEX-9: Commas between the elements of a vector literal are optional when the elements are separated by whitespace or newlines. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- LEX-10: Commas between the entries of a map literal are optional when the entries are separated by whitespace or newlines. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- LEX-11: Commas between the fields of a struct initialization are optional when the fields are separated by whitespace or newlines. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- LEX-12: Commas between the names in an `import` list are optional when the names are separated by whitespace. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- LEX-13: Commas between the values of a `when` branch of a `match` are optional when the values are separated by whitespace. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"; LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"]
- LEX-14: Commas between the parameters of a function declaration and between the arguments of a function call are optional when they are separated by whitespace. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- LEX-15: The comma-separated forms of LEX-9 to LEX-14 remain valid and mean the same as the comma-less forms. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"; LANGUAGE_REFERENCE.md §6 "Modules and Imports"]
- LEX-16: Adjacent simple interpolations in a string such as `"$x$y"` interpolate `x` and `y` in order because `$` is not an identifier character. [src: spec/decisions/D-021]
- LEX-17: Where commas are optional and whitespace separates elements (vector literals, map literal entries, call argument lists, and `when` value lists), `a - b` and `a-b` subtract, `a, -b` is two elements, and a leading `-` such as `[-b]` or `f(-1)` is unary minus; outside those contexts `val x = a -b` remains subtraction. [src: spec/decisions/D-010-optional-commas-vs-unary-minus.md]
- LEX-18: `->` is the canonical arrow token (used in lambdas, map entries, function types, and `match` arms); `=>` is not a token of the language. [src: spec/decisions/D-100-arrow-token.md]
- LEX-19: Commas in comma-separated lists are optional when separated by whitespace, but a trailing comma after the last element is disallowed. [src: spec/decisions/D-108-trailing-commas-disallowed.md]

- LEX-20: A struct, interface, enum, union, enum member or union variant name must begin with an ASCII capital letter (`A` to `Z`). [src: spec/decisions/D-014-naming-convention-enforcement.md]
- LEX-21: Beyond LEX-20 the spelling of a name is not checked: `snake_case`, `SCREAMING_SNAKE_CASE`, acronyms such as `HTTPServer`, digits and underscores are accepted in the names of variables, properties, functions, methods and constants, and in type-like names after the first capital letter. [src: spec/decisions/D-14-naming-convention-enforcement.md]
- LEX-22: An identifier that is also a C keyword, such as `char`, `double` or `register`, is a valid identifier for a variable, a parameter or a loop variable and is not reserved by the checker; a backend that needs to renames it. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; spec/decisions/D-5-c-keyword-identifiers.md]

## Errors
- LEX-E1: Using a keyword such as `match` as a variable name is rejected with a message containing "Expected Identifier". [src: LexerTests.TestIdentifiersAndKeywords]
- LEX-E2: In comma-optional element lists (vector literals, map literal entries, call argument lists, and `when` value lists), a `-` with whitespace on exactly one side (`a -b` or `a- b`) is rejected with a message containing "ambiguous '-'" and suggesting "write 'a - b' to subtract or 'a, -b' for two elements". [src: spec/decisions/D-010-optional-commas-vs-unary-minus.md]
- LEX-E3: A leading, trailing, or doubled `_` in a numeric literal, or a `_` next to `.` (`_1`, `1_`, `1__0`, `1_.0`, `1._0`), is rejected as a lexer error. [src: spec/decisions/D-092-numeric-underscore-grouping.md]
- LEX-E4: A comma after the last element of any comma-separated list (`[1, 2,]`, `f(a,)`, `map[K, V] { 1 -> 2, }`, parameter lists, struct literals, union variants, or generic lists) is rejected as a parse error. [src: spec/decisions/D-108-trailing-commas-disallowed.md]
- LEX-E5: Using `=>` anywhere in source code is rejected as a lexer or parse error. [src: spec/decisions/D-100-arrow-token.md]

- LEX-E6: A struct, interface, enum, union, enum member or union variant whose name does not begin with an ASCII capital letter is rejected by the checker with a message containing "capitalized". [src: spec/decisions/D-014-naming-convention-enforcement.md]

## Examples
Conformance cases: `conformance/lexical/`.

## Open questions
None.
