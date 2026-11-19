# D-070: Struct value versus reference semantics

Status: open
Raised by: 008
Feature: spec/features/structs.md

## Question
When a struct value is assigned to another variable or passed to a function, is the new name a copy of the struct (value semantics) or a second name for the same instance (reference semantics)?

## Evidence
- Reference: §7 "Structs" says structs are "initialized by explicitly naming their fields" and is silent about assignment and argument passing (`LANGUAGE_REFERENCE.md:499-528`).
- Tests: no xUnit test assigns a struct to a second variable and then mutates one of them. `DataStructureTests.TestStructStatePattern` stores a struct in an interface-typed field and reassigns the field, which does not observe copying (`pino-csharp.tests/Execution/DataStructureTests.cs:324-363`).
- Implementation: the evaluator keeps one `PinoStructInstance` object per literal and hands out the same object on assignment (`pino-csharp/Evaluator/Evaluator.cs:151`); the C backend passes struct pointers. `.routing/PROGRESS.md` lists this as an open language question ("struct value-vs-reference semantics").

Minimal snippet:

```pino
struct Point {
  x int
  y int
}
val a = Point { x: 1, y: 2 }
val b = a
b:x = 99
println(a:x)
println(b:x)
fn bump(p Point) {
  p:x = 500
}
bump(a)
println(a:x)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `99`, `99`, `500` (both names see the change, and the callee mutates the caller's struct) |
| VM | `RUNTIME ERROR: Value is not callable.` (the VM has no struct support) |
| C | `99`, `99`, `500` (same as tree-walk) |

## Options
1. Reference semantics (what the working engines do): document it; mutation through any name is visible everywhere. Cheap, but surprising for small structs such as `Vector2`.
2. Value semantics: copy on assignment and on argument passing. Safe by default, but both engines need to change, and large structs get copied.
3. Both: structs are values, with an explicit reference form (a pointer or `ref` type) for sharing. Largest language change.

## Decision
_Pending: owner decides._
