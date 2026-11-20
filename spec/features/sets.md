# Sets

Status: tree-walk 0/0 | VM 0/0 | C 0/0   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `set`.
A set holds unique elements of a single type. It is created with `set[T] { elem1, elem2, ... }`, or `set[T]` with no braces for an empty set. A bare `{ 1, 2, 3 }` is not a set literal.

## Rules
- SET-1: `set[T] { e1, e2, ... }` creates a set of element type `T` containing the given elements. [src: spec/decisions/D-102-set-type-and-literal.md]
- SET-2: `set[T]` with no braces creates an empty set of element type `T`. [src: spec/decisions/D-102-set-type-and-literal.md]
- SET-3: The elements of a set literal may be separated by whitespace or newlines instead of commas, with no trailing comma. [src: spec/decisions/D-102-set-type-and-literal.md; spec/decisions/D-108-trailing-commas-disallowed.md]

- SET-4: In expression position, `set[...]` is always parsed as a set form rather than indexing a variable named `set`, while member calls such as `xs:set(...)` remain unaffected. [src: spec/decisions/D-147-set-map-expression-forms.md]

## Errors
- SET-E1: A bare brace literal `{ 1, 2, 3 }` without `set[T]` is rejected as a parse error. [src: spec/decisions/D-102-set-type-and-literal.md]

- SET-E2: Attempting to index a variable named `set` in expression position (such as `set[0]`) is rejected as a parse error because `set[...]` is always a set form there. [src: spec/decisions/D-147-set-map-expression-forms.md]

## Examples
Conformance cases: `conformance/sets/`.

## Open questions
None.
