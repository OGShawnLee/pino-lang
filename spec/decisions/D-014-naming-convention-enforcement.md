# D-014: Is the naming convention enforced?

Status: open
Raised by: 005
Feature: spec/features/lexical.md

## Question
The reference says Pino "strictly adopts" `snake_case` for variables, properties, functions and methods and `PascalCase` for structs, interfaces and enums: must a program that breaks the convention be rejected, or is it only a style guideline?

## Evidence
- Reference: §1 "Syntax Guidelines", "Naming Convention" (`LANGUAGE_REFERENCE.md:50`).
- Tests: `LexerTests.TestIdentifiersAndKeywords` uses `identifier_name` (`LexerTests.cs:9-60`); `LexerTests.TestComplexStatement` uses `Coordinates`, `pos`, `x`, `y`; no test asserts that another spelling is rejected.
- Implementation: the lexer accepts `^[a-zA-Z_$][a-zA-Z0-9_$]*$` as an identifier (`Lexer.cs:84`, `Lexer.cs:308`), and neither the parser nor the checker inspects the spelling.

Minimal snippet:

```pino
struct player { hp int }
val myVar = 1
println(myVar)
println("accepted")
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `1` then `accepted` |
| VM | `1` then `accepted` |
| C | `1` then `accepted` |

## Options
1. Convention only: document it as a style guideline and keep accepting every spelling; no compiler change.
2. Enforce it in the checker as an error: matches "strictly"; needs a rule for acronyms, digits and leading underscores, and breaks existing programs that use other spellings.
3. Enforce it as a warning: keeps programs running and makes the convention visible; needs a warning channel that does not exist yet.

## Decision
_Pending: owner decides._
