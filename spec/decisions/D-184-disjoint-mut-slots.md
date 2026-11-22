# D-184: Disjoint `mut` slots, run-time exclusivity checks, and `v:swap`

Status: accepted
Raised by: owner review of the Pool[T] design (2026-11-21)
Feature: spec/features/functions.md, spec/stdlib/vector.md

## Question
Under `D-180` (`FN-E16`), passing two `mut` accesses that share a root binding is always a compile error (`PN3226`), even when they refer to different fields (`f(mut p:x, mut p:y)`) or different collection elements (`duel(mut enemies[0], mut enemies[1])`, `duel(mut enemies[i], mut enemies[j])`). Should Pino allow disjoint `mut` places under the same root binding, how should static proofs and run-time checks work, and should vectors provide a built-in `swap` method?

## Evidence
- Spec & Decisions: `D-177` (value semantics and `mut`), `D-180` (`FN-E16`, `FN-31`), `D-182` (loop variable and pattern binding immutability), `D-183` (`val mut`), `VAR-9` (assignment as an expression), `VEC-E4` (vector bounds panic), `MAP-E1` (missing map key panic), `ERR-3` / `ERR-5` (`panic` halts with exit code 101).
- Game and systems ergonomics: Having two entities from one collection interact mutably (e.g., `duel(mut enemies[i], mut enemies[j])` or `enemies[i]:hit(mut enemies[j])`) is an every-frame need; requiring users to work around same-root rejection for every pair of elements breaks ergonomics.
- Prior art: Swift SE-0176 ("Law of Exclusivity") proves disjointness statically where possible and inserts a run-time exclusivity check with a clear panic when dynamic indexing prevents a static proof.

## Options
1. **Option 1 (compile-time proof first, run-time slot-address check fallback, plus `v:swap`):**
   - Keep proven overlap as a compile error (`PN3226`, `FN-E16`).
   - Allow proven-disjoint places with zero run-time cost (`FN-32`, `FN-33`).
   - Allow dynamic index cases whose disjointness cannot be decided statically, checking resolved slot addresses at run time before entering the callee and panicking with a clear human-readable message on overlap (`FN-34`, `FN-35`).
   - Add `v:swap(i, j)` to vectors (`VECLIB-26`, `VECLIB-27`, `VECLIB-E2`).
2. **Option 2 (static-only refinement):** Allow different struct fields and distinct constant indexes, but keep `f(mut v[i], mut v[j])` as a compile error.
3. **Option 3 (unchanged `D-180`):** Reject all same-root `mut` pairs statically.

## Decision
Option 1, decided by the owner on 2026-11-21:

### 1. Place paths (`FN-35`)
A `mut` access in a call is either a `mut` argument (`f(mut p)`) or the receiver of a `mut fn` call or mutating built-in method. Its **place path** is its root binding followed by a sequence of steps:
- **field steps:** `:name`
- **index steps:** `[expr]`

Two special cases:
- **Bare fields inside a method:** An unqualified field name `x` inside an instance method (resolving to a `FieldSymbol` on the implicit `self`) has the place path `self` -> `:x`. Thus inside a `mut fn`, `swap_ints(mut x, mut y)` has distinct field steps and is proven disjoint, while `swap_ints(mut x, mut self:x)` is proven overlapping (`PN3226`).
- **Receivers keep their full path:** `v[i]:hit(mut v[j])` compares the receiver path `v` -> `[i]` against the argument path `v` -> `[j]`. An implicit-`self` sibling call such as `merge(mut self)` has the receiver path `self` with no steps.

### 2. Overlap algorithm (`FN-E16`, `FN-32`, `FN-34`, `FN-35`)
For every pair of `mut` accesses in one call that share a root binding (`FN-35`), walk both paths together from the root, one step at a time, maintaining a list of **unknown index-step pairs** seen so far:
1. **Two field steps:**
   - Same name: continue to the next step.
   - Different names: **proven disjoint** (`FN-32`); stop and drop the list.
2. **Two index steps:** Compare the two index expressions syntactically (`FN-33`):
   - **Proven equal:** continue.
   - **Proven different:** **proven disjoint** (`FN-32`); stop and drop the list.
   - **Unknown:** append this step pair to the list, then continue.
3. **One path ends** (or both end together) with every step so far equal or unknown:
   - If the unknown list is empty, the places are **proven overlapping**: compile error `PN3226` (`FN-E16`).
   - Otherwise, the pair **needs a run-time check** carrying that unknown step-pair list (`FN-34`).

### 3. Run-time rule (`FN-34`)
The two places alias **only if every** unknown step pair in the list resolves to the same slot. For each unknown step pair, generated code compares the resolved address of the slot selected by each side's index at that depth of the shared prefix (not index or key values, and not only final leaf addresses). For example, in `f(mut enemies[i], mut enemies[j]:hp)`, `&enemies[i]` and `&enemies[j]:hp` differ even when `i == j`, so the check compares `&enemies[i]` with `&enemies[j]`.

