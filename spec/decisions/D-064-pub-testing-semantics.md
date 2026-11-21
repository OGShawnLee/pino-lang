# D-064: Meaning of `@pub_testing`

Status: accepted
Raised by: 007
Feature: spec/features/modules.md

## Question
Should the spec define what `@pub_testing` does (make a declaration importable by test code only), or only the parse rule that it cannot be combined with `pub`?

## Evidence
- Reference: not mentioned.
- Tests: `ParserTests.TestParseModuleAndImports` (`ParserTests.cs:206-220`) asserts that `@pub_testing fn sub(a int) {}` sets `IsPubTesting` and not `IsPublic`, and that `pub @pub_testing` and `@pub_testing pub` are rejected (MOD-E5). No test imports a `@pub_testing` symbol.
- Proposal: `proposals/012-test-scoped-visibility-pub-testing.md` has `Status: Proposed`, so by the source-of-truth rule it is evidence only. It describes the decorator as keeping declarations private to normal importers but importable by test suites.
- Implementation: `ModuleResolver.IsTestingFile` treats `*_test.pino` as a testing file (`pino-csharp/ModuleResolver.cs:36-41`).

## Options
1. Accept proposal 012 (mark it `Implemented` once the behavior is verified) and add rules and cases for test-only imports.
2. Keep only the parse-level rule MOD-E5 until the feature is implemented and tested.

## Decision
Option 1, decided by the owner on 2026-11-20 (ticket 031, OQ-34): a `@@Testable` declaration (D-163) is importable from test files (a file named `*_test.pino` or a file inside a `test/` directory) and is rejected, as a private declaration is, when imported from any other file. Normative rules: MOD-8 and MOD-E8. Proposal 012 remains `Proposed`; marking it `Implemented` is not part of ticket 033.

Note (2026-11-20): D-163 renamed the marker to `@@Testable` with the same parse rule (MOD-E5). This question, what the marker grants to test code, stays open; the spelling `@pub_testing` above is historical.
