# Set methods

Status: pending regeneration (ticket 009)

Members are called on a set with `:`, for example `s:len` for a property and `s:add(x)` for a method. Set creation is in `spec/features/sets.md`. The API follows the owner's choice recorded in `spec/decisions/D-175-set-library-api.md`.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `len` / `length` | property | `int`, the number of elements |
| `add(element)` | `fn (T)` | `void` |
| `remove(element)` | `fn (T)` | `void` |
| `any(fn)` | `fn (fn (T) -> bool)` | `bool` |
| `all(fn)` | `fn (fn (T) -> bool)` | `bool` |
| `each(fn)` | `fn (fn (T))` | `void` |
| `x in s` / `x not in s` | operators | `bool` |

## Rules
- SETLIB-1: `s:len` returns the number of elements of `s`. [src: spec/decisions/D-175-set-library-api.md]
- SETLIB-2: `s:length` is an alias of `s:len`. [src: spec/decisions/D-175-set-library-api.md]
- SETLIB-3: `s:add(x)` adds the element `x` of type `T` to `s` and returns `void`; adding an element that is already in `s` leaves `s` unchanged, because a set holds unique elements (SET-1). [src: spec/decisions/D-175-set-library-api.md]
- SETLIB-4: `s:remove(x)` removes the element `x` from `s` and returns `void`. [src: spec/decisions/D-175-set-library-api.md]
- SETLIB-5: `s:any(f)` is true when `f` returns true for at least one element of `s` and false otherwise, where `f` takes one parameter of the element type. [src: spec/decisions/D-175-set-library-api.md]
- SETLIB-6: `s:all(f)` is true when `f` returns true for every element of `s` and false otherwise, where `f` takes one parameter of the element type. [src: spec/decisions/D-175-set-library-api.md]
- SETLIB-7: `s:each(f)` calls `f`, which takes one parameter of the element type, once with every element of `s` and returns `void`. [src: spec/decisions/D-175-set-library-api.md]
- SETLIB-8: `x in s` is true exactly when `x` is an element of `s`, and `x not in s` is its negation. [src: spec/decisions/D-175-set-library-api.md; spec/decisions/D-141-assignment-and-relational-precedence.md]

## Examples
Conformance cases: `conformance/stdlib-set/`.

## Open questions
None. (What `remove` does for an absent element, and the order in which `any`, `all` and `each` visit elements, are not specified; see D-175.)
