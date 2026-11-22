# D-186: Print forms, `main` failure output, and prerequisites for the IR

Status: accepted
Raised by: 056 (IQ-12, Open Points 3–6)
Feature: spec/features/maps.md, spec/features/sets.md, spec/features/pools.md, spec/stdlib/pool.md, spec/features/errors.md, spec/features/structs.md, spec/features/types.md, spec/stdlib/builtins.md

## Question
Before the `-re` IR (`.tickets/056-re-ir-design/design.md`) is built, which exact text do `str(m)`, `str(s)` and `str(p)` produce, what does `main` write when it fails, what happens for `INT64_MIN // -1`, what does a struct that contains itself mean, and what does `time()` return on every platform?

## Evidence
- Design 056 §11 `IQ-12` and Open Points 3–6 list the four gaps; its "Owner answers (2026-11-21)" section approves the design and settles them (`.tickets/056-re-ir-design/design.md:1350-1371`).
- `MAP-9` showed `key: value`, but map literals use `->` (`D-100`); `SET-6` and `POOL` printing had no case (`[no-case]`, "unspecified").
- `D-080` (open) asked for the `main` failure text; the tree-walk engine already writes `Error: <payload>` and `Error: Option::None` and exits with 1.
- Inline struct layout (`AD-003`, design §2.1, `IQ-2`) gives a struct that contains itself by value an infinite size.
- `INT64_MIN // -1` overflows 64-bit integers; sized integer types are deferred (`OQ-6`).
- `time()` (`BUILTIN-7`) already says Unix epoch milliseconds, but a platform could implement it as a boot timer.

## Options
1. Record the owner's answers as one decision with normative rules (`MAP-9`, `SET-6`, `POOL-13`, `POOL-14`, `ERR-24`, `STRUCT-E19`, `TYPE-E6`, `BUILTIN-7`).
2. Leave each item open until the IR needs it.

## Decision
Option 1, decided by the owner on 2026-11-21 (approval of `056-re-ir-design`, `IQ-12` and Open Points 3–6):

1. **Print forms.**
   - Maps: `map[K, V] { "k" -> v, ... }`, in insertion order, entries written with `->` (`D-100`, `D-004`); empty map `map[K, V] { }` (`MAP-9`).
   - Sets: `set[T] { e1, e2 }`, in insertion order, strings quoted; empty set `set[T] { }` (`SET-6`, `D-175`).
   - Pools: `Pool[T] { Handle(i, g) -> v, ... }` in increasing slot-index order, `T` written as the concrete type, values shown as `str(v)` with strings quoted; empty pool `Pool[T] { }`. `type(p)` is `"Pool"` and `type(h)` is `"Handle"` (`POOL-13`, `POOL-14`).
2. **`main` failure output (accepts `D-080`).** When `main` returns `Result::Failure(e)` or `Option::None`, the runtime writes `Error: <str(e)>` or `Error: Option::None` to `stderr` and exits with code 1 (`ERR-24`, `ERR-18` to `ERR-20`).
3. **Panic text.** `-re` writes a panic as `thread 'main' panicked at '<msg>'` followed by a line `  at <file>:<line>:<col>` for now. A Pino-style panic message is explicitly deferred; `D-081` stays open.
4. **Direct self-containing structs (`STRUCT-E19`).** A struct that contains itself by value, directly or through other structs or embedded structs, is rejected with `PN3114` (`RecursiveStructValue`). Collections, `Option`, unions, interfaces, handles and function types break the chain. The checker follows direct struct-typed fields, embedded structs and tuples, plus a generic struct's field of its own instantiation (`struct Box[T] { x Box[T] }`); containment through another generic's type argument is not followed.
5. **Integer division overflow (`TYPE-E6`).** `INT64_MIN // -1` and `INT64_MIN % -1` panic (`ERR-3`, exit code 101 per `ERR-5`) with `Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.`. Other integer overflow stays deferred with sized integer types (`OQ-6`).
6. **`time()` (`BUILTIN-7`).** Returns milliseconds since the Unix epoch (`1970-01-01 00:00:00 UTC`) on every platform, including Windows; it is not a monotonic boot timer.

Message texts are plain sentences without compiler jargon (owner, 2026-11-21):
- `Struct 'Bad' cannot contain itself directly because it would have infinite size.`
- `Struct 'A' cannot contain itself, even through 'B', because it would have infinite size.`
- Help: `wrap the field in 'Option[<Struct>]', store it in '[]<Struct>' or a recursive union, or use 'Handle[<Struct>]' with a 'Pool[<Struct>]'`
