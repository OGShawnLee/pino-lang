# D-185: Built-in `Pool[T]` and `Handle[T]` types

Status: accepted
Raised by: owner review (2026-11-21)
Feature: spec/features/pools.md, spec/stdlib/pool.md, spec/features/equality.md, spec/features/loops.md, spec/features/variables.md

## Question
Under value semantics with copy-on-write (`AD-003`, `D-177`), storing a struct stores a copy, so linked data structures use IDs. Using a plain `[]T` with raw `int` indexes allows mixing up IDs of different kinds, shifts later indexes on removal, and lets stale indexes silently point at unrelated elements. Should Pino provide a built-in generational pool (`Pool[T]`) and typed handle (`Handle[T]`), and what are their syntax, typing, iteration, stale-handle, equality, and exclusivity semantics?

## Evidence
- Value semantics (`AD-003`, `D-177`): structs and collections have value semantics with copy-on-write and deep `val` immutability; `.tickets/043-mut-and-deep-immutability-design/design.md` ("Owner answers") explicitly noted `Pool[T]` with generational IDs as the standard linked-data tool for games and services.
- Existing collection precedents: `D-102` (brace-less empty collection forms `[]T`, `set[T]`, `map[K, V]`), `D-147` (`set[...]` / `map[...]` always parsed as collection forms in expression position), `D-175` (set stdlib API), `D-004` / `D-120` (loop iteration order and single-binding value rule), `D-182` (deeply immutable loop variables), `D-183` (`val mut` mutable-interior rule `VAR-E13`), `D-184` (disjoint `mut` slots and run-time exclusivity checks), `ERR-3` / `ERR-5` (panics exit with code 101), `TYPE-E2` (`void` forbidden as a collection element or generic argument), and `GEN-E4` (generic parameter names cannot shadow defined type names).

## Options
1. **Built-in compiler-known `Pool[T]` and `Handle[T]` types:** reserved built-in type names (not prelude unions), `Pool[T]` brace-less empty expression form, no `Handle[T]` literal/constructor, generational slots with LIFO reuse and retirement at generation limit, deterministic slot-index iteration order, checked (`in`, `get`, `remove`) and panicking (`p[h]`) stale-handle forms, `EQ-14` structural pool equality, `VAR-E13` classification, and `D-184` disjoint-slot integration.
2. **User-level library wrapper over `[]T`:** leaves handle construction and pool layout to user code without compiler-enforced reserved names or `D-184` / `MemoryFacts` awareness.

## Decision
Option 1, decided by the owner on 2026-11-21:

### 1. Built-in types and reserved names
- `Pool[T]` and `Handle[T]` are built-in compiler-known types (like `map[K, V]` and `set[T]`), not prelude source declarations.
- The type names `Pool` and `Handle` are reserved for types: declaring a user `struct`, `enum`, `union`, or `interface` named `Pool` or `Handle` is rejected with `PN3002` (`DuplicateDeclaration`) using the message `'Pool' is a built-in type and can't be declared again.` (or `'Handle'`) and help `choose another name`.
- `Pool` and `Handle` count as existing defined types for `GEN-E4`, so `@@Generic[Pool]` and `@@Generic[Handle]` are rejected.
- Lowercase local variables, functions, and fields named `pool` or `handle` are unaffected.

### 2. Syntax and type arguments
- In type position, `Pool[T]` and `Handle[T]` parse their type argument with the full type grammar (so `Pool[Weapon]`, `Pool[[]int]`, `Pool[Option[int]]`, `Pool[map[string, int]]`, and `Pool[fn (int) -> int]` are valid).
- In expression position, `Pool[T]` with no braces creates an empty pool (`D-102`). There is no non-empty pool literal.
- Following `D-147` (`SET-4`, `SET-E2`), `Pool[...]` and `Handle[...]` on the same line in primary expression position are always parsed as these forms, so indexing a variable named `Pool` or `Handle` is a parse error. A bare `Pool` or `Handle` in expression position with no `[` is an ordinary undeclared-identifier error.
- `Pool[void]` and `Handle[void]` are rejected per `TYPE-E2`. A bare `Pool` or `Handle` in type position, or the wrong number of type arguments (`Pool[int, string]`), is rejected like a wrong-arity generic type.

### 3. `Pool[T]` and `Handle[T]` semantics
- **`Pool[T]`** has value semantics with copy-on-write (`D-177`). Copying a pool produces an independent pool in which existing handles remain valid in both copies. Mutating a pool requires a mutable place (`var`, `val mut`, a `mut` parameter, or a `mut` field reached through one).
- **`Handle[T]`** identifies one slot of a `Pool[T]` by a slot index and a generation:
  - It is a plain immutable scalar value (pointer-free under `AD-002`, so structs holding handles remain pointer-free).
  - Under `VAR-E13` (`D-183`), `Pool[T]` has mutable contents (`val mut p = Pool[T]` is allowed) while `Handle[T]` has no mutable contents (`val mut h` is rejected with `PN3230`).
  - Handles support `==` and `!=` (equal iff both index and generation are equal) and can be used as map keys and set elements.
  - `str(h)` formats a handle as `Handle(<index>, <generation>)`, for example `Handle(0, 0)`.
  - `Handle[T]` has no literal and no constructor; only `p:add(x)` creates one. Writing `Handle[T]` in expression position is rejected with `PN3231` (`A 'Handle[Weapon]' can't be created directly.`, help `get one from 'add' on a 'Pool[Weapon]'`).
  - Passing or using a `Handle[A]` where a `Handle[B]` is expected is a type error. Optional handles use `Option[Handle[T]]`.
