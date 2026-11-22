# Error handling

Status: tree-walk 28/29 | VM 11/29 | C 23/29   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `err`.
Pino has no exceptions. Unrecoverable failures call the global `panic(message)`. Expected failures are values of the predeclared generic unions `Result[T, E]` (variants `Success(T)` and `Failure(E)`) and `Option[T]` (variants `Some(T)` and `None`). The postfix `?` propagates a failure to the caller, `expr or { ... }` recovers locally (the block sees `err` and ends with `yield`, `return` or `panic`), and a `main` that returns `Result` or `Option` ends the program on failure.

## Rules
- ERR-1: `Option[T]` (variants `Option::Some(T)` and `Option::None`) and `Result[T, E]` (variants `Result::Success(T)` and `Result::Failure(E)`) are available without being declared. [src: LANGUAGE_REFERENCE.md §8 "Error Handling"; CheckerTests.TestNativePreludeOptionAndResult]
- ERR-2: A `Result` value is consumed with `match` and `when Result::Success(v)` / `when Result::Failure(e)`. [src: LANGUAGE_REFERENCE.md §8 "Managing Error Type Collisions (Tactics)"; CheckerTests.TestNativeResult]
- ERR-3: `panic(message)` immediately halts the program, so no statement after the call runs. [src: LANGUAGE_REFERENCE.md §8 "The `panic` Mechanism"; ErrorHandlingTests.TestPanicHaltAndBacktrace]
- ERR-4: A `panic` raised inside nested calls halts the whole program and reports the panic message. [src: ErrorHandlingTests.TestPanicHaltAndBacktrace]
- ERR-5: A program ended by `panic` exits the process with code 101. [src: LANGUAGE_REFERENCE.md §8 "The `panic` Mechanism"]
- ERR-6: `panic(message)` has the type `any`, so it can be used where a value of any type is expected, such as `return panic("...")` in a function returning `int`. [src: LANGUAGE_REFERENCE.md §8 "The `panic` Mechanism"; LANGUAGE_REFERENCE.md §9 "`panic(message)`"]
- ERR-7: `expr?` on a `Result` or `Option` value that holds `Success(v)` or `Some(v)` evaluates to `v`. [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; ErrorHandlingTests.TestBubbleOperatorSuccessAndFailure; CheckerTests.TestUnionBubbleResultOperator]
- ERR-8: `expr?` on a `Failure(e)` immediately returns that `Failure` from the enclosing function, so the rest of the function does not run. [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; ErrorHandlingTests.TestBubbleOperatorSuccessAndFailure]
- ERR-9: `expr?` on `Option::None` immediately returns `Option::None` from the enclosing function. [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; CheckerTests.TestUnionBubbleOptionOperator]
- ERR-10: `expr or { ... }` evaluates to the success value of a `Result` and does not run the block when the value is `Success`. [src: LANGUAGE_REFERENCE.md §8 "Suffix Recovery Blocks (`or`)"; ErrorHandlingTests.TestRecoveryOrBlockWithYield]
- ERR-11: When a `Result` is a `Failure(e)`, the `or` block runs in a child scope in which `err` holds `e`, and `yield value` makes `value` the result of the whole expression. [src: LANGUAGE_REFERENCE.md §8 "Suffix Recovery Blocks (`or`)"; ErrorHandlingTests.TestRecoveryOrBlockWithYield; CheckerTests.TestUnionOrOperatorFailureErrCapturing]
- ERR-12: When an `Option` is `None`, the `or` block runs with `err` set to the string `"None"`, and `yield value` makes `value` the result. [src: LANGUAGE_REFERENCE.md §8 "Suffix Recovery Blocks (`or`)"]
- ERR-13: An `or` block may diverge with `return`, for example to map a foreign error into the enclosing function's own `Failure`. [src: LANGUAGE_REFERENCE.md §8 "Managing Error Type Collisions (Tactics)"; CheckerTests.TestUnionOrOperatorSuccess]
- ERR-14: An `or` block may diverge with `panic`. [src: LANGUAGE_REFERENCE.md §8 "Suffix Recovery Blocks (`or`)"]
- ERR-15: Functions that return `Result[T, E]` with the same error type `E`, including a user-declared union, can be combined with `?` in one function, and the failure can be consumed with `match`. [src: LANGUAGE_REFERENCE.md §8 "Managing Error Type Collisions (Tactics)"]
- ERR-16: A `?` whose error type is compatible with the enclosing function's declared error type is accepted. [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; CheckerTests.TestUnionBubbleResultOperator]
- ERR-17: A `?` is accepted on `Option` inside a function that returns `Option`. [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; CheckerTests.TestUnionBubbleOptionOperator]
- ERR-18: In Program Mode, if `main` returns a `Result` and the value is a `Failure` (returned or bubbled), the runtime ends the program and reports the failure message; a `Success` ends normally. [src: LANGUAGE_REFERENCE.md §8 "The `main()` Entry Point Failure Handler"]
- ERR-19: In Program Mode, if `main` returns an `Option` and the value is `None`, the runtime ends the program with a failure. [src: LANGUAGE_REFERENCE.md §8 "The `main()` Entry Point Failure Handler"]
- ERR-20: When `main` ends the program because of a failure, the process exit code is 1 and the message is written to `stderr`. [src: LANGUAGE_REFERENCE.md §8 "The `main()` Entry Point Failure Handler"] [no-case: the exit code and stderr stream of the in-process engines are not observable by the conformance runner, and the C backend does not produce a valid exit code for main (D-080)]
- ERR-21: A `panic` prints a stack backtrace of the calling frames to `stderr`. [src: LANGUAGE_REFERENCE.md §8 "The `panic` Mechanism"; ErrorHandlingTests.TestPanicHaltAndBacktrace] [no-case: the backtrace goes to stderr, which the conformance runner does not capture for the in-process engines; see D-081]

