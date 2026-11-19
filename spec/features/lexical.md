# Lexical structure

Status: tree-walk 17/18 | VM 10/18 | C 16/18   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `lex`.
A program is a sequence of tokens: keywords, identifiers, literals (integer, float, boolean, string, rune), operators and markers. A `#` starts a comment that runs to the end of the line. Commas that separate elements of lists and initialization blocks are optional when the elements are separated by whitespace or newlines.

## Rules
- LEX-1: A `#` outside a string or rune literal starts a comment that extends to the end of the line and is ignored. [src: LexerTests.TestSkipCommentsAndWhitespace; LANGUAGE_REFERENCE.md §2 "Variables and Constants"]
- LEX-2: The words `var`, `val`, `fn`, `if`, `else`, `match`, `when`, `for`, `struct`, `enum`, `break`, `continue` and `return` are keywords, not identifiers. [src: LexerTests.TestIdentifiersAndKeywords]
- LEX-3: A name made of ASCII letters, digits and underscores (starting with a letter or underscore, excluding `$`) that is not a keyword, such as `identifier_name`, `hero_name2` or `GLOBAL_LIMIT`, is an identifier. [src: LexerTests.TestIdentifiersAndKeywords; CheckerTests.TestScopeLeakageGlobalPreservation; spec/decisions/D-021]
- LEX-4: An integer literal is a sequence of decimal digits, optionally grouped with underscores as in `1_000_000`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LexerTests.TestLiterals]
- LEX-5: A float literal is decimal digits, a `.` and decimal digits, such as `3.14159`; a leading `-` is the unary minus operator and produces a negative value such as `-0.005`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LexerTests.TestLiterals; ArithmeticTests.TestUnaryOperators]
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

## Errors
- LEX-E1: Using a keyword such as `match` as a variable name is rejected with a message containing "Expected Identifier". [src: LexerTests.TestIdentifiersAndKeywords]
- LEX-E2: In comma-optional element lists (vector literals, map literal entries, call argument lists, and `when` value lists), a `-` with whitespace on exactly one side (`a -b` or `a- b`) is rejected with a message containing "ambiguous '-'" and suggesting "write 'a - b' to subtract or 'a, -b' for two elements". [src: spec/decisions/D-010-optional-commas-vs-unary-minus.md]

## Examples
Conformance cases: `conformance/lexical/`.

## Open questions
- D-014: The reference says `snake_case` and `PascalCase` naming is "strictly" adopted, but no engine rejects other spellings.
- D-092: Whether underscore separators in integer and float literals must occur in 3-digit groups (`_[0-9]{3}`) or anywhere between two digits.
