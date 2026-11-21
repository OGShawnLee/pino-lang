# D-177: Value semantics with copy-on-write (CoW), deep `val`, and `mut`

Status: accepted
Raised by: 043 (MQ-1, MQ-2, MQ-3, MQ-17, MQ-18)
Feature: spec/features/variables.md, spec/features/structs.md, spec/features/vectors.md, spec/features/maps.md, spec/features/sets.md, spec/features/unions.md, spec/features/tuples.md, spec/features/lexical.md

## Question
Should `val` be deeply immutable, how should aliasing work for structs and collections (`D-070` / `STRUCT-16`), what role does `mut` play alongside `val` and `var`, should collections/structs have `:clone()`, and how do tuple destructuring and temporary rvalue mutations behave?

## Evidence
- Spec & Decisions: `VAR-1` (`D-017`) previously prevented only direct reassignment of a `val`, leaving interior mutations (`v:push(3)`, `v[0] = 9`, `m["k"] = 1`, `p:x = 9`) legal; `D-070` (`STRUCT-16`) specified reference semantics for structs; `STRLIB-9` defines `:clone()` / `:to_owned()` only on `string`; `TUP-4`–`TUP-6` and `spec/grammar.ebnf:253` support `val @(a, b)` and `var @(a, b)`.
- Design `043` (§1–§2, §7, §12, §14): `0` identifier collisions with `mut` across all `627` `.pino` files; `17` conformance `.out` cases and `86` self-host sites mutate through a `val`; pure data types (`struct`, `[]T`, `map`, `set`, `union`, non-closure `interface`) become provably acyclic under value semantics with CoW, eliminating AD-002 cycle-collector overhead on the entire self-host AST.
- Prior art: Swift (`let`/`var`, CoW value types, `mutating`), V (value structs/arrays, `mut` modifier), Hylo (Mutable Value Semantics), Roc/Koka (Perceus RC / FBIP).

## Options
1. **MQ-1 (binding keywords):**
   - Option 1: Keep `val` (deeply immutable) and `var` (deeply mutable, bounded by struct field `mut`) for bindings; use `mut` strictly as a modifier on fields, methods, parameters, interfaces, and call-site arguments.
   - Option 2: Add `mut x = ...` as a third binding form.
   - Option 3: Replace `var` with `mut` everywhere.
2. **MQ-2 (depth of `val`):**
   - Option 1: Make `val` deeply (transitively) immutable at any depth.
   - Option 2: Keep `val` shallow.
3. **MQ-3 (aliasing model):**
   - Option 1 (Design Option (a)): Value semantics with Copy-on-Write (CoW) + `mut`, reversing `D-070` / `STRUCT-16`.
   - Option 2 (Design Option (b)): Mutability in the type (`mut []T` vs `[]T`) with reference semantics.
   - Option 3 (Design Option (c)): Rust-like exclusive ownership and borrowing.
   - Option 4 (Design Option (d)): Keep shallow `val` + add read-only struct fields only.
4. **MQ-17 (`:clone()` on collections/structs):**
   - Option 1: Do not add `:clone()` to collections or structs (keep `STRLIB-9` on `string` only).
   - Option 2: Add `:clone()` to vectors/maps/sets.
5. **MQ-18 (tuple destructuring and temporary rvalues):**
   - Option 1: `var @(a, b) = call()` makes all destructured bindings `var` (`val @(a, b)` makes all deeply immutable `val`s, with no per-element `mut` syntax), and mutating temporary rvalues (`[1, 2]:push(3)`, `Point { x: 1 }:inc()`) is rejected.
   - Option 2: Allow per-binding `mut` inside `val @(a, mut b)` and/or allow mutating temporary rvalues.

## Decision
Option 1 for each of `MQ-1`, `MQ-2`, `MQ-3` (Design Option (a)), `MQ-17`, and `MQ-18`, decided by the owner on 2026-11-21:
- **Reverses `D-070` (`STRUCT-16`):** structs, vectors, maps, sets, and unions have **value semantics with copy-on-write (CoW)** — assignment and non-`mut` argument passing produce logically independent values. Under Perceus RC (AD-002), copies are logical (`O(1)` RC dup) and mutate in place when uniquely owned (`rc == 1`), detaching lazily only when shared (`rc > 1`).
- **Deep `val` and `var`:** `val` is deeply (transitively) immutable (`VAR-1`, `VAR-E10`); `var` stays as the sole mutable local binding form, deeply mutable subject to struct field `mut` (`VAR-2`); `mut` is a reserved keyword (`LEX-2`) used strictly as a modifier, never a third binding form.
- **No collection/struct `:clone()`:** explicit `:clone()` is not added to collections or structs (`STRLIB-9` stays string-only).
- **Tuple destructuring & temporary rvalues:** `val @(a, b) = call()` binds deeply immutable `val`s and `var @(a, b) = call()` binds mutable `var`s, with no per-binding `mut` syntax (`TUP-4`–`TUP-6`); mutating temporary rvalues (`[1, 2]:push(3)`, `Point { x: 1 }:inc()`) is rejected (`VAR-E11`).

## Consequences
- **Target domains:** Pino aims at Go-like native microservices (small, fast, efficient native services) and games, with the best balance of simplicity, performance, and efficiency.
- **Memory model synergy (AD-002) and arena readiness:** Pure data graphs (`struct`, `[]T`, `map`, `set`, `union`, non-closure `interface`) are strictly acyclic, leaving only `D-056` closures capturing `var`s in the cycle collector. The memory model keeps automatic arena (region) allocation in mind (per request, per frame, and for pure-function temporaries; arenas are an optimizer feature with no syntax, requiring the IR to carry escape facts and a pluggable allocator). Escape points under value semantics are:
  1. return values,
  2. writes through `mut` parameters and `mut fn` `self`,
  3. `var`s captured by closures (`D-056`), and
  4. top-level `var`s.
- **Thread-safety direction (for future concurrency design):** Plain data values cannot race. When concurrency is designed, reference counts on values that cross threads will need atomic updates (for example marking objects on thread crossing as Lean 4 does), closures capturing `var`s must not cross threads (similar to Swift's `Sendable`), and shared mutable state will use explicit stdlib types (`Mutex[T]`, `Atomic`, channels).
- **Linked-data caveat:** Storing a struct in a variable or collection stores a copy, not a shared reference. Linked or graph structures use indices or IDs into a flat container (with future stdlib `Pool[T]` using checked or generational IDs). An opt-in `ref` type (`D-070` Option 3) remains "not now".
- **Where Pino shines vs. where it is weaker:** Pino shines for services, compilers, central-state games, data pipelines, and teaching; it is weaker for object webs, GUI trees with parent pointers, hard real-time systems, and shared-memory concurrent data structures.
- **Normative rules:** updates `LEX-2`, `VAR-1`, `VAR-2`, `VAR-E1`, `STRUCT-16`, `TUP-4`–`TUP-6`; adds `VAR-E10`, `VAR-E11`, `VEC-19`, `MAP-13`, `SET-7`.
