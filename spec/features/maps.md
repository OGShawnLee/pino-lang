# Maps

Status: tree-walk 14/14 | VM 4/14 | C 11/14   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `map`.
A map associates unique keys with values. It is created with `map[KeyType, ValueType] { key -> value, ... }`, or `map[KeyType, ValueType]` with no braces for an empty map, read and written with `m[key]`, and tested for a key with `in`. The methods of a map are in `spec/stdlib/map.md`.

## Rules
- MAP-1: `map[K, V] { k1 -> v1, k2 -> v2 }` creates a map from keys of type `K` to values of type `V`. [src: LANGUAGE_REFERENCE.md §4 "Maps (Dictionaries)"; spec/decisions/D-100-arrow-token.md]
- MAP-2: The entries of a map literal may be separated by whitespace or newlines instead of commas. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- MAP-3: `map[K, V]` with no braces creates an empty map. [src: ParserTests.TestMapParserEdgeCases; spec/decisions/D-102-set-type-and-literal.md]
- MAP-4: `m[key]` reads the value associated with `key`. [src: LANGUAGE_REFERENCE.md §4 "Maps (Dictionaries)"]
- MAP-5: `m[key] = value` requires `m` to be a mutable place and inserts the entry when `key` is absent or replaces the value when `key` is present. [src: LANGUAGE_REFERENCE.md §4 "Maps (Dictionaries)"; DataStructureTests.TestMapEvaluatorRuntimeBehavior; spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md]
- MAP-6: `m[key] += n` requires `m` to be a mutable place and adds `n` to the value stored under `key`, evaluating the key expression once per VAR-4. [src: DataStructureTests.TestMapEvaluatorRuntimeBehavior; StdlibTests.TestMapCompoundAssignment; spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- MAP-7: `k in m` is true when `m` has an entry for `k`, and `k not in m` is its negation. [src: DataStructureTests.TestMapAndVectorInNotInOperators; StdlibTests.TestInOperatorMap]
- MAP-8: The value type of a map may be a vector or another composite type, as in `map[string, []int]`. [src: ParserTests.TestMapParserEdgeCases]
- MAP-9: `str(m)` of a map has the form `map[K, V] { key -> value, ... }`, where `K` and `V` are the concrete key and value types, each entry is written with `->` (as in a map literal, `D-100`), strings are shown in double quotes, entries are separated by a comma and a space in insertion order (assigning to an existing key keeps its position; removing a key and inserting it again puts it at the end), and an empty map prints as `map[K, V] { }`. [src: spec/decisions/D-186-print-forms-and-ir-prereqs.md; spec/decisions/D-004-map-iteration.md; spec/decisions/D-100-arrow-token.md]
- MAP-10: `type(m)` of a map is the string `map`. [src: StdlibTests.TestMapDeclarationAndAccess]

- MAP-11: In expression position, `map[...]` is always parsed as a map form (including brace-less empty `map[K, V]`) rather than indexing a variable named `map`, while member calls such as `xs:map(f)` remain unaffected. [src: spec/decisions/D-147-set-map-expression-forms.md]

- MAP-12: A map literal `map[K, float] { ... }` whose values mix `int` and `float` has the value type `float`, and the `int` values widen to `float`. [src: spec/decisions/D-168-literal-widening.md]
- MAP-13: Maps have value semantics with copy-on-write (CoW): assignment and non-`mut` argument passing produce logically independent maps, and mutating a caller's map requires a `mut` parameter and call-site `mut`. [src: spec/decisions/D-177-value-semantics-cow-and-mut.md]

## Errors
- MAP-E1: Reading a key that is not in the map with `m[key]` fails at run time; the engines share the wording "not found in map", which the conformance case checks. [src: DataStructureTests.TestMapEvaluatorRuntimeBehavior]
- MAP-E2: A key in a map literal whose type is not the declared key type is rejected before execution with a type check error. [src: CheckerTests.TestMapTypeCheckerSemanticValidation]
- MAP-E3: A value in a map literal whose type is not the declared value type is rejected before execution with a type check error. [src: CheckerTests.TestMapTypeCheckerSemanticValidation]
- MAP-E4: A map type with only one type argument, or a map literal entry without `-> value`, is rejected as a parse error. [src: ParserTests.TestMapParserEdgeCases; spec/decisions/D-100-arrow-token.md]

## Examples
Conformance cases: `conformance/maps/`.

## Open questions
None.
