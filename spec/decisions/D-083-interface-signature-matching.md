# D-083: Must method signatures match for a struct to satisfy an interface?

Status: accepted
Raised by: 008
Feature: spec/features/interfaces.md

## Question
Does a struct satisfy a non-generic interface only when its methods have identical parameter and return types, or only when methods of the same names exist?

## Evidence
- Reference: §7 "Structural Interfaces" says "Any `struct` implementing the functions specified in an interface automatically satisfies it" (`LANGUAGE_REFERENCE.md:567`); it does not define what "implementing a function" requires beyond the name.
- Tests: `CheckerTests.TestTypeCheckerInvalidInterfaceThrows` covers a missing method; `CheckerTests.TestGenericInterfacesInvalid` covers a wrong return type, but only for a generic interface (`pino-csharp.tests/Checker/CheckerTests.Interfaces.cs:31-48`, `CheckerTests.Generics.cs:331-354`). No test covers a mismatch for a plain interface.
- Implementation: `ImplementsInterface` compares signatures (`pino-csharp/Checker/Checker.Expression.cs:1351`), so a wrong parameter type is rejected.

```pino
interface Greeter {
  fn greet(name string)
}
struct User {
  fn greet(n int) {}
}
fn run_greet(g Greeter) {}
run_greet(User {})
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `TYPE CHECK ERROR: Argument 1 for function 'run_greet' expected type 'Greeter', but got 'User'.` |
| VM | same message (the check runs before the engine) |
| C | same message |

## Options
1. Specify that parameter and return types must match exactly (what the checker does); add the rule and a case.
2. Specify a looser rule (for example, parameter names may differ but types must match, which is already true) and list what is compared.
3. Specify name-only matching and relax the checker.

## Decision
Option 1, decided by the owner on 2026-11-20: a struct satisfies a non-generic interface only when its methods' parameter and return types match exactly.

**Extended on 2026-11-20 (ticket 031, OQ-30), Option 1, decided by the owner:** parameter names are ignored when a struct is matched against an interface: only the number of parameters, their types and the return type are compared. Normative rule: IFACE-6.

**Extended on 2026-11-21 (AD-003 / 043, MQ-9), decided by the owner:** interfaces support `mut fn` methods and `mut prop Type` properties with safe covariance (`IFACE-1`, `IFACE-4`, `IFACE-6`, `IFACE-E7`): a non-`mut` interface method rejects a `mut fn` implementation, a `mut fn` interface method accepts either a `mut fn` or a plain `fn`, a read-only interface property accepts either a read-only or a `mut` struct field, and a `mut` interface property requires a `mut` struct field; method signatures must also match per-parameter `mut` modifiers (`D-180`). See `D-179-struct-field-and-method-mutability.md`.
