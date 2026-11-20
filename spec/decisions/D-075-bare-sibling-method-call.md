# D-075: Calling a sibling method by its bare name inside a method

Status: accepted
Raised by: 008
Feature: spec/features/structs.md

## Question
Inside a method, may another method of the same struct be called by its bare name (`area()`), the way fields can be read by their bare names, or must it be written `self:area()`?

## Evidence
- Reference: §7 "Structs" says a method can "directly access declared properties" and use `self`/`this` (`LANGUAGE_REFERENCE.md:503-504`). It says nothing about calling methods without a receiver.
- Tests: no xUnit test calls a sibling method by its bare name. Tests that call a method from a method use `self`/`state:` receivers (`DataStructureTests.TestStructStatePattern`).
- Implementation: the type checker accepts the program; the evaluator fails when it looks the name up (see below), and the C backend emits a plain C call to an undefined function.

```pino
struct Rect {
  w int
  h int
  fn area() int {
    return w * h
  }
  fn doubled() int {
    return area() * 2
  }
}
val r = Rect { w: 3, h: 4 }
println(r:doubled())
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `The given key 'area' was not present in the dictionary.` (an internal .NET exception) |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | TCC error: `implicit declaration of function 'area'`, `undefined symbol 'area'` |

`conformance/structs/instance-method.pino` uses `self:area()`, which works on tree-walk and C.

## Options
1. Make a bare method name inside a method resolve to the receiver's method (consistent with bare field access); needs checker, evaluator and C changes.
2. Reject it with a clear type-check error and require `self:`/`this:`.

## Decision
Option 1, decided by the owner on 2026-11-20: inside a method, a sibling method can be called by its bare name (`area()`), like bare field access.
