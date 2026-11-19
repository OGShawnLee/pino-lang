# Maps

Status: tree-walk 14/14 | VM 4/14 | C 11/14   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `map`.
A map associates unique keys with values. It is created with `map[KeyType, ValueType] { key: value, ... }`, read and written with `m[key]`, and tested for a key with `in`. The methods of a map are in `spec/stdlib/map.md`.

## Rules
- MAP-1: `map[K, V] { k1: v1, k2: v2 }` creates a map from keys of type `K` to values of type `V`. [src: LANGUAGE_REFERENCE.md §4 "Maps (Dictionaries)"]
- MAP-2: The entries of a map literal may be separated by whitespace or newlines instead of commas. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- MAP-3: `map[K, V] {}` creates an empty map. [src: ParserTests.TestMapParserEdgeCases]
- MAP-4: `m[key]` reads the value associated with `key`. [src: LANGUAGE_REFERENCE.md §4 "Maps (Dictionaries)"]
- MAP-5: `m[key] = value` inserts the entry when `key` is absent and replaces the value when `key` is present. [src: LANGUAGE_REFERENCE.md §4 "Maps (Dictionaries)"; DataStructureTests.TestMapEvaluatorRuntimeBehavior]
- MAP-6: `m[key] += n` adds `n` to the value stored under `key`. [src: DataStructureTests.TestMapEvaluatorRuntimeBehavior; StdlibTests.TestMapCompoundAssignment]
- MAP-7: `k in m` is true when `m` has an entry for `k`, and `k not in m` is its negation. [src: DataStructureTests.TestMapAndVectorInNotInOperators; StdlibTests.TestInOperatorMap]
- MAP-8: The value type of a map may be a vector or another composite type, as in `map[string, []int]`. [src: ParserTests.TestMapParserEdgeCases]
- MAP-9: `str(m)` of a map with one entry has the form `map[K, V] { key: value }` with string keys and values shown in double quotes; with several entries the entries are separated by a comma and a space in an unspecified order. [src: StdlibTests.TestMapFormatting]
- MAP-10: `type(m)` of a map is the string `map`. [src: StdlibTests.TestMapDeclarationAndAccess]

## Errors
- MAP-E1: Reading a key that is not in the map with `m[key]` fails at run time; the engines share the wording "not found in map", which the conformance case checks. [src: DataStructureTests.TestMapEvaluatorRuntimeBehavior]
- MAP-E2: A key in a map literal whose type is not the declared key type is rejected before execution with a type check error. [src: CheckerTests.TestMapTypeCheckerSemanticValidation]
- MAP-E3: A value in a map literal whose type is not the declared value type is rejected before execution with a type check error. [src: CheckerTests.TestMapTypeCheckerSemanticValidation]
- MAP-E4: A map type with only one type argument, or a map literal entry without `: value`, is rejected as a parse error. [src: ParserTests.TestMapParserEdgeCases]

## Examples
Conformance cases: `conformance/maps/`.

## Open questions
- D-004: Iteration order of maps, which also leaves the order of the entries in `str(m)` unspecified (MAP-9).
- D-061: The reference says keys cannot be null; no engine can exercise it, and the test that claims to is passing for another reason.
