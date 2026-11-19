# Modules and imports

Status: pending regeneration (ticket 009)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `mod`.
A source file used as a module starts with `module Name`. Another file loads it with `import Name`, reaching its members as `Name::member`, or with `from Name import a, b`, which brings the listed members into the current scope. Only declarations marked `pub` are visible to importers. Imported modules cannot define `main`.

## Rules
- MOD-1: A module file begins with `module Name` naming the module. [src: ParserTests.TestParseModuleAndImports; TranspilerCTests.TestTranspilerModuleCompilation]
- MOD-2: `import Name` makes the public members of module `Name` reachable as `Name::member`, including functions (`Name::f(x)`) and structs (`Name::S { ... }`). [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"; TranspilerCTests.TestTranspilerModuleCompilation; ParserTests.TestStaticMemberAccessStructInitialization]
- MOD-3: `from Name import a, b` brings the named public members into the current scope so they are used without the `Name::` prefix. [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"; TranspilerCTests.TestTranspilerFromImport]
- MOD-4: Commas between the names of a `from` import are optional. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- MOD-5: A module declaration marked `pub` is visible to the importing file; one that is not marked `pub` is private to its module. [src: TranspilerCTests.TestTranspilerModuleCompilation; CheckerTests.TestNamespacedEntityVisibility]
- MOD-6: A name that is not listed in a `from` import is not brought into the importing scope. [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"]

## Errors
- MOD-E1: Using a struct that the module does not mark `pub`, as in `Name::S { ... }`, is rejected with a message containing "Struct 'Name::S' is not public" (with the real names). [src: CheckerTests.TestNamespacedEntityVisibility]
- MOD-E2: Using an interface that the module does not mark `pub` as a type is rejected with a message containing "Interface 'Name::I' is not public" (with the real names). [src: CheckerTests.TestNamespacedEntityVisibility]
- MOD-E3: Naming a struct that the module does not define, as in `Name::Missing { }`, is rejected with a message containing "Struct 'Missing' is not defined in module 'Name'" (with the real names). [src: CheckerTests.TestNamespacedEntityNotDefined]
- MOD-E4: An imported module that defines a `main` function is rejected with a type check error. [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"; CheckerTests.TestModuleMainFunctionRestriction]
- MOD-E5: Combining `pub` and `@pub_testing` on one declaration, in either order, is rejected as a parse error. [src: ParserTests.TestParseModuleAndImports]

## Examples
Conformance cases: `conformance/modules/`. The helper modules they import are in `conformance/modules/modules/` and are not cases.

## Open questions
- D-062: Is a qualified call to a function that is not `pub` rejected on every engine? The C backend runs it.
- D-063: How does `import Name` find the module file (lowercased name under `modules/`), and is the lookup case-sensitive?
- D-064: What does `@pub_testing` mean beyond being mutually exclusive with `pub`? Proposal 012 is only `Proposed`.
