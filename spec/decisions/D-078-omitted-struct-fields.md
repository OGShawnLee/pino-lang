# D-078: Struct literals that omit fields

Status: open
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
_Pending: owner decides._
