# D-004: Iterating over maps

Status: accepted
Raised by: 004
Feature: spec/features/loops.md

## Question
Should iteration over maps be part of the documented loop semantics, and should its order be specified?

## Evidence
- Reference: §5 says iterator loops iterate over "a collection (list, numeric range, or character string)" and does not mention maps (`LANGUAGE_REFERENCE.md:389`).
- Tests: `DataStructureTests.TestMapLoopSingleVar` and `TestMapLoopDoubleVar` assert that `for k in m` visits keys and `for k, v in m` visits key and value pairs, only with `Assert.Contains`, so no order is asserted (`pino-csharp.tests/Execution/DataStructureTests.cs:211-239`).
- Implementation: the evaluator iterates the underlying dictionary in its own order (`pino-csharp/Evaluator/Evaluator.Statement.cs:257-269`) and the checker types the variables from the map type (`pino-csharp/Checker/Checker.Statement.cs:313-321`). The VM compiler has no map iteration.

```pino
val m = map[string, int] { "a": 1, "bb": 2 }
var total_len = 0
for k in m {
  total_len = total_len + k:len
}
println(total_len)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `3` |
| VM | `0` (the loop body never runs) |
| C | `3` |

Spec impact: LOOP-9 and LOOP-10 are written from the tests (source rule (b)) and state that the order is unspecified. The cases use order-independent sums on purpose.

## Options
1. Document map iteration in the reference with "order unspecified": matches LOOP-9 and LOOP-10 as written.
2. Specify insertion order: needs the C backend's hash map to preserve insertion order, plus a new rule and cases.
3. Do not document it and treat it as internal: the two tests stay, but the rules would have to be removed from the spec.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-26): iteration over maps stays with an unspecified order. LOOP-9 and LOOP-10 are unchanged; the checker types `for v in m` and `for k, v in m` the same way whatever the order.
