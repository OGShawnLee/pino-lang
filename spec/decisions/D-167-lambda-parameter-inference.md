# D-167: Untyped lambda parameters infer from every typed context

Status: accepted
Raised by: 028
Feature: spec/features/functions.md

## Question
In which positions do untyped lambda parameters take their types from context?

## Evidence
- Audit 028 section 2.4: the self-host infers untyped lambda parameters only when the lambda is a direct call argument (`projects/pino-compiler/modules/Checker.pino:1844-1915`); assigned to a variable, returned, placed in a struct field or in a collection it stays `Type::Implicit` and fails (`Cannot perform addition between 'implicit' and 'int'`, run #12).
- Audit 028 section 7, question 5.
- Owner's root `main.pino:34`: `call_lambda_3(fn (n, m) -> n + m)` infers `n` and `m` from the call.
- `spec/features/functions.md` FN-24 specifies only the comma syntax of untyped lambda parameters (D-145).

## Options
1. Infer from every typed context: a typed `val`/`var`, a `return` in a function with a known return type, struct field types, collection element types and call arguments.
2. Infer only from call arguments.

## Decision
Option 1, decided by the owner on 2026-11-20: untyped lambda parameters infer their types from every typed context, not just call arguments: a typed `val`/`var`, a `return` in a function with a known return type, struct field types, collection element types and call arguments.

**Extended on 2026-11-20 (ticket 031, OQ-14), Option 1, decided by the owner:** an untyped lambda with no typed context at all (`val f = fn (x) -> x + 1`) is a checker error with a message containing "Cannot infer parameter type", asking for a type annotation. Lambdas do not become implicitly polymorphic. Normative rule: FN-E11.
