# D-053: Return type on a lambda

Status: accepted
Raised by: 007
Feature: spec/features/functions.md

## Question
May a lambda state its return type (`fn (n int) int => n * 2`)?

## Evidence
- Reference: §10 writes callback signatures as `fn(item) TargetType` and `fn(item) bool` (`LANGUAGE_REFERENCE.md:1184`, `:1193`), which is type notation, not lambda syntax. Every lambda example in the reference has no return type (`LANGUAGE_REFERENCE.md:1177`, `:1188`, `:1197`).
- Tests: no test annotates a lambda's return type. The function-type syntax `fn(int) int` is asserted only as a type (`DataStructureTests.TestVectorEmptyInitializationComplex`, `DataStructureTests.cs:169`).
- Implementation: `ParseFunctionLambda` reads the parameter list and then either `=>` or a block; there is no return-type slot (`pino-csharp/Parser/Parser.Expression.cs:412-433`).

Snippet:

```pino
val sq = fn (n int) int => n * n
println(sq(5))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `PARSER: Expected '{'` |
| VM | `PARSER: Expected '{'` |
| C | `PARSER: Expected '{'` |

## Options
1. Keep lambdas without a return type (inferred, see D-050). The spec says so explicitly.
2. Allow an optional return type between the parameter list and `=>` or `{`: parser change, rules and cases.

## Decision
Option 1: lambdas take no return type; it is inferred, decided by the owner on 2026-11-19. Consequence: lambda syntax has no return-type annotation slot between the parameter list and `->` or `{`.
