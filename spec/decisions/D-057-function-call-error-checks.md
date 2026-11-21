# D-057: Errors for wrong argument count and wrong return type

Status: accepted
Raised by: 007
Feature: spec/features/functions.md

## Question
The checker rejects calls with the wrong number of arguments and functions whose `return` does not match the declared return type. Should the spec state these rules (and their messages)?

## Evidence
- Reference: silent. §3 only says the checker "ensure[s] type consistency before execution" (`LANGUAGE_REFERENCE.md:80`).
- Tests: `CheckerTests.TestEnumDefinitionAndTypeChecking` and `CheckerTests.TestRuneTypeChecker` assert that a wrongly typed argument is rejected (`ThrowsAny`, no message), which FN-E1 covers. No test asserts the argument-count error or a mismatched return type for a plain return (only for tuples, TUP-E3).
- Implementation: `Function '<name>' expected <n> arguments, but got <m>.` and `Function '<name>' declared return type '<t>', but returned '<u>'.` (the latter at `pino-csharp/Checker/Checker.Statement.cs:275`).

Snippets and outputs (identical on all engines, they come from the shared checker):

```pino
fn two(a int, b int) int {
  return a + b
}
println(two(1))
```

`TYPE CHECK ERROR: Function 'two' expected 2 arguments, but got 1.`

```pino
fn ret_str() string {
  return 5
}
```

`TYPE CHECK ERROR: Function 'ret_str' declared return type 'string', but returned 'int'.`

## Options
1. Add rules FN-E2 (arity) and FN-E3 (return mismatch) with the current messages.
2. Add them with only the `TYPE CHECK ERROR` prefix and leave the wording free.
3. Leave them unspecified.

## Decision
Option 2, decided by the owner on 2026-11-20 (ticket 031, OQ-19): the checker rejects a call with the wrong number of arguments and a `return` whose value does not match the declared return type, with flexible wording: the message contains `expected N arguments` (with the real count) and `declared return type`. Normative rules: FN-E9 and FN-E10.
