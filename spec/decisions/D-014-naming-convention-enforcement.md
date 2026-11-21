# D-014: Is the naming convention enforced?

Status: accepted
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
Option 2, decided by the owner on 2026-11-20: the naming conventions are **enforced as checker errors**: `snake_case` for variables, properties, functions and methods, and `PascalCase` for structs, interfaces, enums and unions.

The owner asked for the exact casing rule to be copied from the self-host. The self-host decides only this, and this is the whole of what is recorded as a rule (LEX-20, LEX-E6):
- A struct, interface, enum, union, enum member or union variant name must start with an ASCII capital letter `A`-`Z`. Evidence: `is_capital` returns `first_char >= 'A' and first_char <= 'Z'` (`projects/pino-compiler/modules/parser.pino:719-722`) and is applied to a struct name (`parser.pino:1096`, "Expected capitalized identifier for struct name"), an interface name (`parser.pino:1159`), an enum name (`parser.pino:734`), an enum constant (`parser.pino:770`), a union name (`parser.pino:1046`) and a union pattern name (`parser.pino:1017`). It is also applied to a struct instance name (`parser.pino:217`) and a generic parameter (`parser.pino:1244`, not a decided category here).

**Not decided by the self-host, so no rule is written (stopped and logged for the owner):**
- Any check on the spelling of a variable, property, function or method name. The self-host enforces nothing for these (`grep is_capital` finds only the sites above), so the `snake_case` check has no evidence.
- The rest of `PascalCase` after the first letter: acronyms (`HTTPServer`), digits, underscores (`Http_Server`).
- Module-level constants written in `SCREAMING_SNAKE_CASE`: the self-host itself declares `val SPECIAL_REGEX`, `val STRING_REGEX`, `val FLOAT_SCI_REGEX` and others (`projects/pino-compiler/modules/lexer.pino:10-19`), and LEX-3 lists `GLOBAL_LIMIT` as an identifier. Whether these violate `snake_case` is the owner's call.

**Amended on 2026-11-20 (ticket 031, OQ-1), Option 1, decided by the owner:** only LEX-20 is enforced. A struct, interface, enum, union, enum member or union variant name must begin with an ASCII capital letter (LEX-20, LEX-E6); nothing else about spelling is checked. `snake_case`, `SCREAMING_SNAKE_CASE`, acronyms (`HTTPServer`), digits and underscores are accepted wherever LEX-3 accepts an identifier: for variables, properties, functions, methods and module constants (`SPECIAL_REGEX`, `GLOBAL_LIMIT`), and inside `PascalCase` type names after the first letter. The `snake_case`/`PascalCase` convention beyond that first letter is a style guideline, not a checker error. This narrows the first paragraph of the Decision above (which said the conventions are enforced as errors) and closes the list of points it left undecided. Normative rule: LEX-21.
