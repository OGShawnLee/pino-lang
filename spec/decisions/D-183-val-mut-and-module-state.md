# D-183: `val mut` bindings and module-state purity facts

Status: accepted
Raised by: 043 (MQ-1 revision, MQ-15)
Feature: spec/features/variables.md, spec/features/entry-point.md, spec/features/modules.md, spec/features/functions.md

## Question
Should Pino add a third binding form (`val mut`) whose contents can be mutated without allowing reassignment of the binding itself, and how should private mutable state in imported modules (`var` / `val mut`) be modeled for purity and optimization?

## Evidence
- Spec & Decisions: `VAR-1` (`D-177`) makes `val` deeply immutable and `VAR-2` makes `var` both reassignable and deeply mutable (bounded by struct field `mut`). `D-180` governs `mut` parameters and `D-056` / `FN-21` govern closure captures. `D-182` (`MQ-15`) permits private top-level `var` in non-root imported modules while forbidding top-level `var` in Program Mode root files (`MAIN-3`, `MAIN-E3`) and rejecting `pub var` and `@@Testable var` (`MOD-5`, `MOD-E9`).
- Self-host & idioms: Compiler modules (`projects/pino-compiler/modules/Checker.pino`, `Transformer.pino`, `Utility.pino`) keep module-private mutable registries, counters, and `error_list` collections that are mutated in place (`:push`, index writes, `mut` arguments) but never rebound to a different collection.
- Owner revision (2026-11-21): When reviewing `043` `MQ-1`, the owner clarified they had intended option 2 — a third binding form (`val mut`) — and settled its syntax, placement, capture, and scalar behavior alongside option 1 for module-level mutable state.

## Options
1. **Part 1 — `val mut` binding form (`MQ-1` revision):**
   - Syntax `val mut name = expr` / `val mut name Type = expr` (`mut` directly after `val`; `var mut`, `mut val`, and `val mut @(...)` are parse errors).
   - Interior-mutable at any depth like `var` (still requiring `mut` fields per `STRUCT-E15`), allowed as a `mut` argument (`f(mut x)`), never reassignable (`x = ...` / `x op= ...` rejected by `VAR-E12`).
   - Same placement and visibility rules as `var` (`MAIN-E3`, `MOD-E9`), same `D-056` capture-by-reference when mutated by a closure (`FN-21`), same `D-177` value semantics, and a checker error (`VAR-E13`) when the binding's type has no mutable interior.
2. **Part 2 — Mutable state in modules (`MQ-15` follow-up):**
   - Option 1: Keep private top-level `var` and `val mut` allowed in imported modules with no new syntax, and have the checker infer transitive `ReadsModuleState` and `WritesModuleState` purity facts per function and method (producing no diagnostics).
   - Option 2: Require explicit effect annotations or forbid private module-level mutable state.

## Decision
Decided by the owner on 2026-11-21:

### 1. `val mut` bindings
- **Syntax:** `val mut name = expr` and `val mut name Type = expr`, with `mut` immediately after `val`. `var mut`, `mut val`, and `val mut @(a, b) = ...` are parse errors (`var @(a, b)` already exists for mutable destructuring).
- **Mutation:** A `val mut` binding's contents may be mutated at any depth exactly like a `var` — mutating stdlib methods (`push`, `pop`, `remove`, `add`), index writes (`v[i] = x`, `m[k] = v`, `m[k] op= v`), writes to `mut` struct/interface fields, calls to `mut fn` methods, and passing as a `mut` argument (`f(mut x)`). Struct field writes still require the field to be `mut` (`STRUCT-E15`).
- **No reassignment (`VAR-E12`):** Reassigning the binding itself (`x = ...` or `x op= ...`) is a checker error stating that the binding is `val mut` and cannot be reassigned, with help: `"declare it with 'var' to allow reassignment"`.
- **`val` and `var` unchanged:** `val` remains deeply immutable (`VAR-1`), and `var` remains both reassignable and deeply mutable (`VAR-2`).
- **`mut` arguments (`VAR-13`):** Passing a `val mut` (or a mutable path rooted in it) as a `mut` argument (`check(mut error_list)`) is allowed; the callee follows its own `mut`-parameter rules (`D-180`).
- **Placement and visibility:** Identical to top-level `var` — allowed privately in imported modules and in scripts, rejected at the top level of a Program Mode root file (`MAIN-3`, `MAIN-E3`), and rejected with `pub` or `@@Testable` (`MOD-5`, `MOD-E9`).
- **Closures (`D-056` / `FN-21`):** A closure that mutates a captured `val mut` captures it by reference per closure instance, exactly like a `var` that it mutates.
- **Value semantics (`D-177`):** Unchanged — `val mut a = [1]; var b = a` gives `b` an independent logical copy.
- **Mutable interior required (`VAR-E13`):** `val mut` requires a declared or inferred type with mutable contents; using `val mut` on a type with nothing mutable inside is a checker error with help `"use 'val' if it never changes, or 'var' to reassign it"`:
  - Rejected (no mutable interior): `int`, `float`, `bool`, `rune`, `string`, enums, unions (including `Option` and `Result`, since union payloads are deeply immutable per `UNION-24`), tuples, function types, structs with no `mut` field (counting embedded fields), and interfaces with no `mut` property and no `mut fn`.
  - Allowed (mutable interior or unknown): vectors, maps, sets, structs with at least one `mut` field (direct or embedded), interfaces with at least one `mut` property or `mut fn`, generic type parameters `T` (cannot be known statically), and the internal error type (silent per A4).

### 2. Mutable state in modules (Option 1)
- Private top-level `var` and `val mut` in imported modules remain allowed with no new syntax.
- The checker infers two purity facts for every function and method, with **no diagnostics**:
  - `ReadsModuleState`: reads a top-level `var` or `val mut` of any module, directly or through a call.
  - `WritesModuleState`: reassigns or mutates a top-level `var` or `val mut` of any module, directly or through a call.
- Both facts are computed transitively over the call graph using a fixed point over strongly connected components (supporting direct and mutual recursion).
- Calls through function values (lambdas, function-typed fields or parameters, indexed function elements, or interface methods whose concrete implementation is unknown statically) are treated conservatively as unknown, marking both `ReadsModuleState` and `WritesModuleState` `true` for the caller.
- A closure body's effects count for the function that calls it, not the function that declares it.
- These facts are exposed on the checker's memory facts and `SemanticModel` for downstream IR and arena passes.

## Consequences
- **Why module state stays allowed:** Module state is a practical need for caches, counters, registries, and `error_list`.
- **Call-site visibility trade-off:** Private module state is the one place in Pino where mutation is not visible at the call site.
- **Instancing and testing:** Module state makes a module single-instance and causes tests to share state across runs unless reset explicitly.
- **Future concurrency:** Module-level mutable state will require an explicit rule when concurrency is designed (parallel to `Sendable` closures and explicit synchronization types in `D-177`).
- **Recommended idiom:** Idiomatic Pino code keeps mutable state in `main` (or a `Context` struct) and passes it explicitly with `mut`.
- **IR & arena optimization:** Inferred `ReadsModuleState` and `WritesModuleState` let future IR and arena passes identify pure functions without adding user-facing effect syntax.
- **Normative rules:** adds `VAR-12`, `VAR-13`, `VAR-E12`, `VAR-E13`; updates `VAR-1`, `VAR-2`, `MAIN-3`, `MAIN-E3`, `MOD-5`, `MOD-E9`, `FN-21`; extends `D-177` and `D-182`.
