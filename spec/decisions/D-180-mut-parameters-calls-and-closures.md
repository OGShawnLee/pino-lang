# D-180: Mutable parameters (`mut`), call-site exclusivity, function types, and closure captures

Status: accepted
Raised by: 043 (MQ-10, MQ-11)
Feature: spec/features/functions.md

## Question
Are function, method, and lambda parameters deeply immutable by default, how are mutable parameters declared, passed, typed, and checked for aliasing, and may a closure capture a `mut` parameter (or `mut fn` `self`)?

## Evidence
- Spec & Decisions: `FN-1`, `FN-12`, `FN-20`, `FN-21`, `D-056`, `D-101`.
- Design `043` (§5–§6, `MQ-10`, `MQ-11`): `-re` currently permits reassigning any `ParameterSymbol` (`BodyChecker.Operators.cs:492-497`) without spec backing; under Option (a) value semantics, `mut` parameters borrow the caller's mutable place (`inout`), so escaping closures cannot outlive the call and overlapping `mut` arguments violate exclusivity.
- Prior art: V (`fn f(mut x T)` + `f(mut a)`), Swift (`inout` + call-site `&a` + SE-0176 exclusivity + SE-0035 no escaping `inout` capture), Hylo (`inout` + law of exclusivity).

## Options
1. **MQ-10 (parameters, call-site `mut`, exclusivity):**
   - Option 1: Parameters (including lambdas, implicit `it`, and all stdlib callbacks) are deeply immutable by default (`FN-E14`); mutable parameters use `mut name Type` and require `mut` at every call site (`f(mut a)`, `FN-E15`); overlapping `mut` arguments sharing the same root binding (`swap(mut a, mut a)`, `f(mut a, mut a:x)`, `f(mut v[i], mut v[j])`, `a:merge(mut a)`) are statically rejected (`FN-E16`).
   - Option 2: `mut` only on parameter declaration, not at call site.
   - Option 3: Same as Option 1, but keep local reassignment `p = ...` on non-`mut` parameters.
   - Option 4: No `mut` parameters at all.
2. **MQ-11 (capturing `mut` parameters or `mut fn` `self` in closures):**
   - Option 1: Forbid capturing a `mut` parameter (and mutably capturing `self` inside a `mut fn`) in any closure (`FN-E17`), keeping `D-056` capture-by-reference for local `var`s only.
   - Option 2: Allow capturing a `mut` parameter in non-escaping stdlib callbacks only.
   - Option 3: Allow capturing a `mut` parameter by read-only snapshot value.

## Decision
Option 1 for both `MQ-10` and `MQ-11`, decided by the owner on 2026-11-21:
- **Parameters deeply immutable by default:** Function, method, and lambda parameters (as well as implicit `it` and all stdlib callbacks) are deeply immutable by default; neither reassignment (`VAR-E1`) nor interior mutation (`FN-E14`) is allowed.
- **`mut` parameters and call-site `mut`:** Mutable parameters are written `mut name Type` and require `mut` at every call site (`f(mut a)`). Missing `mut`, unexpected `mut`, or passing an immutable or non-place expression as `mut` is a checker error (`FN-1`, `FN-E15`).
- **Exclusivity (`FN-E16` vs `FN-31`):** Overlapping `mut` arguments sharing the same root binding (e.g. `swap(mut a, mut a)`, `f(mut a, mut a:x)`, `f(mut v[i], mut v[j])`, `a:merge(mut a)` where a mutating receiver counts as a `mut` access) are statically rejected (`FN-E16`). One `mut` access combined with a read on the same root (e.g. `f(mut a, a)`, `v:push(v[0])`, `p:add(p:x)`) is allowed (`FN-31`): read arguments are evaluated as values, left to right, before the call's mutable access begins.
- **Function types:** Function types carry per-parameter `mut` (`fn (mut T) -> R`), which is distinct and non-convertible from `fn (T) -> R` (`FN-12`, `FN-20`, extending `D-101`).
- **Closures:** Capturing a `mut` parameter (or mutably capturing `self` inside a `mut fn`) inside a closure is a checker error (`FN-E17`). `D-056` capture-by-reference applies to local `var`s only, and interior mutation of a captured `var` counts as mutation (`FN-21`).
