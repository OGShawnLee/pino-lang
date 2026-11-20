# D-073: Implicit field access inside methods versus parameters and locals

Status: accepted
Raised by: 008
Feature: spec/features/structs.md

## Question
Inside a method, when a parameter or local variable has the same name as a field, which one does the bare name refer to, and how is the field reached then?

## Evidence
- Reference: §7 "Structs" says a method "can directly access declared properties of their struct in their body, or use the reserved identifiers `self` and `this`" (`LANGUAGE_REFERENCE.md:503-504`). It does not say what happens on a name clash.
- Tests: none declares a parameter or local with a field's name (`DataStructureTests.TestStructSelfThisAndProperties`, `DataStructureTests.TestStructSelfThisImmediateSync`).
- Implementation: the evaluator and the C backend resolve a bare name to the innermost declaration, so the parameter or local wins. No diagnostic is produced.

```pino
struct Box {
  x int
  fn set(x int) {
    x = x + 1
    println("param x: $x")
  }
  fn show() {
    println("field x: $x")
  }
}
val b = Box { x: 5 }
b:set(10)
b:show()
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `param x: 11`, `field x: 5` (the parameter is assigned, the field is untouched) |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | `param x: 11`, `field x: 5` |

A local `val x = 100` inside a method behaves the same way (`local x: 100`, then the field is still `5`) on tree-walk and C.

## Options
1. The parameter or local shadows the field (today's behavior) and `self:x` reaches the field: document it, no compiler change.
2. Reject a parameter or local that shadows a field with a diagnostic, forcing renames.
3. Require `self:` for every field access and drop bare field names (a large breaking change for existing programs).

## Decision
Option 1, decided by the owner on 2026-11-20: a parameter or local shadows a field with the same name, and `self:x` reaches the field.
