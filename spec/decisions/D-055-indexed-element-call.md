# D-055: Calling an indexed element (`v[0](x)`)

Status: accepted
Raised by: 007
Feature: spec/features/functions.md

## Question
Should a call whose callee is an index expression, such as `fn_list[0](9)`, call the element? Today the parser reads `name[...](...)` as a call with generic arguments.

## Evidence
- Reference: silent. §4 shows indexing (`numbers[0]`, `LANGUAGE_REFERENCE.md:296`); generic calls such as `return_tuple[int](42)` appear in §3 (`LANGUAGE_REFERENCE.md:219`). Nothing says what `x[i](y)` means.
- Tests: `DataStructureTests.TestVectorEmptyInitializationComplex` (`DataStructureTests.cs:153-180`) stores functions in a `[]fn (int) int` but only tests membership (`int_fn in fn_list`); it never calls an element.
- Implementation: after an identifier followed by `[`, `ParseFunctionCall` parses the bracket contents as type arguments (`pino-csharp/Parser/Parser.Expression.cs:392-410`).

Snippet:

```pino
val fn_list = []fn (int) int
val int_fn = fn (n int) => n
fn_list:push(int_fn)
println(fn_list[0](9))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `PARSER: Expected Typing, got Literal: 0 (Integer)` |
| VM | same parse error |
| C | same parse error |

## Options
1. Distinguish by the bracket contents: a type name makes it a generic call, anything else an index followed by a call.
2. Require the two-step form (`val f = fn_list[0]` then `f(9)`) and document it.
3. Use a different generic-call syntax.

## Decision
Option 1: `fns[0](9)` calls the indexed element when the brackets contain an expression rather than type arguments, decided by the owner on 2026-11-19 (ticket 016, OQ-8).
