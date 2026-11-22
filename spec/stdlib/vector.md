# Vector methods

Status: pending regeneration (ticket 009)

Members are called on a vector with `:`, for example `v:len` for a property and `v:push(x)` for a method. Callbacks may take `(item)` or `(item, index)` and may use the implicit `it` (FN-16). Vector creation and indexing are in `spec/features/vectors.md`.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `len` / `length` | property | `int`, the number of elements |
| `cap` / `capacity` | property | `int`, the allocated capacity |
| `push(item)` / `add(item)` | `mut fn (T)` | `void` |
| `pop()` | `mut fn ()` | `Option[T]` (`Option::Some(last)` or `Option::None` when empty) |
| `swap(i, j)` | `mut fn (int, int)` | `void` |
| `each(fn)` | `fn (fn (T))` or `fn (fn (T, int))` | `void` |
| `map(fn)` | `fn (fn (T) -> R)` or `fn (fn (T, int) -> R)` | a new vector of the results |
| `filter(fn)` | `fn (fn (T) -> bool)` or `fn (fn (T, int) -> bool)` | a new vector of the matching elements |
| `find(fn)` | `fn (fn (T) -> bool)` or `fn (fn (T, int) -> bool)` | `Option[T]` (`Option::Some(first)` or `Option::None`) |
| `find_index(fn)` | `fn (fn (T) -> bool)` or `fn (fn (T, int) -> bool)` | `int`, the index of the first match, or `-1` |
| `any(fn)` | `fn (fn (T) -> bool)` or `fn (fn (T, int) -> bool)` | `bool` |
| `all(fn)` | `fn (fn (T) -> bool)` or `fn (fn (T, int) -> bool)` | `bool` |
| `count(fn)` | `fn (fn (T) -> bool)` or `fn (fn (T, int) -> bool)` | `int` |
| `join(separator)` | `fn (string)` | `string` |

## Rules
- VECLIB-1: `v:len` returns the number of elements of `v`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-2: `v:length` is an alias of `v:len`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-3: `v:cap` returns the allocated capacity of `v`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-4: `v:capacity` is an alias of `v:cap`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; DataStructureTests.TestVectorCapacityInitialization]
- VECLIB-5: `v:push(x)` requires `v` to be a mutable place and appends `x` to the end of `v`, modifying `v` in place. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md]
- VECLIB-6: `v:push(x)` and `v:add(x)` return `void` (matching `set:add`), so chaining such as `v:push(a):push(b)` is retired and rejected (VECLIB-E1). [src: spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md]
- VECLIB-7: `v:add(x)` is an alias of `v:push(x)`, requiring a mutable receiver place and returning `void`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md]
- VECLIB-8: `v:pop()` requires `v` to be a mutable place, removes the last element `x` of `v` and returns `Option::Some(x)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; spec/decisions/D-013-null-primitive-versus-option.md; spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md]
- VECLIB-9: `v:pop()` on an empty vector returns `Option::None`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; spec/decisions/D-013-null-primitive-versus-option.md]
- VECLIB-10: `v:each(fn)` calls `fn` with each element in index order. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; StdlibTests.TestStringSplit]
- VECLIB-11: A callback of `each` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-12: `v:map(fn)` returns a new vector of the results of `fn` applied to each element in index order, and leaves `v` unchanged. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-13: A callback of `map` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-14: `v:filter(fn)` returns a new vector of the elements for which `fn` is true, in their original order, and leaves `v` unchanged. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; StdlibTests.TestShorthandLambdaItResolution]
- VECLIB-15: A callback of `filter` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-16: `v:find(fn)` returns `Option::Some(x)` for the first element `x` for which `fn` is true. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; spec/decisions/D-013-null-primitive-versus-option.md]
- VECLIB-17: `v:find(fn)` returns `Option::None` when no element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"; spec/decisions/D-013-null-primitive-versus-option.md]
- VECLIB-18: `v:find_index(fn)` returns the zero-based index of the first element for which `fn` is true. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-19: `v:find_index(fn)` returns `-1` when no element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-20: `v:any(fn)` is true when at least one element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-21: `v:all(fn)` is true when every element satisfies `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-22: `v:count(fn)` returns the number of elements that satisfy `fn`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-23: The callbacks of `find`, `find_index`, `any`, `all` and `count` may take the index as a second parameter, `fn(item, index)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-24: The callback of `count` may be written with the implicit `it`, as in `v:count(it % 2 == 0)`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods"]
- VECLIB-25: `v:join(sep)` returns the elements converted to strings and joined with `sep`, and returns the empty string for an empty vector. [src: DataStructureTests.TestVectorJoinMethod]
- VECLIB-26: `v:swap(i, j)` requires `v` to be a mutable place, exchanges the elements at zero-based indexes `i` and `j`, and returns `void`. [src: spec/decisions/D-184-disjoint-mut-slots.md]
- VECLIB-27: `v:swap(i, i)` is a no-op when `i` is in range (`0 <= i < v:len`). [src: spec/decisions/D-184-disjoint-mut-slots.md]

## Errors
- VECLIB-E1: Chaining `v:push(a):push(b)` or using the result of `v:push(x)` or `v:add(x)` as a value is rejected by the checker because both return `void`. [src: spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md]
- VECLIB-E2: `v:swap(i, j)` bounds-checks both `i` and `j` first and panics at run time per VEC-E4 if either index is out of range, even when `i == j` (such as `v:swap(99, 99)`). [src: spec/decisions/D-184-disjoint-mut-slots.md]

## Examples
Conformance cases: `conformance/stdlib-vector/`.

## Open questions
None. (A line break before `[` ends the expression, so a statement may start with a vector literal; see `VEC-17` and `D-058`.)
