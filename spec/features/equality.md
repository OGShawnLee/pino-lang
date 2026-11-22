# Equality

Status: tree-walk 15/15 | VM 3/15 | C 15/15   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `eq`.
`==` and `!=` compare values structurally. The reference-identity operators `===` and `!==` are removed from the language (`D-178`).

## Rules
- EQ-1: `==` on two primitive values (`int`, `float`, `bool`, `rune`, `string`) is `true` when the values are equal. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"; ArithmeticTests.TestBooleanLogic; ArithmeticTests.TestRuneComparisons]
- EQ-2: `a != b` is the negation of `a == b`. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-3: `==` on two vectors is `true` when they have the same length and every pair of elements at the same index is equal under `==`. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-4: `==` on two maps is `true` when they have the same number of entries and every key of one maps to an equal value in the other. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-5: `==` on two struct instances of the same struct is `true` when all their fields are equal under `==`. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-6: `==` on two union values is `true` when they are the same variant and their payloads are equal under `==`. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"; ArithmeticTests.TestUnionEquality; ArithmeticTests.TestUnionEqualityWithValues]
- EQ-7: `==` on two enum values is `true` when they are the same enum member. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"; ArithmeticTests.TestEnumEquality]
- EQ-8: `==` on two `regex` values is `true` when their pattern strings are equal. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-9: The comparison of EQ-3 to EQ-6 recurses into nested elements and fields, so vectors of vectors and structs holding vectors are compared by content. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-13: Because structs, vectors, maps, sets, and unions have value semantics (D-177), `val b = a` produces a logically independent value equal to `a` under `==` (`a == b` is `true`). [src: spec/decisions/D-177-value-semantics-cow-and-mut.md; spec/decisions/D-178-remove-identity-operators.md]
- EQ-14: `==` on two pools of the same element type is `true` when they have the same live slots (index and generation) and equal values under `==` in each slot. [src: spec/decisions/D-185-pool-and-handle.md]

## Errors
- EQ-E1: Using `===` or `!==` is rejected as a parse error because reference-identity operators are removed from the language (only `==` and `!=` remain). [src: spec/decisions/D-178-remove-identity-operators.md]

## Examples
Conformance cases: `conformance/equality/`.

## Open questions
None.
