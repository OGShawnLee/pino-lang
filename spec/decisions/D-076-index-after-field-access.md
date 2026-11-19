# D-076: Indexing a struct field directly (`instance:field[index]`)

Status: open
Raised by: 008
Feature: spec/features/structs.md

## Question
Should `instance:field[index]` (and `instance:field["key"]:method()`) parse as an index into the field, or is it intended that the field must first be copied into a variable?

## Evidence
- Reference: vectors and maps are indexed with `[]` (`LANGUAGE_REFERENCE.md` §4); the reference is silent about indexing a field reached through `:`.
- Tests: tests index fields only through the bare name inside methods (`CheckerTests.TestGenericStructMethodInferenceWithInterfaceCompatibilityThrows` uses `catalog[key]` in a method body).
- Implementation: after `:` the parser reads `name[...]` as a generic call with type arguments, so the index expression is parsed as a type and fails.

```pino
struct Reg {
  items []int
}
val r = Reg { items: [1, 2, 3] }
println(r:items[1])
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `PARSER: Expected Typing, got Literal: 1 (Integer)` |
| VM | `PARSER: Expected Typing, got Literal: 1 (Integer)` |
| C | `PARSER: Expected Typing, got Literal: 1 (Integer)` |

Working form: `val items = r:items` followed by `items[1]`.

## Options
1. Parse `field[expr]` as an index when the bracket content is an expression, and as generic arguments only for `name[Type](...)` calls (parser change).
2. Keep the parser and document the restriction.

## Decision
_Pending: owner decides._
