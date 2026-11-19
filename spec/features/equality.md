# Equality and identity

Status: pending regeneration (ticket 009)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `eq`.
`==` and `!=` compare values structurally. `===` and `!==` compare identity: scalar values by value and heap objects (structs, vectors, maps, unions) by memory reference.

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
- EQ-10: `===` on two primitive values or two enum values is `true` when the values are identical, and `!==` is its negation. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-11: `===` on two heap objects (struct, vector, map or union values) is `true` only when both operands refer to the same object, and is `false` for distinct objects holding equal data. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-12: `!==` on two heap objects is `true` when they are different objects and `false` when they are the same object. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]
- EQ-13: `==` on two references to the same heap object is `true`. [src: LANGUAGE_REFERENCE.md §3 "Structural Equality vs. Reference Identity Operators"]

## Errors
None documented.

## Examples
Conformance cases: `conformance/equality/`.

## Open questions
- D-026: `===` on two equal string literals (`"a" === "a"`) and on two equal `float` values is `true` in the tree-walk interpreter; the reference describes strings as `{ data, len }` views whose identity is an address comparison and does not say what `===` means for them.
