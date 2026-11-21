# Enums

Status: tree-walk 8/8 | VM 1/8 | C 7/8   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `enum`.
An `enum` declaration names a closed set of members, separated by newlines or commas. A member is referenced as `Enum::Member`.

## Rules
- ENUM-1: `enum Name { A B C }` declares a closed set of named constants. [src: LANGUAGE_REFERENCE.md §7 "Enums"; DataStructureTests.TestEnumComparisonAndEvaluation]
- ENUM-2: A member is referenced with the scope resolution operator, `Enum::Member`. [src: LANGUAGE_REFERENCE.md §7 "Enums"]
- ENUM-3: Members may be separated by commas (`enum LogLevel { Info, Warning, Error }`) as well as by newlines. [src: DataStructureTests.TestEnumComparisonAndEvaluation]
- ENUM-4: Two enum values are equal (`==`) when they are the same member of the same enum, and `!=` is the negation. [src: DataStructureTests.TestEnumComparisonAndEvaluation]
- ENUM-5: `str(Enum::Member)` returns the text `"Enum::Member"`. [src: DataStructureTests.TestEnumBuiltInFunctions]
- ENUM-6: `type(Enum::Member)` returns the text `"enum"`. [src: LANGUAGE_REFERENCE.md §9 "`type(val)`"; DataStructureTests.TestEnumBuiltInFunctions]
- ENUM-7: An enum is a type usable for parameters and struct fields, and its values can be compared and passed around like other values. [src: LANGUAGE_REFERENCE.md §7 "Enums"]
- ENUM-8: `match` over an enum value selects the branch whose `when Enum::Member` equals the value. [src: proposals/005-tagged-unions-and-patterns-roadmap.md]
- ENUM-9: `value is Enum::Member` and `value is not Enum::Member` test whether an enum value is a given member. [src: LANGUAGE_REFERENCE.md §7 "Variant Testing (`is` and `is not`)"]

## Errors
- ENUM-E1: A `match` over an enum that omits a member and has no `else` is rejected with a message containing "is not exhaustive". [src: proposals/005-tagged-unions-and-patterns-roadmap.md]
- ENUM-E2: Naming an enum member that the enum does not declare, as `Direction::Up` for an enum without `Up`, is rejected by the checker with a message containing "has no member". [src: spec/decisions/D-77-unknown-enum-member.md]

## Examples
Conformance cases: `conformance/enums/`.

## Open questions
None.