The checker exposes each call's required run-time checks (naming the two accesses and their list of unknown `IndexExpressionSyntax` step pairs) on `SemanticModel` as an obligation for the IR and backends.

### 4. Syntactic index-expression comparison (`FN-33`)
The checker compares index expressions strictly by **syntax** without constant folding (`TryEvalConstant` is not used; a `val i = 0` remains the name `i`). Parentheses around an index are unwrapped. Any form not listed below is **unknown**:
- **Integer literals**, including a unary minus on an integer literal (`-1`): proven equal when their values are equal, proven different otherwise.
- **String literals without interpolation** (map keys): proven equal when their unescaped texts are equal, proven different otherwise.
- **Literals of any other type** (`bool`, `rune`, `float`, enum members): **unknown**.
- **Same immutable name with optional constant offset:** `n`, `n + k`, or `n - k`, where `n` resolves to the **same symbol** on both sides and `k` is an integer literal.
  - `n` must be an immutable binding: a `val` local (not `val mut`), a non-`mut` parameter, a loop variable (`D-182`), a pattern binding, or `it`.
  - A `var`, `val mut`, `mut` parameter, or struct/interface field is never `n` (assignment is an expression per `VAR-9`, so a mutable binding can change between arguments).
  - Same symbol and same net offset: **proven equal** for any index or key type (`v[i]` vs `v[i]`, `m[k]` vs `m[k]`, `p[h]` vs `p[h]`).
  - Same symbol and different net offsets: **proven different** for `int` indexes only (`v[i]` vs `v[i + 1]`).
- **Branch fact:** `a` and `b` are two distinct immutable bindings (under the same restrictions as `n`), and the two index expressions are literally `a` and `b` compared by resolved symbol identity (never by name text). When the call sits inside the then-branch of an `if` statement or `if` expression whose entire condition (with parentheses unwrapped) is `a != b` or `b != a`, then `a` and `b` are **proven different**.
  - The fact holds anywhere inside that then-branch, including inside nested `if`s, loops, and blocks.
  - It does not hold inside lambda bodies (or nested function declarations), in `else` branches, or after the `if`.
  - Conditions joined with `and` or `or` do not establish a branch fact.
  - Shadowing is safe because comparison uses symbol identity (`if a != b { val a = b; f(mut v[a], mut v[b]) }` gets a run-time check).

### 5. Diagnostics, run-time panic, and missing map keys (`FN-E16`, `FN-34`)
- **Proven overlap** keeps `PN3226` and its existing message.
- **Proven disjoint** produces no diagnostic and records no run-time check.
- **Needs a run-time check** produces no diagnostic; the checker records the check for the IR.
- **Run-time panic (`FN-34`):** After all arguments are evaluated and their slots resolved, and before the callee runs, if the two places alias by the run-time rule, the program panics (`ERR-3`, exit code `101` per `ERR-5`) with the exact message:

  `Cannot pass the same element of '<collection>' as two 'mut' arguments to <target>.`

  - `<target>` is the unqualified function or method name as written at the call site, in single quotes (`'duel'` for `duel(...)`, `Mod::duel(...)`, and `x:duel(...)`). For a call through a function value, `<target>` is the unquoted words `this function`.
  - `<collection>` is the first identifier of the path as written at the call site (`enemies` for `enemies[i]`, `items` for a bare field `items[i]` inside a method, and `self` for `self:items[i]`).
- **Missing map keys:** `mut m[k]` requires `k` to be present in `m`. When `k` is absent, the program panics per `MAP-E1` ("not found in map") while resolving that argument's slot, before any `D-184` exclusivity check runs.

### 6. Vector `swap` (`VECLIB-26`, `VECLIB-27`, `VECLIB-E2`)
- `v:swap(i, j)` is a `mut fn (int, int)` returning `void`. It requires `v` to be a mutable place and exchanges the elements at zero-based indexes `i` and `j` (`VECLIB-26`).
- Both indexes are bounds-checked first; an out-of-range index panics as `VEC-E4` does, even when `i == j`, so `v:swap(99, 99)` panics (`VECLIB-E2`).
- An in-range `v:swap(i, i)` is a no-op (`VECLIB-27`).

## Consequences
- Games and collection algorithms can pass disjoint fields or elements (`duel(mut enemies[0], mut enemies[1])`, `duel(mut enemies[i], mut enemies[j])`, `enemies[i]:hit(mut enemies[j])`, `swap_ints(mut p:x, mut p:y)`) directly without sacrificing Pino's exclusivity guarantee.
- Compile-time proofs eliminate run-time overhead in common constant, offset, field, and `if i != j` patterns while keeping checker analysis simple, local, and predictable (no constant folding or general SMT reasoning).
- Normative rules: rewords `FN-E16`; adds `FN-32`, `FN-33`, `FN-34`, `FN-35`, `VECLIB-26`, `VECLIB-27`, `VECLIB-E2`; extends `D-180`.
