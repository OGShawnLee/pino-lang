# Functions, lambdas, closures

Status: pending regeneration (ticket 009)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `fn`.
A function is declared with `fn`, a name, a parameter list (`name Type` pairs, parentheses optional when empty), an optional return type and a braced body. A lambda is an anonymous function expression written `fn (params) => expr` or `fn (params) { ... }`. Function types are written `fn(T1, T2) R`. The reference has no dedicated functions section; the rules below come from the function syntax used in its other sections and from the tests.

## Rules
- FN-1: `fn name(p1 T1, p2 T2) { ... }` declares a function with typed parameters, and `name(a1, a2)` calls it. [src: ParserTests.TestParseFunctionDeclaration; ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-2: A return type is written after the parameter list: `fn add(a int, b int) int { ... }`. [src: LANGUAGE_REFERENCE.md §7 "Structs" (`distance_to`); LANGUAGE_REFERENCE.md §8 "Error Handling" (`divide`); ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-3: `return expr` ends the call and the call expression evaluates to `expr`. [src: ParserTests.TestParseFunctionDeclaration; ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-4: The parentheses of the parameter list may be omitted when a function has no parameters, as in `fn name { ... }`. [src: ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution; ParserTests.TestParseStructDeclaration]
- FN-5: Commas between parameters are optional. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- FN-6: Commas between call arguments are optional. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- FN-7: A function with an empty parameter list `()` is declared and called with empty parentheses. [src: ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution; CheckerTests.TestTypeCheckerTupleValid]
- FN-8: `fn (params) => expr` is an expression whose value is a function that returns `expr`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods" (`map`); DataStructureTests.TestStructLambdaFieldExecution]
- FN-9: `fn (params) { ... }` is an expression whose value is a function with a block body that uses `return`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods" (`each`)]
- FN-10: A lambda with no parameters is written `fn () => expr`. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution]
- FN-11: A function value, whether a lambda or the name of a declared function, can be stored in a variable, passed as an argument and called through that variable or parameter. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution; DataStructureTests.TestStructCallableFieldsAndBoundMethods; CheckerTests.TestTypeCheckerTupleReturningFunction]
- FN-12: The type of a function value is written `fn(T1, T2) R`, and a parameter may have such a type. [src: DataStructureTests.TestStructCallableFieldsAndBoundMethods; StdlibTests.TestCustomGenericShorthandLambda]
- FN-13: A function may declare a function type as its return type and return a function value, as in `fn get_adder() fn(int) int { ... }`. [src: CheckerTests.TestTypeCheckerTupleReturningFunction; LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"]
- FN-14: A function declaration may appear inside another function's body, and it is callable from that body. [src: CheckerTests.TestTypeCheckerTupleReturningFunction]
- FN-15: A lambda can read the variables of the scopes enclosing it, including the locals of the enclosing function and of an enclosing block. [src: ScopingExecutionTests.TestLambdaCaptureScopingExecution]
- FN-16: A call argument that uses the undeclared name `it` is an implicit lambda with the single parameter `it`, so `xs:filter(it:len <= 4)` filters by that expression. [src: ParserTests.TestImplicitLambdaArgumentWrapping; StdlibTests.TestShorthandLambdaItResolution; LANGUAGE_REFERENCE.md §10 "Vector Methods" (`count`)]
- FN-17: When `it` is declared as a parameter of the enclosing function, an argument using `it` refers to that parameter and is not turned into a lambda. [src: ParserTests.TestNoImplicitLambdaWrappingWhenDeclared]
- FN-18: A called function does not see the local variables of its caller. [src: ScopingExecutionTests.TestScopeLeakageFromCallerToCalleeExecution]
- FN-19: A `val` declared in an inner block shadows an outer variable of the same name inside that block only, and the outer binding is visible again after the block. [src: ScopingExecutionTests.TestScopeShadowingInLocalBlocksExecution]

## Errors
- FN-E1: Calling a function with an argument of the wrong type is rejected with a type check error. [src: CheckerTests.TestEnumDefinitionAndTypeChecking; CheckerTests.TestRuneTypeChecker]

## Examples
Conformance cases: `conformance/functions/`.

## Open questions
- D-050: Is the return type of a function without a declared return type inferred, and from what?
- D-051: Commas are optional in parameter and argument lists (FN-5, FN-6); does that hold for a minus-prefixed argument (`f(x -y)`)? Follows the ticket-005 decision on commas vs. unary minus.
- D-052: The reference declares a method with an arrow body (`fn make_sound() string => "Woof"`); the parser rejects `=>` on declarations.
- D-053: Can a lambda state its return type (`fn (n int) int => ...`)? The parser rejects it.
- D-054: The reference's `each` example names a parameter `val`, a keyword the parser rejects.
- D-055: `fn_list[0](9)` is parsed as a generic call, not as a call of an indexed element.
- D-056: Does a lambda capture a mutated `var` by reference or by value?
- D-057: Should the argument-count and return-type errors be specified?
- D-002: `it` inside a call argument turns the argument into a lambda even in a loop body (see loops).
