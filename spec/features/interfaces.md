# Interfaces

Status: tree-walk 10/10 | VM 5/10 | C 5/10   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `iface`.
An `interface` lists method signatures (`[mut] fn name(params) type`, no body) and may also list properties (`[mut] name type`). There is no `implements` clause: a struct satisfies an interface structurally, and an interface name is used as a type for parameters, fields and collection element types.

## Rules
- IFACE-1: An `interface` declares method signatures (`fn name(params) returnType` or `mut fn name(params) returnType`) without bodies. [src: LANGUAGE_REFERENCE.md §7 "Structural Interfaces (\"Duck Typing\")"; spec/decisions/D-179-struct-field-and-method-mutability.md]
- IFACE-2: A struct satisfies an interface implicitly, without any `implements` keyword, when it declares the methods the interface specifies. [src: LANGUAGE_REFERENCE.md §7 "Structural Interfaces (\"Duck Typing\")"; CheckerTests.TestTypeCheckerValidInterface]
- IFACE-3: A value of a satisfying struct type can be passed where the interface type is expected, and calling an interface method on it runs that struct's implementation. [src: LANGUAGE_REFERENCE.md §7 "Structural Interfaces (\"Duck Typing\")"; CheckerTests.TestTypeCheckerValidInterface]
- IFACE-4: An interface may declare properties (`name type` or `mut name type`); a struct satisfies a read-only interface property with either a read-only or `mut` field of the same name and type, and satisfies a `mut` interface property only with a `mut` field of the same name and type. [src: CheckerTests.TestInterfacePropertiesCompatibilityPasses; spec/decisions/D-179-struct-field-and-method-mutability.md]
- IFACE-5: An interface type may be used as the type of a struct field or as the value type of a map, and such a field or entry accepts, and when mutable may be reassigned to, any satisfying struct; mutating through a `mut` property or calling a `mut fn` on an interface value requires a mutable receiver place. [src: CheckerTests.TestMapInterfaceCompatibilityPasses; DataStructureTests.TestStructStatePattern; spec/decisions/D-179-struct-field-and-method-mutability.md]

- IFACE-6: A struct satisfies a non-generic interface only when each method's number of parameters, parameter types, per-parameter `mut` qualifiers, and return type match the interface's signature (parameter names are ignored), and its mutability matches safely: a non-`mut` interface method requires a non-`mut` `fn` implementation, while a `mut fn` interface method accepts either a `mut fn` or a plain `fn`. [src: spec/decisions/D-083-interface-signature-matching.md; spec/decisions/D-179-struct-field-and-method-mutability.md]

## Errors
- IFACE-E1: Passing a struct that lacks a method required by the interface is rejected with a message containing "expected type" and the interface name. [src: CheckerTests.TestTypeCheckerInvalidInterfaceThrows]
- IFACE-E2: Passing a struct that lacks a property required by the interface is rejected with a message containing "expected type" and the interface name. [src: CheckerTests.TestInterfacePropertiesMissingFieldThrows]
- IFACE-E3: Passing a struct whose field has a different type than the interface property is rejected with a message containing "expected type" and the interface name. [src: CheckerTests.TestInterfacePropertiesTypeMismatchThrows]
- IFACE-E4: Reading a member that the interface does not declare, through a value of the interface type, is rejected with a message containing "does not have property or method". [src: CheckerTests.TestAccessOnMissingFieldThrows]
- IFACE-E5: Calling a method that the interface does not declare, through a value of the interface type, is rejected with a message containing "does not have method or callable field". [src: CheckerTests.TestAccessOnMissingMethodThrows]

- IFACE-E6: Passing a struct whose method has a different parameter type or return type than the interface's signature is rejected by the checker with a message containing "expected type" and the interface name. [src: spec/decisions/D-083-interface-signature-matching.md]
- IFACE-E7: Passing a struct that fails an interface's `mut fn` or `mut` property requirement (such as implementing a non-`mut` interface method with a `mut fn` or implementing a `mut` interface property with a read-only field), or mutating a non-`mut` interface property, is rejected by the checker. [src: spec/decisions/D-179-struct-field-and-method-mutability.md]

## Examples
Conformance cases: `conformance/interfaces/`. Generic interfaces are specified in `spec/features/generics.md`.

## Open questions
None.
