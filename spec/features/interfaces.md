# Interfaces

Status: pending regeneration (ticket 009)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `iface`.
An `interface` lists method signatures (`fn name(params) type`, no body) and may also list properties (`name type`). There is no `implements` clause: a struct satisfies an interface structurally, and an interface name is used as a type for parameters, fields and collection element types.

## Rules
- IFACE-1: An `interface` declares method signatures (`fn name(params) returnType`) without bodies. [src: LANGUAGE_REFERENCE.md §7 "Structural Interfaces (\"Duck Typing\")"]
- IFACE-2: A struct satisfies an interface implicitly, without any `implements` keyword, when it declares the methods the interface specifies. [src: LANGUAGE_REFERENCE.md §7 "Structural Interfaces (\"Duck Typing\")"; CheckerTests.TestTypeCheckerValidInterface]
- IFACE-3: A value of a satisfying struct type can be passed where the interface type is expected, and calling an interface method on it runs that struct's implementation. [src: LANGUAGE_REFERENCE.md §7 "Structural Interfaces (\"Duck Typing\")"; CheckerTests.TestTypeCheckerValidInterface]
- IFACE-4: An interface may declare properties (`name type`); a struct satisfies them by declaring a field with the same name and type. [src: CheckerTests.TestInterfacePropertiesCompatibilityPasses]
- IFACE-5: An interface type may be used as the type of a struct field or as the value type of a map, and such a field or entry accepts, and may be reassigned to, any satisfying struct. [src: CheckerTests.TestMapInterfaceCompatibilityPasses; DataStructureTests.TestStructStatePattern]

## Errors
- IFACE-E1: Passing a struct that lacks a method required by the interface is rejected with a message containing "expected type" and the interface name. [src: CheckerTests.TestTypeCheckerInvalidInterfaceThrows]
- IFACE-E2: Passing a struct that lacks a property required by the interface is rejected with a message containing "expected type" and the interface name. [src: CheckerTests.TestInterfacePropertiesMissingFieldThrows]
- IFACE-E3: Passing a struct whose field has a different type than the interface property is rejected with a message containing "expected type" and the interface name. [src: CheckerTests.TestInterfacePropertiesTypeMismatchThrows]
- IFACE-E4: Reading a member that the interface does not declare, through a value of the interface type, is rejected with a message containing "does not have property or method". [src: CheckerTests.TestAccessOnMissingFieldThrows]
- IFACE-E5: Calling a method that the interface does not declare, through a value of the interface type, is rejected with a message containing "does not have method or callable field". [src: CheckerTests.TestAccessOnMissingMethodThrows]

## Examples
Conformance cases: `conformance/interfaces/`. Generic interfaces are specified in `spec/features/generics.md`.

## Open questions
- D-074: Should the expression-bodied method form `fn make_sound() string => "Woof"`, used by the reference's own interface example, be accepted?
- D-083: Must a method signature (parameter and return types) match exactly for a struct to satisfy a non-generic interface?
