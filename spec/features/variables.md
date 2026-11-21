# Variables and constants

Status: tree-walk 14/14 | VM 9/14 | C 11/14   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `var`.
A variable is declared with `val name = expression` (immutable) or `var name = expression` (mutable), optionally with a type after the name (`val name type = expression`). A `var` is updated with `name = expression` or with a compound assignment operator (`+=`, `-=`, `*=`, `/=`, `//=`, `%=`). Blocks, including a standalone `{ ... }` block statement, introduce nested scopes.

## Rules
- VAR-1: `val name = expr` declares a deeply (transitively) immutable binding whose value is the result of evaluating `expr` once; neither the binding nor any field, element, or nested value reachable through it at any depth may be reassigned or mutated. [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"; spec/decisions/D-177-value-semantics-cow-and-mut.md]
- VAR-2: `var name = expr` declares a mutable binding that later assignments `name = expr2` update, and whose value may be mutated at any depth provided every struct field traversed along the mutation path is marked `mut` (STRUCT-4, STRUCT-7). [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"; spec/decisions/D-177-value-semantics-cow-and-mut.md]
- VAR-3: The type of a `val` or `var` is inferred from its initializer and is used by the checker wherever the variable appears later. [src: LANGUAGE_REFERENCE.md §3 "Type System and Data Types"]
- VAR-4: A compound assignment `target op= value` with `op` one of `+`, `-`, `*`, `/`, `//` and `%` stores the result of `target op value` in `target`; target path subexpressions in compound and nested assignments (`v[f()] += 1`, `a:b[f()] = g()`) are evaluated exactly once left-to-right before reading or mutating the target slot. [src: LexerTests.TestOperators; StdlibTests.TestMapCompoundAssignment; spec/decisions/D-164-floor-division-and-modulo.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- VAR-5: A declaration inside a block (`if`, `for`, function body) is visible only in that block and shadows a same-named outer variable until the block ends, without changing the outer variable. [src: CheckerTests.TestScopeShadowingInLocalBlocks; ScopingExecutionTests.TestScopeShadowingInLocalBlocksExecution]
- VAR-6: A function body does not see the local variables of its caller, and its own locals do not affect the caller's locals of the same name. [src: CheckerTests.TestScopeLeakageFromCallerToCallee; ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- VAR-7: Functions and struct methods can read the top-level `val` and `var` declarations that appear above them in the file (private top-level `var` declarations are allowed in non-root imported modules, while Program Mode root files forbid top-level `var` per MAIN-E3); a top-level `val` or `var` declared below a function is not visible to it (VAR-E8). [src: CheckerTests.TestScopeLeakageGlobalPreservation; ScopingExecutionTests.TestDeeplyNestedCallerScopesExecution; spec/decisions/D-165-declaration-hoisting.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- VAR-8: A lambda reads the variables of its enclosing scopes, including variables declared in enclosing blocks. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution; CheckerTests.TestLambdaCaptureScoping]

- VAR-9: Assignment (`=`, `+=`, `-=`, `*=`, `/=`, `%=`) is a right-associative expression at the loosest precedence level, so `a = b = 0` assigns `0` to `b` and then to `a`. [src: spec/decisions/D-141-assignment-and-relational-precedence.md]

- VAR-10: A declaration may write a type after the name and before `=`, as in `val n int = 12` or `var p Person[string] = Person { ... }`; the initializer is still required and the declared type is the type of the variable. [src: spec/decisions/D-012-explicit-type-annotation-syntax.md]
- VAR-11: A `{ ... }` at the start of a statement is a block statement that opens a nested scope, in which a declaration shadows an outer name until the block ends. [src: spec/decisions/D-161-standalone-blocks.md]

## Errors
- VAR-E1: Assigning to a `val` (or any other immutable binding: a non-`mut` parameter, a loop variable, a `when`/`is` pattern binding, or implicit `it`) after its declaration (with `=` or a compound assignment) is rejected by the checker before execution. [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"; spec/decisions/D-017-val-reassignment-detection.md; spec/decisions/D-177-value-semantics-cow-and-mut.md; spec/decisions/D-180-mut-parameters-calls-and-closures.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- VAR-E2: Assigning a value of a different type, such as a string to an `int` variable, is rejected by the checker. [src: LANGUAGE_REFERENCE.md §2 "Variables and Constants"]
- VAR-E3: Using a variable whose inferred type is `string` where an `int` is required is rejected by the checker. [src: LANGUAGE_REFERENCE.md §3 "Type System and Data Types"]
- VAR-E4: Redeclaring a name in the same scope is rejected by the checker, while shadowing in a nested block remains allowed. [src: spec/decisions/D-018-redeclaration-in-same-scope.md]
- VAR-E5: Assigning a `float` value to an `int` variable is rejected as a type error. [src: spec/decisions/D-019-int-float-mixing-and-division.md]

- VAR-E6: An initializer whose type is not assignable to the declared type, such as `val n int = "x"`, is rejected by the checker with a message containing "Cannot assign". [src: spec/decisions/D-012-explicit-type-annotation-syntax.md]
- VAR-E7: A `val` whose initializer is `Option::None`, such as `val x Option[int] = Option::None`, is rejected by the checker with a message containing "Option::None" because it can never hold a value; `var x Option[int] = Option::None` is the form for an empty variable. [src: spec/decisions/D-160-option-none-in-val.md]
- VAR-E8: A function or method that reads a top-level `val` or `var` declared below it, in script mode and in program mode alike, is rejected by the checker with a message containing "is not declared", even when the function is only called after the `val` or `var` has been initialized. [src: spec/decisions/D-165-declaration-hoisting.md]
- VAR-E9: Using `/=` on an `int` target is rejected by the checker because `/` produces a `float` that cannot be stored into an `int` variable, with a message suggesting `//=` for integer division; `/=` on a `float` target is unchanged. [src: spec/decisions/D-019-int-float-mixing-and-division.md]
- VAR-E10: Mutating the interior of a `val` (direct or nested field assignment, collection index assignment, calling a `mut fn` or mutating stdlib method, or passing a `val` or its interior as a `mut` argument) is rejected by the checker. [src: spec/decisions/D-177-value-semantics-cow-and-mut.md]
- VAR-E11: Mutating a temporary rvalue expression (such as `[1, 2]:push(3)`, `Point { x: 1 }:inc()`, or assigning to a field or index of a call result) is rejected by the checker because mutation requires a mutable variable place. [src: spec/decisions/D-177-value-semantics-cow-and-mut.md]

## Examples
Conformance cases: `conformance/variables/`.

## Open questions
None.
