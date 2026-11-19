# Labeled tuples

Status: tree-walk 16/16 | VM 16/16 | C 14/16   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `tup`.
Labeled tuples exist only to return several named values from a function. A function declares `@(label Type, ...)` as its return type, returns a tuple literal `@(label: expr, ...)`, and the caller unpacks the result with a destructuring declaration `val @(label, label: variable) = call()`. Tuples are not independent values: they cannot be stored in a variable or passed as an argument.

## Rules
- TUP-1: A function declares a tuple return type by writing `@(label Type, ...)` after its parameter list. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; ParserTests.TestParseTupleReturnSignatureAndLiteral]
- TUP-2: A function with a tuple return type returns a tuple literal `@(label: expr, ...)` whose labels and value types match the declaration. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestTupleBasicReturnAndDestructure]
- TUP-3: In a tuple literal, a bare name `x` is shorthand for `x: x`. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestTupleShorthandLiteralExecution]
- TUP-4: `val @(a, b) = call()` binds each variable to the tuple field with the same label. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestTupleBasicReturnAndDestructure]
- TUP-5: `val @(label: name) = call()` binds the variable `name` to the field `label`. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestTupleDestructureRenaming]
- TUP-6: A destructuring pattern may omit fields, and only the named fields are bound. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestTupleDestructureIgnoringFields]
- TUP-7: The fields of a returned tuple literal may be written in any order relative to the declared return type. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestTupleOrderIndependentExecution]
- TUP-8: The fields of a destructuring pattern may be written in any order relative to the declared return type. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestTupleOrderIndependentExecution]
- TUP-9: In a generic function, generic type parameters used inside a tuple return type are substituted by the type arguments of the call. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; TupleTests.TestGenericTupleOrderIndependent]
- TUP-10: A function type may return a tuple, as in `fn() @(x int, y int)`, and the labels of the returned tuple are matched by name rather than by position. [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; CheckerTests.TestTypeCheckerTupleReturningFunction; CheckerTests.TestTypeCheckerTupleOrderIndependent]

## Errors
- TUP-E1: A tuple literal anywhere except as the expression of a `return` statement, for example `val t = @(x: 1, y: 2)`, is rejected with a message containing "Tuple literals can only be used as a return value". [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"; CheckerTests.TestTypeCheckerTupleIllegalContext]
- TUP-E2: A tuple literal passed as a function argument is rejected with a message containing "Tuple literals can only be used as a return value". [src: LANGUAGE_REFERENCE.md §3 "Labeled Tuples (Return-Exclusive)"]
- TUP-E3: Returning a tuple whose field types do not match the declared tuple type is rejected with a message containing "declared return type". [src: CheckerTests.TestTypeCheckerTupleInvalidReturn]
- TUP-E4: Repeating a label in one tuple literal is rejected with the message "Duplicate label 'x' in tuple literal." (with the repeated label). [src: CheckerTests.TestTypeCheckerTupleDuplicateLabelsReturn]
- TUP-E5: Destructuring a label that the tuple type does not have is rejected with a message containing "Field 'z' does not exist in tuple type" (with the unknown label). [src: CheckerTests.TestTypeCheckerTupleDestructuringMismatch]
- TUP-E6: Binding the same variable name twice in one destructuring pattern is rejected with the message "Duplicate variable name 'a' in destructuring." (with the repeated name). [src: CheckerTests.TestTypeCheckerTupleDestructuringDuplicateVariables]
- TUP-E7: Returning a function whose tuple labels differ from the declared function type is rejected with a message containing "declared return type 'fn() @(x:int,y:int)', but returned 'fn() @(a:int,b:int)'". [src: CheckerTests.TestTypeCheckerTupleReturningFunctionInvalid]

## Examples
Conformance cases: `conformance/tuples/`.

## Open questions
None.