- ERR-22: `or { ... }` is a postfix recovery operator binding at the same precedence level as `?`, so `a + b or { yield 0 }` recovers only `b`. [src: spec/decisions/D-140-or-recovery-precedence.md]
- ERR-23: The success value of a `Result[void, E]` is written `Result::Success` (bare, with no parentheses, like `Option::None`), as in `return Result::Success` in a function declared `Result[void, E]`. [src: spec/decisions/D-162-void-as-a-writable-type.md]
- ERR-24: When `main` ends the program with a failure (`ERR-18`, `ERR-19`), the runtime writes `Error: <str(e)>` to `stderr` for `Result::Failure(e)`, or `Error: Option::None` for `Option::None`, followed by a newline, and exits with code 1 (`ERR-20`). [src: spec/decisions/D-080-main-failure-handler-output.md; spec/decisions/D-186-print-forms-and-ir-prereqs.md] [no-case: the stderr text and exit code of the in-process engines are not observable by the conformance runner]

## Errors
- ERR-E1: Using `?` outside a function body is rejected with a message containing "without a declared Result or Option return type". [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; ErrorHandlingTests.TestCheckerEnforcesSafetyRules]
- ERR-E2: Using `?` in a function whose declared return type is not `Result` or `Option` is rejected with a message containing "Enclosing function must return a Result". [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; ErrorHandlingTests.TestCheckerEnforcesSafetyRules]
- ERR-E3: Bubbling an error whose type differs from the enclosing function's declared error type is rejected with a message containing "Cannot bubble error of type". [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"; CheckerTests.TestUnionBubbleMismatchingErrorTypes; CheckerTests.TestUnionBubbleMismatchingErrorTypesComplex]
- ERR-E4: Using `?` on a `Result` inside a function that returns `Option` is rejected with a message containing "Enclosing function must return a Result". [src: CheckerTests.TestUnionBubbleOptionResultMismatch]
- ERR-E5: `yield` outside an `or` recovery block or a `match` expression block arm is rejected as a compile error. [src: ErrorHandlingTests.TestCheckerEnforcesSafetyRules; spec/decisions/D-143-return-in-match-and-yield-scope.md]
- ERR-E6: Yielding a value whose type differs from the success type of the recovered expression is rejected with a message containing "Yield statement expected type". [src: LANGUAGE_REFERENCE.md §8 "Suffix Recovery Blocks (`or`)"; CheckerTests.TestUnionYieldMismatchingType]
- ERR-E7: Using `?` on an expression that is neither a `Result` nor an `Option` is rejected with a message containing "non-union type". [src: LANGUAGE_REFERENCE.md §8 "The Bubble Operator (`?`)"]
- ERR-E8: Writing `Result::Success()` with empty parentheses for a `Result[void, E]` is rejected by the checker with help text "write `Result::Success`". [src: spec/decisions/D-162-void-as-a-writable-type.md]

## Examples
Conformance cases: `conformance/errors/`.

## Open questions
- D-081: What must the `panic` output look like, including the backtrace, and must the C backend print it?
