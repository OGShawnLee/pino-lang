# Structs and embedding

Status: tree-walk 25/25 | VM 11/25 | C 19/25   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `struct`.
A `struct` declaration lists fields (`name type`), embedded structs (a bare struct name) and methods (`fn`, optionally `static fn`). An instance is created with a struct literal that names its fields. Fields and instance methods are reached with `instance:member`; static methods with `Struct::member`. Inside an instance method the receiver is available as `self` or `this`, and the declared fields can also be used by their bare names.

## Rules
- STRUCT-1: A `struct` declares a type with named, typed fields; `Name { field: value, ... }` creates an instance and `instance:field` reads a field. [src: LANGUAGE_REFERENCE.md §7 "Structs"]
- STRUCT-2: A method declared with `fn` in a struct is called as `instance:method(args)`, takes parameters and may return a value. [src: LANGUAGE_REFERENCE.md §7 "Structs"]
- STRUCT-3: Inside an instance method, a field of the struct can be read by its bare name. [src: LANGUAGE_REFERENCE.md §7 "Structs"; DataStructureTests.TestStructSelfThisAndProperties]
- STRUCT-4: Inside an instance method, assigning to a field's bare name updates that field of the receiver. [src: DataStructureTests.TestStructSelfThisAndProperties; DataStructureTests.TestStructSelfThisImmediateSync]
- STRUCT-5: Inside an instance method, `self` and `this` both refer to the receiver, so `self:field` and `this:field` read its fields. [src: LANGUAGE_REFERENCE.md §7 "Structs"; DataStructureTests.TestStructSelfThisImmediateSync]
- STRUCT-6: Compound assignment through the receiver, such as `self:hp -= dmg`, updates the field of the receiver. [src: LANGUAGE_REFERENCE.md §7 "Composition and Embedding"]
- STRUCT-7: A field of an instance can be assigned from outside the struct with `instance:field = value`. [src: DataStructureTests.TestStructStatePattern]
- STRUCT-8: A struct that names another struct as a bare member embeds it: its fields are initialised in the outer struct literal and read with `outer:field`. [src: LANGUAGE_REFERENCE.md §7 "Composition and Embedding"; CheckerTests.TestStructEmbeddingChecking; DataStructureTests.TestStructEmbeddingEvaluation]
- STRUCT-9: The methods of an embedded struct can be called on the outer instance and operate on the embedded fields. [src: LANGUAGE_REFERENCE.md §7 "Composition and Embedding"]
- STRUCT-10: A method declared in the outer struct takes precedence over an embedded method with the same name. [src: DataStructureTests.TestStructEmbeddingEvaluation]
- STRUCT-11: A method declared `static fn` is called as `Struct::method(args)` without an instance. [src: DataStructureTests.TestStaticMethodEvaluation; CheckerTests.TestStaticMethodTypeCheckingErrors]
- STRUCT-12: A field whose type is a function type (for example `increment fn(int) int`) can hold a lambda or function and is called as `instance:field(args)`. [src: DataStructureTests.TestStructCallableFieldsAndBoundMethods; DataStructureTests.TestStructLambdaFieldExecution; CheckerTests.TestTypeCheckerLambdaAsStructFieldAssignmentPasses]
- STRUCT-13: A static method used as a value (`Struct::method`, no call) can be stored in a field of a matching function type and called through it. [src: DataStructureTests.TestStructStaticMethodReferencedAsField; CheckerTests.TestMethodCallOnMethodTypedStaticFieldThrows]
- STRUCT-14: `instance:method` without a call yields a callable bound to that instance, and calling it affects the instance. [src: DataStructureTests.TestStructCallableFieldsAndBoundMethods; DataStructureTests.TestStructBoundMethodStateMutation]

## Errors
- STRUCT-E1: Reading a field that the struct does not declare is rejected with a message containing "does not have field or instance method". [src: CheckerTests.TestAccessOnMissingField]
- STRUCT-E2: Calling a method that the struct does not declare is rejected with a message containing "does not have method". [src: CheckerTests.TestAccessOnMissingMethod]
- STRUCT-E3: Using `this` inside a `static fn` is rejected with a message containing "Cannot access 'this' from static method". [src: CheckerTests.TestStaticMethodTypeCheckingErrors]
- STRUCT-E4: Using an instance field by its bare name inside a `static fn` is rejected with a message containing "Cannot access instance field". [src: CheckerTests.TestStaticMethodTypeCheckingErrors]
- STRUCT-E5: Calling a static method through an instance (`instance:static_fn()`) is rejected with a message containing "Cannot call static method". [src: CheckerTests.TestStaticMethodTypeCheckingErrors]
- STRUCT-E6: Calling an instance method through the struct name (`Struct::method()`) is rejected with a message containing "is not static". [src: CheckerTests.TestStaticMethodTypeCheckingErrors]
- STRUCT-E7: Initialising a function-typed field with a function of an incompatible type is rejected with a message containing "Cannot assign type". [src: CheckerTests.TestTypeCheckerMethodTypedFieldIncompatibleAssignmentThrows]
- STRUCT-E8: Calling a function-typed field with an argument of the wrong type is rejected with a message containing "for callable field". [src: CheckerTests.TestTypeCheckerMethodTypedFieldArgTypeMismatchThrows]
- STRUCT-E9: Calling a function-typed field with the wrong number of arguments is rejected with a message containing "expected 1 arguments, but got 2". [src: CheckerTests.TestTypeCheckerMethodTypedFieldArgCountMismatchThrows]
- STRUCT-E10: Calling a field that is not of a function type is rejected with a message containing "does not have method". [src: CheckerTests.TestTypeCheckerNonFunctionFieldCallThrows]
- STRUCT-E11: Initialising a field with a value of an incompatible type is rejected with a message containing "Cannot assign type". [src: CheckerTests.TestGenericMapValueTypeMismatchThrows]

## Examples
Conformance cases: `conformance/structs/`.

## Open questions
- D-070: Do structs have value or reference semantics when assigned to a second variable or passed to a function?
- D-071: Should absence of a value be `null` or `Option[T]`? Cross-reference: the ticket-005 decision on `null` as a primitive (range D-010 to D-029).
- D-073: Which name wins inside a method when a parameter or local has the same name as a field?
- D-074: Should the expression-bodied method form `fn f() T => expr` from the reference be accepted?
- D-075: Can a method call a sibling method by its bare name, without `self:`?
- D-076: Why is `instance:field[index]` a parse error?
- D-078: What happens when a struct literal omits a field that is read later?
