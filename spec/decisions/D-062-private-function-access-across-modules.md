# D-062: Calling a non-`pub` module function

Status: accepted
Raised by: 007
Feature: spec/features/modules.md

## Question
Must `Module::name(...)` and `from Module import name` reject a function that is not marked `pub`, at check time on every engine?

## Evidence
- Reference: §6 does not mention `pub` at all (`LANGUAGE_REFERENCE.md:472-494`).
- Tests: `CheckerTests.TestNamespacedEntityVisibility` (`CheckerTests.cs:138-185`) asserts that a non-`pub` struct (`Struct 'Lexer::Reader' is not public`) and a non-`pub` interface are rejected; MOD-E1 and MOD-E2 cover them. `TranspilerCTests` only imports `pub` functions. No test covers a non-`pub` function.
- Implementation: the checker has visibility errors for structs, interfaces, enums and unions (`pino-csharp/Checker/Checker.cs:95`, `:123`, `:151`, `:179`) and for `from` imports (`pino-csharp/Checker/Checker.Statement.cs:374`). A qualified call to a private function is only caught by the tree-walk evaluator at run time (`pino-csharp/Evaluator/Evaluator.Expression.cs:842`).

Snippet (`conformance/modules/modules/geometry.pino` has a non-`pub` struct, interface and no private function; the module used here defines `fn hidden_helper(n int) int { return n * 100 }` without `pub`):

```pino
import Geometry
println(Geometry::hidden_helper(1))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Member 'hidden_helper' is not exported by module 'Geometry' (or is private).` |
| VM | `RUNTIME ERROR: Value is not callable.` |
| C | compiles and prints `100` (the private function is called) |

`from Geometry import hidden_helper` is rejected by the checker on every engine: `TYPE CHECK ERROR: Module 'Geometry' does not export 'hidden_helper' (or it is private).`

## Options
1. Add a checker rule so a qualified call to a non-`pub` function is a `TYPE CHECK ERROR` on every engine; add `.err` cases.
2. Leave functions out of the visibility rules and document that `pub` applies to types only.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-32): a qualified call `Module::name(...)` to a function that is not `pub` is a checker error on every engine, and so is `from Module import name` for such a function (as MOD-5 and D-163 say, a non-`pub` top-level declaration is private to its module). Normative rules: MOD-E6 and MOD-E7.
