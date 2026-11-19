# D-065: `join` is missing from the reference's vector methods

Status: open
Raised by: 007
Feature: spec/stdlib/vector.md

## Question
Should the reference's "Vector Methods" list document `join(separator)`, which a test asserts and the tree-walk engine implements?

## Evidence
- Reference: §10 "Vector Methods" lists `len`, `cap`, `push`/`add`, `pop`, `each`, `map`, `filter`, `find`, `find_index`, `any`, `all` and `count` (`LANGUAGE_REFERENCE.md:1147-1244`) but not `join`.
- Tests: `DataStructureTests.TestVectorJoinMethod` (`DataStructureTests.cs:498-514`) asserts `["Pino", "is", "fast"]:join(" ")` is `Pino is fast`, joining an empty vector gives `""`, and joining `[1, 2, 3]` with `", "` gives `1, 2, 3`.
- Implementation: tree-walk supports it. The VM fails on every vector case. The C backend does not: `Vector_string_join` is an undefined symbol (TCC `implicit declaration of function 'Vector_string_join'`).

VECLIB-25 records `join` from the test (source-of-truth rule (b)); the C failure is listed in `conformance/stdlib-vector/known-failures.txt`.

## Options
1. Add `join` to the reference and keep VECLIB-25.
2. Remove it from the language (the test and the evaluator) and delete VECLIB-25.

## Decision
_Pending: owner decides._
