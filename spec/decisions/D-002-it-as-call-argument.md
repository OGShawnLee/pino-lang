# D-002: `it` as a call argument inside `for N`

Status: accepted
Raised by: 004
Feature: spec/features/loops.md

## Question
Should the implicit loop variable `it` of `for N { ... }` remain a loop variable when it appears inside a call argument, instead of turning the argument into an implicit lambda?

## Evidence
- Reference: §5 shows `for 3 { println("Implicit iteration: $it") }` and says it prints 0, 1, 2 (`LANGUAGE_REFERENCE.md:383-385`). The vector constructor in §4 also uses `it` as an implicit value (`init: it * 2`, `LANGUAGE_REFERENCE.md:289`).
- Tests: no xUnit test asserts `it` inside a `for N` body.
- Implementation: the parser wraps any call argument that contains an undeclared `it` into a one-parameter lambda (`pino-csharp/Parser/Parser.cs:65-69`, `pino-csharp/Parser/Parser.cs:263-267`). `ParseLoopStatement` never declares `it` in the parser scope for `for N` (`pino-csharp/Parser/Parser.Statement.cs:442-448`), whereas the checker and evaluator do bind it (`pino-csharp/Checker/Checker.Statement.cs:342-345`, `pino-csharp/Evaluator/Evaluator.Statement.cs:229-239`). So `it` works in expressions such as `sum = sum + it` but not as (part of) a call argument.

```pino
for 3 {
  println("Implicit iteration: $it")
}
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `Pino.PinoLambda` printed 3 times |
| VM | `null` printed 3 times |
| C | TCC error `cannot cast 'struct <anonymous>' to 'struct <anonymous>'` |

Working form (covered by `conformance/loops/times-implicit-it.pino`, passes on all engines):

```pino
var sum = 0
for 4 {
  sum = sum + it
}
println(sum)
```

## Options
1. Declare `it` in the parser scope while parsing a `for N` body: the reference example works unchanged. Risk: an implicit-lambda argument such as `numbers:map(it * 2)` written inside a `for N` body would then refer to the loop's `it`.
2. Keep the parser as is and change the reference example to use a temporary (`val i = it`): no compiler change, but the documented example and the implicit-lambda shorthand keep conflicting inside `for N`.
3. Forbid the implicit-lambda shorthand inside a `for N` body: removes the ambiguity, at the cost of a restriction that must be specified and diagnosed.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-17): `it` is declared in the scope of a `for N { ... }` body. Inside that body, including inside call arguments such as `println("$it")`, `it` is the loop's zero-based index and the argument is not turned into an implicit lambda (FN-17). To pass a lambda inside a `for N` body, write an explicit one (`xs:map(fn (x) -> x * 2)`) or use `for i in N`. Normative rule: LOOP-17.
