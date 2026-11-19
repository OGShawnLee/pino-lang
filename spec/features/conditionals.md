# Conditionals

Status: tree-walk 7/7 | VM 7/7 | C 7/7   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `if`.
`if` has two forms. The statement form `if cond { ... } else if cond { ... } else { ... }` runs blocks for their effects; the condition is not parenthesised and the braces are mandatory. The expression form `if cond then a else b` is a single-line, value-producing conditional.

## Rules
- IF-1: `if cond { ... }` executes its block when `cond` is true and skips it otherwise; the condition needs no parentheses. [src: LANGUAGE_REFERENCE.md §5 "Conditionals (`if-else`)"; ControlFlowTests.TestVMControlFlow]
- IF-2: In `if cond { A } else { B }`, block `B` executes when `cond` is false and block `A` is skipped. [src: LANGUAGE_REFERENCE.md §5 "Conditionals (`if-else`)"; ControlFlowTests.TestVMControlFlow]
- IF-3: In an `if ... else if ... else ...` chain the conditions are tested in order and only the block of the first true condition executes; the final `else` block executes only when no condition is true, and without a final `else` nothing executes. [src: LANGUAGE_REFERENCE.md §5 "Conditionals (`if-else`)"]
- IF-4: A `return` executed inside an `if` block ends the containing function. [src: ControlFlowTests.TestVMFunctionsAndRecursion]
- IF-5: `if cond then a else b` is an expression whose value is `a` when `cond` is true and `b` otherwise. [src: LANGUAGE_REFERENCE.md §5 "Conditionals (`if-else`)"]
- IF-6: An `if ... then ... else ...` expression may be used as the initializer of a `val` or `var` declaration. [src: LANGUAGE_REFERENCE.md §5 "Conditionals (`if-else`)"]

## Errors
- IF-E1: An `if` statement whose block is not enclosed in braces, such as `if x > 1 println("a")`, is rejected with a message containing "Expected '{'". [src: LANGUAGE_REFERENCE.md §5 "Conditionals (`if-else`)"]
- IF-E2: An `if ... then ...` expression without an `else` part is rejected with a message containing "Expected 'else' in ternary expression". [src: LANGUAGE_REFERENCE.md §5 "Conditionals (`if-else`)"]

## Examples
Conformance cases: `conformance/conditionals/`.

## Open questions
- D-032: Must an `if` condition have type `bool`? Today any value, including `0` and `""`, is accepted and treated as true.
- D-033: Must both branches of `if ... then a else b` have the same type? Today `if true then 1 else "x"` is accepted, while a `match` expression with incompatible arms is rejected.
- D-035: Is the arrow form `else => expr` part of the `if` statement? The parser accepts it, the reference does not mention it.
