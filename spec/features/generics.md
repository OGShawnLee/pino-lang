# Generics

Status: tree-walk 26/26 | VM 14/26 | C 21/26   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `gen`.
Generics are resolved by the type checker at compile time (monomorphization). Type parameters are declared only by the `@@Generic[T, ...]` decorator placed before a `fn`, `struct`, `interface`, `union` or struct method. A parameter may carry a bound, `T is Interface`. Type arguments are written in brackets at a call or literal (`f[int](x)`, `Pair[string, int] { ... }`) and are inferred when omitted. Generic unions are specified in `spec/features/unions.md`.

## Rules
- GEN-1: A `struct` declares type parameters with the `@@Generic[Key, Value]` decorator before the `struct` keyword and may use them as field types. [src: CheckerTests.TestStructGenericsExplicitInstantiation; DataStructureTests.TestStructGenericsEvaluation; spec/decisions/D-072-two-generic-syntaxes.md]
- GEN-2: A generic struct is instantiated with explicit type arguments (`Pair[string, int] { key: "Hello", value: 42 }`), and its fields then have the substituted types. [src: CheckerTests.TestStructGenericsExplicitInstantiation; DataStructureTests.TestStructGenericsEvaluation]
- GEN-3: The type arguments of a generic struct literal can be omitted and are inferred from the field values. [src: CheckerTests.TestStructGenericsImplicitInstantiation]
- GEN-4: The `@@Generic[T, ...]` decorator before a `struct` is the sole syntax for declaring struct type parameters; inline brackets after the struct name are not accepted. [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestStructDecoratorSyntaxAndBounds; spec/decisions/D-072-two-generic-syntaxes.md]
- GEN-5: The `@@Generic[T, U]` decorator before a `fn` declares a generic function, and explicit type arguments are given at the call as `name[int, string](args)`. [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestFunctionGenericsExplicitCall; spec/decisions/D-072-two-generic-syntaxes.md]
- GEN-6: The type arguments of a generic function call can be omitted and are inferred from the argument types. [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestFunctionGenericsImplicitCall]
- GEN-7: A static or instance method of a struct can be generic with the `@@Generic[T]` decorator, and is called with explicit type arguments (`Reader::read[int](42)`, `printer:show[int](5)`) or with inferred ones. [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestStructMethodGenerics; CheckerTests.TestMethodGenericsBounds; spec/decisions/D-072-two-generic-syntaxes.md]
- GEN-8: A type parameter can be bounded with `is` (`@@Generic[Doc is DocumentShape]`) on a struct, union, interface, function or method, and the type argument must satisfy the interface structurally. [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestStructDecoratorSyntaxAndBounds; CheckerTests.TestFunctionGenericsBounds; CheckerTests.TestMethodGenericsBounds; spec/decisions/D-072-two-generic-syntaxes.md]
- GEN-9: Inside a generic body, the members declared by a type parameter's bound can be accessed on values of that type parameter. [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestGenericDeclarationConstrainedAccessPasses]
- GEN-10: An `interface` can be generic with the `@@Generic[T]` decorator, and a struct satisfies an instantiated generic interface such as `Repository[Person]` when its method signatures match after substituting the type arguments. [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestGenericInterfaces; spec/decisions/D-072-two-generic-syntaxes.md]
- GEN-11: A type parameter can be bounded by an instantiated generic interface (`@@Generic[Repo is Repository[Person]]`). [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestGenericConstraintUsingGenericInterface; spec/decisions/D-072-two-generic-syntaxes.md]

## Errors
- GEN-E1: A generic struct literal whose field values give conflicting types for one type parameter is rejected with a message containing "Inconsistent types for generic parameter". [src: CheckerTests.TestStructGenericsInconsistentInferenceThrows]
- GEN-E2: A field value that does not match the substituted field type is rejected with a message containing "Cannot assign type". [src: CheckerTests.TestStructGenericsIncompatibleInitializationTypes]
- GEN-E3: Using a field of a generic struct as a value of a different type than its substituted type is rejected with a message containing "expected type". [src: CheckerTests.TestStructGenericsIncompatibleTypesThrows]
- GEN-E4: A type parameter named like an existing type is rejected with a message containing "conflicts with an existing defined type name". [src: CheckerTests.TestGenericParameterNameConflictThrows]
- GEN-E5: A type argument that does not satisfy the bound of its type parameter, including a primitive type, is rejected with a message containing "does not satisfy generic constraint". [src: proposals/004-generics-evolution-roadmap.md; CheckerTests.TestStructDecoratorSyntaxAndBoundsInvalid; CheckerTests.TestStructDecoratorSyntaxAndBoundsPrimitiveInvalid; CheckerTests.TestFunctionGenericsBoundsInvalid; CheckerTests.TestMethodGenericsBoundsInvalid]
- GEN-E6: A struct whose method signatures do not match an instantiated generic interface is rejected when passed for it, with a message containing "expected type". [src: CheckerTests.TestGenericInterfacesInvalid]
- GEN-E7: A type argument that does not satisfy a bound given by a generic interface is rejected with a message containing "does not satisfy generic constraint". [src: CheckerTests.TestGenericConstraintUsingGenericInterfaceInvalid]
- GEN-E8: Accessing a member on a value whose type parameter has no bound is rejected with a message containing "does not have field or instance method". [src: CheckerTests.TestGenericDeclarationUnconstrainedAccessThrows]
- GEN-E9: A static method that uses a type parameter bound to the struct instance is rejected with a message containing "cannot use instance-bound generic parameter". [src: CheckerTests.TestStaticMethodUsingInstanceBoundGenericThrows]
- GEN-E10: Using the legacy `@generic[...]` spelling or inline type parameters on a `struct` or `union` declaration (`struct Pair[A, B]`, `union Option[T]`) is rejected as a parse error. [src: spec/decisions/D-072-two-generic-syntaxes.md]

## Examples
Conformance cases: `conformance/generics/`.

## Open questions
None.
