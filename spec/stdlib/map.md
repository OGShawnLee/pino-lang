# Map methods

Status: pending regeneration (ticket 009)

Members are called on a map with `:`, for example `m:len` for a property and `m:remove(k)` for a method. Map creation, lookup and assignment are in `spec/features/maps.md`.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `len` / `length` | property | `int`, the number of entries |
| `keys()` | `fn ()` | `[]K`, a new vector of the keys |
| `values()` | `fn ()` | `[]V`, a new vector of the values, in the order of their keys |
| `remove(key)` | `fn (K)` | the removed value, or `null` when the key is absent |

## Rules
- MAPLIB-1: `m:len` returns the number of entries of `m`. [src: LANGUAGE_REFERENCE.md §10 "Map Methods"; StdlibTests.TestMapLength]
- MAPLIB-2: `m:length` is an alias of `m:len`. [src: LANGUAGE_REFERENCE.md §10 "Map Methods"; StdlibTests.TestMapLength]
- MAPLIB-3: `m:keys()` returns a new vector containing every key of `m`. [src: LANGUAGE_REFERENCE.md §10 "Map Methods"; StdlibTests.TestMapKeys]
- MAPLIB-4: `m:values()` returns a new vector containing every value of `m`. [src: LANGUAGE_REFERENCE.md §10 "Map Methods"; DataStructureTests.TestMapEvaluatorRuntimeBehavior]
- MAPLIB-5: The vector returned by `m:values()` is in the order of the keys returned by `m:keys()`, so element `i` of one belongs to element `i` of the other. [src: LANGUAGE_REFERENCE.md §10 "Map Methods"]
- MAPLIB-6: `m:remove(k)` removes the entry for `k` from `m` and returns the value it had. [src: LANGUAGE_REFERENCE.md §10 "Map Methods"; StdlibTests.TestMapRemove]
- MAPLIB-7: `m:remove(k)` returns `null` and leaves `m` unchanged when `k` is not in `m`. [src: LANGUAGE_REFERENCE.md §10 "Map Methods"]

## Examples
Conformance cases: `conformance/stdlib-map/`.

## Open questions
- D-004: The iteration order of a map is unspecified, so the order of `keys()` and `values()` is too; only their alignment is specified (MAPLIB-5).
