# Pool methods

Status: tree-walk 0/0 | VM 0/0 | C 0/0   (from conformance/STATUS.md)

Members are called on a `Pool[T]` with `:`, for example `p:len` for a property and `p:add(x)` for a method. Pool creation, handle rules, slot reuse, and stale-handle panics are in `spec/features/pools.md`. The API follows `spec/decisions/D-185-pool-and-handle.md`.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `len` / `length` | property | `int`, the number of live slots |
| `add(x)` | `mut fn (T)` | `Handle[T]` |
| `remove(h)` | `mut fn (Handle[T])` | `Option[T]` |
| `get(h)` | `fn (Handle[T])` | `Option[T]` |
| `handles()` | `fn ()` | `[]Handle[T]` |
| `clear()` | `mut fn ()` | `void` |
| `h in p` / `h not in p` | operators | `bool` |
| `p[h]` | index read | `T` (panics when `h` is stale) |
| `p[h] = x`, `p[h]:field = x`, `p[h] op= x` | index write | `T` (requires mutable place; panics when `h` is stale) |

## Rules
- POOLLIB-1: `p:len` returns the number of live slots in `p`. [src: spec/decisions/D-185-pool-and-handle.md]
- POOLLIB-2: `p:length` is an alias of `p:len`. [src: spec/decisions/D-185-pool-and-handle.md]
- POOLLIB-3: `p:add(x)` requires `p` to be a mutable place, stores `x` of type `T` in the pool (reusing the most recently freed slot first, or increasing index order after `clear()`, before appending), and returns its `Handle[T]`. [src: spec/decisions/D-185-pool-and-handle.md]
- POOLLIB-4: `p:remove(h)` requires `p` to be a mutable place; when `h` is live it frees the slot with an incremented generation and returns `Option::Some(value)`, and when `h` is stale it leaves `p` unchanged and returns `Option::None`. [src: spec/decisions/D-185-pool-and-handle.md]
- POOLLIB-5: `p:get(h)` returns `Option::Some(value)` when `h` is live in `p` and `Option::None` when `h` is stale. [src: spec/decisions/D-185-pool-and-handle.md]
- POOLLIB-6: `p:handles()` returns a `[]Handle[T]` of all live handles in `p` in increasing slot-index order. [src: spec/decisions/D-185-pool-and-handle.md]
- POOLLIB-7: `p:clear()` requires `p` to be a mutable place, frees every live slot with an incremented generation so every existing handle becomes stale, and returns `void`. [src: spec/decisions/D-185-pool-and-handle.md]
- POOLLIB-8: `h in p` is `true` exactly when handle `h` of type `Handle[T]` is live in `p`, `h not in p` is its negation, and as a read of `p` it follows FN-31 when combined with a `mut` call. [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-141-assignment-and-relational-precedence.md; spec/decisions/D-180-mut-parameters-calls-and-closures.md]

## Examples
Conformance cases: `conformance/stdlib-pool/`.

## Open questions
None. (`str(p)`, `type(p)`, and `type(h)` are specified by `POOL-13` and `POOL-14` in `spec/features/pools.md`.)
