# Functions, lambdas, closures

Status: tree-walk 18/18 | VM 11/18 | C 11/18   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `fn`.
A function is declared with `fn`, a name, a parameter list (`name Type` pairs, parentheses optional when empty), an optional return type and a braced body. A lambda is an anonymous function expression written `fn (params) -> expr` or `fn (params) { ... }`. Function types are written `fn (T1, T2) -> R` (parentheses optional when empty, omitted return means `void`, so `fn` alone is `fn () -> void`). The reference has no dedicated functions section; the rules below come from the function syntax used in its other sections and from the tests.

## Rules
- FN-1: `fn name(p1 T1, p2 T2) { ... }` declares a function with typed parameters, and `name(a1, a2)` calls it. [src: ParserTests.TestParseFunctionDeclaration; ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-2: A return type is written after the parameter list: `fn add(a int, b int) int { ... }`. [src: LANGUAGE_REFERENCE.md §7 "Structs" (`distance_to`); LANGUAGE_REFERENCE.md §8 "Error Handling" (`divide`); ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-3: `return expr` ends the call and the call expression evaluates to `expr`. [src: ParserTests.TestParseFunctionDeclaration; ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-4: The parentheses of the parameter list may be omitted when a function has no parameters, as in `fn name { ... }`. [src: ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution; ParserTests.TestParseStructDeclaration]
- FN-5: Commas between parameters are optional. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- FN-6: Commas between call arguments are optional. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- FN-7: A function with an empty parameter list `()` is declared and called with empty parentheses. [src: ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution; CheckerTests.TestTypeCheckerTupleValid]
- FN-8: `fn (params) -> expr` is an expression whose value is a function that returns `expr`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods" (`map`); DataStructureTests.TestStructLambdaFieldExecution; spec/decisions/D-100-arrow-token.md]
- FN-9: `fn (params) { ... }` is an expression whose value is a function with a block body that uses `return`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods" (`each`)]
- FN-10: A lambda with no parameters is written `fn () -> expr`. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution; spec/decisions/D-100-arrow-token.md]
- FN-11: A function value, whether a lambda or the name of a declared function, can be stored in a variable, passed as an argument and called through that variable or parameter. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution; DataStructureTests.TestStructCallableFieldsAndBoundMethods; CheckerTests.TestTypeCheckerTupleReturningFunction]
- FN-12: The type of a function value is written `fn (T1, T2) -> R`, and a parameter may have such a type. [src: DataStructureTests.TestStructCallableFieldsAndBoundMethods; StdlibTests.TestCustomGenericShorthandLambda; spec/decisions/D-101-function-type-syntax.md]
- FN-13: A function may declare a function type as its return type and return a function value, as in `fn get_adder() fn (int) -> int { ... }`. [src: CheckerTests.TestTypeCheckerTupleReturningFunction; LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; spec/decisions/D-101-function-type-syntax.md]
- FN-14: A function declaration may appear inside another function's body, and it is callable from that body. [src: CheckerTests.TestTypeCheckerTupleReturningFunction]
- FN-15: A lambda can read the variables of the scopes enclosing it, including the locals of the enclosing function and of an enclosing block. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution]
- FN-16: A call argument that uses the undeclared name `it` is an implicit lambda with the single parameter `it`, so `xs:filter(it:len <= 4)` filters by that expression. [src: ParserTests.TestImplicitLambdaArgumentWrapping; StdlibTests.TestShorthandLambdaItResolution; LANGUAGE_REFERENCE.md §10 "Vector Methods" (`count`)]
- FN-17: When `it` is declared as a parameter of the enclosing function, an argument using `it` refers to that parameter and is not turned into a lambda. [src: ParserTests.TestNoImplicitLambdaWrappingWhenDeclared]
- FN-18: A called function does not see the local variables of its caller. [src: ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-19: A `val` declared in an inner block shadows an outer variable of the same name inside that block only, and the outer binding is visible again after the block. [src: ScopingExecutionTests.TestScopeShadowingInLocalBlocksExecution]
- FN-20: In a function type, parentheses may be omitted when there are no parameters (`fn -> R`), and an omitted return type means `void`, so `fn` alone is `fn () -> void` and `fn (T)` is `fn (T) -> void`. [src: spec/decisions/D-101-function-type-syntax.md]
- FN-21: Closures capture enclosing `var` bindings by reference per closure instance, so mutations persist across calls to that closure without affecting other instances. [src: spec/decisions/D-056-closure-mutable-capture.md]

- FN-22: `return` at the end of a line continues onto the next line like a trailing binary operator, and is bare (returns `void`) only when the next token is `}` or end of file. [src: spec/decisions/D-142-return-newline-continuation.md]
- FN-23: A line break before `(` ends the preceding expression, so a call's `(` must appear on the same line as its callee. [src: spec/decisions/D-144-newline-before-open-paren.md]
- FN-24: Untyped lambda parameters must be separated by commas (`fn (n, m) -> n + m`), whereas typed parameters remain comma-optional, so `fn (n m)` declares one parameter `n` of type `m`. [src: spec/decisions/D-145-untyped-lambda-param-commas.md]
- FN-25: Calling an indexed expression such as `fns[0](9)` calls the indexed element when the brackets contain an expression rather than type arguments. [src: spec/decisions/D-055-indexed-element-call.md]

- FN-26: Declarations (`fn`, `struct`, `enum`, `union` and `interface`) are hoisted at module level and inside nested blocks, so they can be used before their textual position in the same scope; `val` and `var` are not hoisted. [src: spec/decisions/D-165-declaration-hoisting.md]
- FN-27: A named function without a declared return type has the type of its `return` statements as its return type, and is `void` when it has no value-returning `return`. [src: spec/decisions/D-050-function-return-type-inference.md]
- FN-28: Return-type inference does not depend on the order of the base case and a recursive call, so `if n > 0 { return n * f(n - 1) } return 1` infers `int` just as the early-base-case form does, and mutual recursion works in any declaration order. [src: spec/decisions/D-050-function-return-type-inference.md]
- FN-29: When one `return` of an unannotated function yields `float` and the others yield `int`, the inferred return type is `float`. [src: spec/decisions/D-050-function-return-type-inference.md; spec/decisions/D-019-int-float-mixing-and-division.md]
- FN-30: Untyped lambda parameters take their types from the context the lambda appears in: a typed `val` or `var`, a `return` in a function with a known return type, a struct field type, a collection element type or a call argument. [src: spec/decisions/D-167-lambda-parameter-inference.md]

## Errors
- FN-E1: Calling a function with an argument of the wrong type is rejected with a type check error. [src: CheckerTests.TestEnumDefinitionAndTypeChecking; CheckerTests.TestRuneTypeChecker]
- FN-E2: Named functions and methods must have block bodies (`{ ... }`); an arrow body on a named function or method (`fn f() int -> 1`) is rejected as a parse error. [src: spec/decisions/D-052-arrow-bodied-function-declarations.md]
- FN-E3: A lambda with an explicit return type (`fn (n int) int -> n`) is rejected as a parse error. [src: spec/decisions/D-053-lambda-return-type-annotation.md]

- FN-E4: Recursion in an unannotated function with no non-recursive `return` is rejected by the checker with a message containing "explicit return type". [src: spec/decisions/D-050-function-return-type-inference.md]
- FN-E5: `return` statements of an unannotated function that yield different struct types are rejected by the checker with a message containing "conflicting return types", suggesting a declared interface return type. [src: spec/decisions/D-050-function-return-type-inference.md]
- FN-E6: A function whose return type is not `void` and that does not return on every path is rejected by the checker with a message containing "always return". [src: spec/decisions/D-050-function-return-type-inference.md]
- FN-E7: A bare `return` in a function whose return type is not `void` is rejected by the checker with a message containing "must return a value". [src: spec/decisions/D-050-function-return-type-inference.md]

## Examples
Conformance cases: `conformance/functions/`.

## Open questions
- D-054: The reference's `each` example names a parameter `val`, a keyword the parser rejects.
- D-057: Should the argument-count and return-type errors be specified?
- D-002: `it` inside a call argument turns the argument into a lambda even in a loop body (see loops).
