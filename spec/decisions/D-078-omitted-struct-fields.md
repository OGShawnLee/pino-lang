# D-078: Struct literals that omit fields

Status: accepted
Raised by: 008
Feature: spec/features/structs.md

## Question
What value does a field have when its struct literal omits it, and what happens when that field is read?

## Evidence
- Reference: §7 "Structs" says "Structs are initialized by explicitly naming their fields" (`LANGUAGE_REFERENCE.md:503`) and does not say whether every field must be named.
- Tests: `DataStructureTests.TestMapEvaluatorRuntimeBehavior` builds `Wrapper {}` and expects using the omitted `key` field as a map key to throw (`pino-csharp.tests/Execution/DataStructureTests.cs:104-110`). Several tests also build structs with no fields named (`Helper {}`, `User {}`).
- Proposal `proposals/001-null-safety-and-default-struct-fields.md` (`Draft`, evidence only) says omitted fields currently raise `RUNTIME ERROR: Struct 'Person' has no property ...` in the interpreter and a native default in compiled code, and proposes safe defaults (`""`, `0`, `false`, empty vector). Related: D-071.
- Implementation: the evaluator raises when the omitted field is read (`pino-csharp/Evaluator/Evaluator.Expression.cs:452`); the C backend zero-initialises.

```pino
struct Person {
  name string
  age int
}
val p = Person { name: "Ana" }
println(p:name)
println(p:age)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `Ana`, then `RUNTIME ERROR: Struct 'Person' has no property or instance method 'age'.` |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | `Ana`, `0` |

## Options
1. Default-initialise omitted fields (proposal 001, section 3.1) so every engine prints `0`.
2. Require every field to be named in a literal (checker error), keeping `null`-free structs.
3. Keep the current split behavior and document that reading an omitted field is undefined.

## Decision
Option 2, decided by the owner on 2026-11-20: a struct literal must name every field, and an omitted field is a checker error. The shorthand `Point { x }` still counts as naming `x` (D-104).

**Extended on 2026-11-20 (ticket 031, OQ-29), Option 1, decided by the owner:** a struct literal must also name every field of all the structs the struct embeds (STRUCT-8), so an omitted embedded field is a checker error like any other omitted field. Field names that collide between an outer struct and an embedded struct are a checker error. Normative rules: STRUCT-E12 and STRUCT-E14.
