# D-182: Union payload immutability, loop variable immutability, module-level `var`, and target path evaluation order

Status: accepted
Raised by: 043 (MQ-13, MQ-14, MQ-15, MQ-16)
Feature: spec/features/unions.md, spec/features/loops.md, spec/features/modules.md, spec/features/entry-point.md, spec/features/variables.md

## Question
Are union payloads and loop variables (`for x in coll`) deeply immutable, what top-level `var` forms are permitted in modules vs. Program Mode root files (`MOD-5`, `MOD-E9`, `MAIN-E3`), and how many times are target path subexpressions evaluated in compound/nested assignments (`VAR-4`)?

## Evidence
- Design `043` (§8–§9, §14, `MQ-13`–`MQ-16`):
  - `spec/features/unions.md` previously lacked an explicit payload immutability rule, forcing `ReachabilityBuilder.cs:164-171` to mark union edges mutable.
  - `LOOP-3` makes `for N`'s `it` constant while `BodyChecker.cs:501` set `isMutable: true` on `for x in coll`; under value semantics, mutating `x` would silently mutate only a per-iteration copy.
  - `ModuleChecker.Phase1.cs:63` already permits private top-level `var` in non-root modules while forbidding it in Program Mode root files (`MAIN-E3`), and `BodyChecker.cs:263` rejects `@@Testable var`.
  - `VAR-4` did not specify whether `v[f()] += 1` evaluates `f()` once or twice.

## Options
1. **MQ-13 (union payloads):**
   - Option 1: Specify union values, payloads, and `when`/`is` pattern bindings as deeply immutable (`UNION-24`).
   - Option 2: Allow mutating union payload fields in place through mutable pattern bindings.
2. **MQ-14 (loop variables):**
   - Option 1: Make `for x in coll` and `for i, x in coll` loop variables deeply immutable, matching `for N`'s `it` (`LOOP-3`).
   - Option 2: Keep loop variables mutable as local copies.
3. **MQ-15 (module-level `var` and `@@Testable var`):**
   - Option 1: Keep `MAIN-E3` forbidding top-level `var` in Program Mode root files, allow private top-level `var` in non-root imported modules (in strict textual order per `VAR-7`/`VAR-E8`), confirm `pub val` is a true deeply immutable data constant (with the closure-over-private-`var` caveat), and keep `MOD-E9` rejecting both `pub var` and `@@Testable var`.
   - Option 2: Same, but allow `@@Testable var`.
   - Option 3: Forbid top-level `var` in imported modules too.
4. **MQ-16 (target path evaluation order):**
   - Option 1: Specify that target path subexpressions in compound and nested assignments (`v[f()] += 1`, `a:b[f()] = g()`) are evaluated exactly once left-to-right before reading/mutating the slot.
   - Option 2: Leave evaluation count unspecified.

## Decision
Option 1 for each of `MQ-13`, `MQ-14`, `MQ-15`, and `MQ-16`, decided by the owner on 2026-11-21:
- **Unions:** Union values and their payloads, along with pattern bindings from `match`/`when` and `is`, are deeply immutable (`UNION-3`, `UNION-16`, `UNION-24`).
- **Loop variables:** Loop variables in `for x in coll` and `for i, x in coll` are deeply immutable (`LOOP-5`–`LOOP-10`, `LOOP-18`, `LOOP-E2`), matching `for N`'s `it` (`LOOP-3`).
- **Top-level `var` and `pub val`:** Private top-level `var` is allowed in non-root imported modules following strict textual order (`VAR-7`, `VAR-E8`); Program Mode root files still forbid top-level `var` (`MAIN-3`, `MAIN-E3`); `pub val` is a true deeply immutable data constant (noting that a `pub val` holding a closure over a module-private `var` can still observe and mutate that private `var` via `D-056`); `MOD-E9` rejects both `pub var` and `@@Testable var` (`MOD-5`, `MOD-E9`).
- **Target path evaluation:** Target path subexpressions in compound and nested assignments (`v[f()] += 1`, `a:b[f()] = g()`) are evaluated **exactly once** left-to-right before reading or mutating the slot (`VAR-4`, extending `D-141`).

## Extension (2026-11-21, `D-183`)
On 2026-11-21 (`D-183`) the owner extended `MQ-15` to cover `val mut` bindings alongside `var` and chose Option 1 for module-level mutable state: private top-level `val mut` is allowed in non-root imported modules in strict textual order (`MOD-5`), Program Mode root files forbid top-level `val mut` alongside `var` (`MAIN-3`, `MAIN-E3`), and `MOD-E9` rejects `pub val mut` and `@@Testable val mut`. The checker infers transitive `ReadsModuleState` and `WritesModuleState` purity facts across module-level `var` and `val mut` accesses without emitting diagnostics.
