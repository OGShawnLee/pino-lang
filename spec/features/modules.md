# Modules and imports

Status: tree-walk 8/9 | VM 5/9 | C 8/9   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `mod`.
A source file used as a module starts with `module Name`. Another file loads it with `import Name`, reaching its members as `Name::member`, or with `from Name import a, b`, which brings the listed members into the current scope. Only top-level declarations marked `pub` are visible to importers, and a struct's fields are private to its module unless marked `pub` (STRUCT-20). `@@Testable` is a visibility marker that cannot be combined with `pub`. Imported modules cannot define `main`.

## Rules
- MOD-1: A module file begins with `module Name` naming the module. [src: ParserTests.TestParseModuleAndImports; TranspilerCTests.TestTranspilerModuleCompilation]
- MOD-2: `import Name` makes the public members of module `Name` reachable as `Name::member`, including functions (`Name::f(x)`) and structs (`Name::S { ... }`). [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"; TranspilerCTests.TestTranspilerModuleCompilation; ParserTests.TestStaticMemberAccessStructInitialization]
- MOD-3: `from Name import a, b` brings the named public members into the current scope so they are used without the `Name::` prefix. [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"; TranspilerCTests.TestTranspilerFromImport]
- MOD-4: Commas between the names of a `from` import are optional. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- MOD-5: A top-level declaration marked `pub` (`fn`, `struct`, `interface`, `enum`, `union`, or `val`, but never `var`) is importable by other modules via `Name::member` or `from Name import member`; one that is not marked `pub` is private to its module. [src: TranspilerCTests.TestTranspilerModuleCompilation; CheckerTests.TestNamespacedEntityVisibility; spec/decisions/D-163-visibility-and-testable.md]
- MOD-6: A name that is not listed in a `from` import is not brought into the importing scope. [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"]
- MOD-7: `import Name` and `from Name import ...` search the `modules/` directory of the importing file's directory and then each parent directory in turn; at each directory, they try `modules/<Name>.pino` first and then `modules/<name lowercased>.pino` before moving to the parent directory. [src: spec/decisions/D-063-module-file-lookup.md]
- MOD-8: A declaration marked `@@Testable` can be imported from a test file (a file named `*_test.pino` or a file inside a `test/` directory) and is private to its module for every other importing file. [src: spec/decisions/D-64-pub-testing-semantics.md; spec/decisions/D-163-visibility-and-testable.md]

## Errors
- MOD-E1: Using a struct that the module does not mark `pub`, as in `Name::S { ... }`, is rejected with a message containing "Struct 'Name::S' is not public" (with the real names). [src: CheckerTests.TestNamespacedEntityVisibility]
- MOD-E2: Using an interface that the module does not mark `pub` as a type is rejected with a message containing "Interface 'Name::I' is not public" (with the real names). [src: CheckerTests.TestNamespacedEntityVisibility]
- MOD-E3: Naming a struct that the module does not define, as in `Name::Missing { }`, is rejected with a message containing "Struct 'Missing' is not defined in module 'Name'" (with the real names). [src: CheckerTests.TestNamespacedEntityNotDefined]
- MOD-E4: An imported module that defines a `main` function is rejected with a type check error. [src: LANGUAGE_REFERENCE.md §6 "Modules and Imports"; CheckerTests.TestModuleMainFunctionRestriction]
- MOD-E5: Combining `pub` and `@@Testable` on one declaration, in either order, is rejected as a parse error. [src: ParserTests.TestParseModuleAndImports; spec/decisions/D-163-visibility-and-testable.md]
- MOD-E6: A qualified call `Name::f(...)` to a function that the module does not mark `pub` is rejected by the checker with a message containing "is private". [src: spec/decisions/D-62-private-function-access-across-modules.md]
- MOD-E7: `from Name import f` for a function that the module does not mark `pub` is rejected by the checker with a message containing "does not export". [src: spec/decisions/D-62-private-function-access-across-modules.md]
- MOD-E8: Importing a `@@Testable` declaration with `from Name import f` from a file that is not a test file is rejected by the checker with a message containing "does not export". [src: spec/decisions/D-64-pub-testing-semantics.md]
- MOD-E9: Marking a top-level `var` as `pub` (`pub var`) is rejected by the checker with a message suggesting `pub val` or a `pub fn`. [src: spec/decisions/D-163-visibility-and-testable.md]

## Examples
Conformance cases: `conformance/modules/`. The helper modules they import are in `conformance/modules/modules/` and are not cases.

## Open questions
None.