- `str(p)`, `type(p)`, and `type(h)` are unspecified.

### 4. Members, slots, generations, and stale handles
- Members (`spec/stdlib/pool.md`): `len` / `length` (`int`), `add(x) -> Handle[T]` (`mut fn`), `remove(h) -> Option[T]` (`mut fn`), `get(h) -> Option[T]`, `handles() -> []Handle[T]`, `clear() -> void` (`mut fn`), `h in p` / `h not in p` (`bool`, left operand `Handle[T]`, reading `p` per `FN-31`), and index read/write `p[h]`, `p[h] = x`, `p[h]:field = x`, `p[h] op= x`.
- A new slot starts at generation `0`. `remove(h)` on a live handle returns `Option::Some(value)`, frees the slot, and increments its generation; on a stale handle it returns `Option::None` and leaves the pool unchanged. `clear()` frees every live slot with an incremented generation so all existing handles become stale.
- `add(x)` reuses the most recently freed slot first (LIFO) and appends a new slot when none is free. After `clear()`, subsequent `add` calls reuse slots in increasing index order (`0`, `1`, `2`, ...), skipping any retired slots, before appending.
- **Generation limit:** a slot whose generation would exceed the runtime's limit (implementation-defined, at least `2147483647`) is retired and never reused so a stale handle can never become live again.
- **Stale-handle policy:** `h in p` and `p:get(h)` are the checked forms when absence is expected; `p[h]` read or write is for handles valid by construction and panics on a stale handle (`ERR-3`, exit code `101` per `ERR-5`) with the exact message:

  `This handle no longer points to a value in '<pool>'; it was removed.`

  where `<pool>` is the pool's first identifier as written (as in `D-184`).

### 5. Iteration, equality, and disjoint `mut` slots
- **Iteration (`LOOP-19`, `LOOP-20`):** Loops walk the pool as it was when the loop began (`D-177`) with deeply immutable loop variables (`D-182`). Live slots are visited deterministically in **increasing slot-index order**, which is explicitly the one Pino collection whose iteration order is not insertion order once slots are reused (contrasting with `D-004` maps and `D-175` sets). `for x in p` binds the value; `for h, x in p` binds the handle and the value (last name is always the element per `D-120`). To update elements in place, write `for h in p:handles() { p[h]:hp -= 1 }`.
- **Equality (`EQ-14`):** Two pools of the same element type are equal under `==` when they have the same live slots (index and generation) and equal values in each slot.
- **Disjoint slots (`D-184`):** `duel(mut p[a], mut p[b])` follows `D-184`. Handle index expressions are unknown unless both sides are the same immutable symbol (proven equal -> `PN3226`) or proven different by an `if a != b` branch fact (`FN-33`); otherwise the call receives a run-time exclusivity check.

## Consequences
- **IR obligations (for the future `-re` IR and backends):**
  - **Layout:** dense, contiguous slots with a generation per slot, plus a free list; removing an element does not shift later slots.
  - **Stale checks:** compare the handle's generation with the slot's generation on every `p[h]`, `get`, `in`, and `remove`.
  - **Disjointness:** the `D-184` run-time check compares resolved slot addresses.
  - **Allocation:** a pool is a natural arena whose whole storage can be freed at once.
- **Caveats:**
  - **Cross-pool handles:** handles are tied to the element type `T`, not to a specific pool instance. Passing a handle obtained from one `Pool[T]` to a different `Pool[T]` is not detected beyond what the slot index and generation check happens to catch.
  - **Thread safety:** out of scope; concurrent access to pools waits for the concurrency design.
- **Normative rules:** adds `spec/features/pools.md` (`POOL-1`–`POOL-12`, `POOL-E1`–`POOL-E5`), `spec/stdlib/pool.md` (`POOLLIB-1`–`POOLLIB-8`), `EQ-14`, `LOOP-19`, `LOOP-20`; rewords `VAR-E13`; extends `D-183`; updates `spec/grammar.ebnf` and `spec/SPEC.md`.

## Extension (2026-11-21, owner, ticket 057)

The built-in type names `Option` and `Result` are reserved exactly like `Pool` and `Handle`. A user `struct`, `enum`, `union` or `interface` with either name is rejected with `PN3002` (`'Option' is a built-in type and can't be declared again.`, help `choose another name`), and either name as a `@@Generic` parameter is rejected per `GEN-E4`. `?` and `or` are tied to the built-in types, so a lookalike would be confusing. Normative rule: `TYPE-E5`. Conformance cases that declared their own `union Option` now use `Maybe`.
