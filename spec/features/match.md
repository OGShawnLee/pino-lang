# Match

Status: tree-walk 16/16 | VM 6/16 | C 15/16   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `match`.
`match subject { ... }` contains `when` arms followed by an optional `else` arm. A `when` arm lists one or more values (commas between them are optional) and then has either a braced block or `->` with a single expression. The same construct is a statement and an expression; in expression position the arms produce the value of the `match`, with `->` for single expressions and `yield` inside block arms.

## Rules
- MATCH-1: `match subject { when v { ... } ... }` evaluates `subject` and executes the block of the first `when` arm whose value matches; the other arms are skipped. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; ControlFlowTests.TestMatchStatementExecution; proposals/006-match-expressions.md §4.4]
- MATCH-2: A `when` arm may list several values separated by commas, and it matches when any of them matches. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; ControlFlowTests.TestMatchStatementExecution]
- MATCH-3: The commas between the values of a `when` arm are optional. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"]
- MATCH-4: The `else` arm executes when no `when` arm matches. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; ControlFlowTests.TestMatchStatementExecution]
- MATCH-5: When the subject is an enum value, a `when Enum::Member` arm matches that member, and a `match` that covers every member needs no `else` arm. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; ControlFlowTests.TestMatchStatementExecution; MatchExpressionTests.TestMatchStatementStillWorks]
- MATCH-6: `match` may be used as an expression, and its value is the value produced by the selected arm. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; proposals/006-match-expressions.md §1]
- MATCH-7: An arm written `when v -> expr` or `else -> expr` produces the value of the single expression `expr`. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; MatchExpressionTests.TestMatchExpressionArrowSyntax; proposals/006-match-expressions.md §5; spec/decisions/D-100-arrow-token.md]
- MATCH-8: An arm written as a block `{ ... }` inside a `match` expression produces the value of the `yield` statement it executes, and only the block of the selected arm runs. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; MatchExpressionTests.TestMatchExpressionBlockYieldSyntax]
- MATCH-9: A `match` expression may be the operand of `return` and the initializer of a variable declaration. [src: MatchExpressionTests.TestMatchExpressionArrowSyntax; LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"]

- MATCH-10: A block arm of a `match` expression may execute `return`, which exits the enclosing function without producing a value for the `match` and requires no `yield`. [src: spec/decisions/D-143-return-in-match-and-yield-scope.md]

## Errors
- MATCH-E1: A `match` over an enum subject that does not cover every member and has no `else` arm is rejected with a message containing "is not exhaustive", in both statement and expression form. [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; MatchExpressionTests.TestMatchExpressionCheckerExhaustiveness]
- MATCH-E2: A `match` over a union subject that does not cover every variant and has no `else` arm is rejected with a message containing "is not exhaustive". [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"]
- MATCH-E3: A `match` expression whose arms produce incompatible types is rejected with a message containing "incompatible types". [src: MatchExpressionTests.TestMatchExpressionCheckerTypeMismatch; proposals/006-match-expressions.md §4.3]
- MATCH-E4: A block arm written with `->` before the block, such as `when 2 -> { ... }`, is rejected with a message containing "Expected expression". [src: LANGUAGE_REFERENCE.md §5 "Pattern Matching (`match-when`)"; spec/decisions/D-100-arrow-token.md]
- MATCH-E5: A `match` statement or expression whose subject is neither an enum nor a union and that has no `else` arm is rejected with a message containing "is not exhaustive". [src: proposals/006-match-expressions.md §4.3; spec/decisions/D-031-match-without-else-on-plain-subjects.md]

- MATCH-E6: An arrow arm `when X -> return` or `else -> return` is rejected as a parse error because `->` arms accept only an expression. [src: spec/decisions/D-143-return-in-match-and-yield-scope.md]

## Examples
Conformance cases: `conformance/match/`.

## Open questions
- D-034: What does an identifier in a `when` arm mean? The parser reads it as a binding that matches any value, not as a comparison with a variable.
