# Pools and handles

Status: tree-walk 0/0 | VM 0/0 | C 0/0   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `pool`.
A `Pool[T]` is a built-in value-semantic collection that stores elements of type `T` and hands out typed, generational `Handle[T]` values. An empty pool is created with `Pool[T]` (no braces); there is no non-empty pool literal and no `Handle[T]` literal or constructor.

## Rules
- POOL-1: `Pool[T]` in expression position with no braces creates an empty pool of element type `T`, and there is no non-empty pool literal. [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-102-set-type-and-literal.md]
- POOL-2: In expression position, `Pool[...]` and `Handle[...]` are always parsed as the pool and handle forms rather than indexing a variable named `Pool` or `Handle`. Indexing such a variable is rejected with `PN2004` and the message `'<Name>[...]' always means the built-in <kind> type, so it can't index a variable named '<Name>'.`, where `<kind>` is `pool` or `handle` (help: `rename the variable`). [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-147-set-map-expression-forms.md]
- POOL-3: `Pool[T]` has value semantics with copy-on-write (CoW): copying a pool produces an independent pool in which existing handles remain valid in both copies, and mutating a pool requires a mutable place (`var`, `val mut`, a `mut` parameter, or a `mut` field reached through one). [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-177-value-semantics-cow-and-mut.md; spec/decisions/D-183-val-mut-and-module-state.md]
- POOL-4: `Handle[T]` is a plain immutable value identifying a slot index and generation of a `Pool[T]`, created only by `p:add(x)` and usable with `==`, `!=`, as a map key, and as a set element (with optional handles expressed via `Option[Handle[T]]`). [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-5: Two `Handle[T]` values compare equal under `==` if and only if both their slot index and generation are equal, and `!=` is its negation. [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-6: `str(h)` formats a `Handle[T]` as `Handle(<index>, <generation>)` (for example `Handle(0, 0)`). [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-7: Indexing a pool with a live handle `p[h]` reads the stored value of type `T`, and on a mutable pool place `p[h] = x`, `p[h]:field = x`, and `p[h] op= x` update the stored value in place. [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-8: A newly allocated slot starts at generation `0`, removing a live handle frees its slot and increments its generation so the old handle becomes stale, and subsequent `add` calls reuse the most recently freed slot first before appending a new slot. [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-9: After `p:clear()`, all live slots are freed with incremented generations, and subsequent `add` calls reuse slots in increasing index order (`0`, `1`, `2`, ...) skipping any retired slots before appending. [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-10: A slot whose generation would exceed the runtime's generation limit (implementation-defined, at least `2147483647`) is retired and never reused so a stale handle can never become live again. [src: spec/decisions/D-185-pool-and-handle.md] [no-case: exercising at least 2147483647 generations on a single slot is not feasible in a conformance test]
- POOL-11: Iterating over a pool visits live slots deterministically in increasing slot-index order (explicitly differing from the insertion order of maps in D-004 and sets in D-175 once slots are reused), walking the pool as it was when the loop began with deeply immutable loop variables. [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-177-value-semantics-cow-and-mut.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- POOL-12: Passing two `mut` pool-element arguments sharing a root pool (`duel(mut p[a], mut p[b])`) follows D-184: distinct handle expressions receive a run-time slot-address check unless proven different by a branch fact, while identical immutable handle symbols (`duel(mut p[h], mut p[h])`) are rejected at compile time with `PN3226`. [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-184-disjoint-mut-slots.md]
- POOL-13: `str(p)` of a pool has the form `Pool[T] { Handle(i, g) -> v, ... }`, where `T` is the concrete element type (for example `Pool[Weapon]`), each live slot is written as its handle `Handle(index, generation)` followed by `->` and `str(v)` of its value (strings in double quotes, as in `MAP-9`), the entries are in increasing slot-index order (`POOL-11`) separated by a comma and a space, and an empty pool prints as `Pool[T] { }`. [src: spec/decisions/D-186-print-forms-and-ir-prereqs.md; spec/decisions/D-185-pool-and-handle.md]
- POOL-14: `type(p)` of a pool is the string `Pool`, and `type(h)` of a handle is the string `Handle`. [src: spec/decisions/D-186-print-forms-and-ir-prereqs.md; spec/decisions/D-185-pool-and-handle.md]

## Errors
- POOL-E1: Reading or writing `p[h]` with a stale handle panics at run time (ERR-3, exit code 101 per ERR-5) with the message `This handle no longer points to a value in '<pool>'; it was removed.`, where `<pool>` is the pool's first identifier as written. [src: spec/decisions/D-185-pool-and-handle.md; ERR-3; ERR-5]
- POOL-E2: Indexing a `Pool[T]` with a value whose type is not `Handle[T]` (including an `int` or a `Handle[U]` for a different type `U`) is rejected by the checker. [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-E3: Writing `Handle[T]` in expression position is rejected by the checker with `PN3231` (`A 'Handle[T]' can't be created directly.`) because handles can only be created by `add` on a `Pool[T]`. [src: spec/decisions/D-185-pool-and-handle.md]
- POOL-E4: Mutating a pool reached through an immutable binding (such as `val p = Pool[T]` followed by `p:add(...)` or `p[h] = ...`) is rejected by the checker with `PN3220`. [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-177-value-semantics-cow-and-mut.md]
- POOL-E5: Declaring a user `struct`, `enum`, `union`, or `interface` named `Pool` or `Handle` is rejected by the checker with `PN3002`, and using `Pool` or `Handle` as a `@@Generic` type parameter name is rejected per `GEN-E4`. [src: spec/decisions/D-185-pool-and-handle.md]

## Examples
Conformance cases: `conformance/pools/`. Pool members (`len`, `length`, `add`, `remove`, `get`, `handles`, `clear`, `in`, `not in`) are specified in `spec/stdlib/pool.md`.

Caveat: handles are tied to the element type `T`, not to one pool instance. Passing a handle from another `Pool[T]` is not detected apart from what the generation check happens to catch.

## Open questions
- `str(p)`, `type(p)`, and `type(h)` are currently unspecified (similar to `SET-6` for sets).
- Thread safety for pools is out of scope and waits for the concurrency design (`D-185`).
