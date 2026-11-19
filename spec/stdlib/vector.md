# Vector methods

Status: pending regeneration (ticket 009)

Members are called on a vector with `:`, for example `v:len` for a property and `v:push(x)` for a method. Callbacks may take `(item)` or `(item, index)` and may use the implicit `it` (FN-16). Vector creation and indexing are in `spec/features/vectors.md`.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `len` / `length` | property | `int`, the number of elements |
| `cap` / `capacity` | property | `int`, the allocated capacity |
| `push(item)` / `add(item)` | `fn(T)` | the vector itself (mutated) |
| `pop()` | `fn()` | the removed last element, or `null` when empty |
| `each(fn)` | `fn(fn(T))` or `fn(fn(T, int))` | `null` |
| `map(fn)` | `fn(fn(T) R)` or `fn(fn(T, int) R)` | a new vector of the results |
| `filter(fn)` | `fn(fn(T) bool)` or `fn(fn(T, int) bool)` | a new vector of the matching elements |
| `find(fn)` | `fn(fn(T) bool)` or `fn(fn(T, int) bool)` | the first matching element, or `null` |
| `find_index(fn)` | `fn(fn(T) bool)` or `fn(fn(T, int) bool)` | `int`, the index of the first match, or `-1` |
| `any(fn)` | `fn(fn(T) bool)` or `fn(fn(T, int) bool)` | `bool` |
| `all(fn)` | `fn(fn(T) bool)` or `fn(fn(T, int) bool)` | `bool` |
| `count(fn)` | `fn(fn(T) bool)` or `fn(fn(T, int) bool)` | `int` |
| `join(separator)` | `fn(string)` | `string` |

## Rules
- VECLIB-1: `v:len` returns the number of elements of `v`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-2: `v:length` is an alias of `v:len`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-3: `v:cap` returns the allocated capacity of `v`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-4: `v:capacity` is an alias of `v:cap`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; DataStructureTests.TestVectorCapacityInitialization]
- VECLIB-5: `v:push(x)` appends `x` to the end of `v`, modifying `v`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-6: `v:push(x)` returns the modified vector, so calls can be chained as `v:push(a):push(b)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-7: `v:add(x)` is an alias of `v:push(x)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-8: `v:pop()` removes the last element of `v` and returns it. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-9: `v:pop()` on an empty vector returns `null`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-10: `v:each(fn)` calls `fn` with each element in index order. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; StdlibTests.TestStringSplit]
- VECLIB-11: A callback of `each` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-12: `v:map(fn)` returns a new vector of the results of `fn` applied to each element in index order, and leaves `v` unchanged. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-13: A callback of `map` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-14: `v:filter(fn)` returns a new vector of the elements for which `fn` is true, in their original order, and leaves `v` unchanged. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; StdlibTests.TestShorthandLambdaItResolution]
- VECLIB-15: A callback of `filter` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-16: `v:find(fn)` returns the first element for which `fn` is true. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-17: `v:find(fn)` returns `null` when no element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-18: `v:find_index(fn)` returns the zero-based index of the first element for which `fn` is true. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-19: `v:find_index(fn)` returns `-1` when no element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-20: `v:any(fn)` is true when at least one element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-21: `v:all(fn)` is true when every element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-22: `v:count(fn)` returns the number of elements that satisfy `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-23: The callbacks of `find`, `find_index`, `any`, `all` and `count` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-24: The callback of `count` may be written with the implicit `it`, as in `v:count(it % 2 == 0)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-25: `v:join(sep)` returns the elements converted to strings and joined with `sep`, and returns the empty string for an empty vector. [src: DataStructureTests.TestVectorJoinMethod]

## Examples
Conformance cases: `conformance/stdlib-vector/`.

## Open questions
- D-065: `join` is asserted by a test but missing from the reference's vector method list.
- D-059: Reading an element out of range is unspecified.
- D-058: A statement that starts with a vector literal after an expression statement is parsed as an index access.
