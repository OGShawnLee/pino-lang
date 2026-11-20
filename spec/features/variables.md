# Variables and constants

Status: tree-walk 14/14 | VM 9/14 | C 11/14   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `var`.
A variable is declared with `val name = expression` (immutable) or `var name = expression` (mutable). A `var` is updated with `name = expression` or with a compound assignment operator (`+=`, `-=`, `*=`, `/=`, `%=`). Blocks introduce nested scopes.

## Rules
- VAR-1: `val name = expr` declares an immutable binding whose value is the result of evaluating `expr` once. [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"]
- VAR-2: `var name = expr` declares a mutable variable that later assignments `name = expr2` update. [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"]
- VAR-3: The type of a `val` or `var` is inferred from its initializer and is used by the checker wherever the variable appears later. [src: LANGUAGE_REFERENCE.md §3 "Type System and Data Types"]
- VAR-4: A compound assignment `target op= value` with `op` one of `+`, `-`, `*`, `/` and `%` stores the result of `target op value` in `target`. [src: LexerTests.TestOperators; StdlibTests.TestMapCompoundAssignment]
- VAR-5: A declaration inside a block (`if`, `for`, function body) is visible only in that block and shadows a same-named outer variable until the block ends, without changing the outer variable. [src: CheckerTests.TestScopeShadowingInLocalBlocks; ScopingExecutionTests.TestScopeShadowingInLocalBlocksExecution]
- VAR-6: A function body does not see the local variables of its caller, and its own locals do not affect the caller's locals of the same name. [src: CheckerTests.TestScopeLeakageFromCallerToCallee; ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- VAR-7: Functions and struct methods can read top-level `val` declarations. [src: CheckerTests.TestScopeLeakageGlobalPreservation; ScopingExecutionTests.TestDeeplyNestedCallerScopesExecution]
- VAR-8: A lambda reads the variables of its enclosing scopes, including variables declared in enclosing blocks. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution; CheckerTests.TestLambdaCaptureScoping]

- VAR-9: Assignment (`=`, `+=`, `-=`, `*=`, `/=`, `%=`) is a right-associative expression at the loosest precedence level, so `a = b = 0` assigns `0` to `b` and then to `a`. [src: spec/decisions/D-141-assignment-and-relational-precedence.md]

## Errors
- VAR-E1: Assigning to a `val` after its declaration (with `=` or a compound assignment) is rejected by the checker before execution. [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"; spec/decisions/D-017-val-reassignment-detection.md]
- VAR-E2: Assigning a value of a different type, such as a string to an `int` variable, is rejected by the checker. [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"]
- VAR-E3: Using a variable whose inferred type is `string` where an `int` is required is rejected by the checker. [src: LANGUAGE_REFERENCE.md §3 "Type System and Data Types"]
- VAR-E4: Redeclaring a name in the same scope is rejected by the checker, while shadowing in a nested block remains allowed. [src: spec/decisions/D-018-redeclaration-in-same-scope.md]
- VAR-E5: Assigning a `float` value to an `int` variable is rejected as a type error. [src: spec/decisions/D-019-int-float-mixing-and-division.md]

## Examples
Conformance cases: `conformance/variables/`.

## Open questions
- D-012: How are explicit type annotations written (`val x: int = 10`, `val x int = 10`)? Today neither form parses.
