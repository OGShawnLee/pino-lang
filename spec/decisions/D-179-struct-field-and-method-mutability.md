# D-179: Struct field, method, and interface mutability (`mut` and `mut fn`)

Status: accepted
Raised by: 043 (MQ-5, MQ-6, MQ-7, MQ-8, MQ-9)
Feature: spec/features/structs.md, spec/features/interfaces.md, spec/features/generics.md

## Question
How are mutable struct fields, `pub mut` visibility, mutating methods (`mut fn`, `self` reassignment, bound methods), and interface `mut` contracts specified?

## Evidence
- Spec & Decisions: `STRUCT-2`, `STRUCT-4`–`STRUCT-9`, `STRUCT-11`, `STRUCT-14`, `STRUCT-20`, `D-163`, `D-078`, `D-105`, `IFACE-1`, `IFACE-4`–`IFACE-6`, `D-083`, `GEN-9`.
- Design `043` (§3–§4, §8, `MQ-5`–`MQ-9`): `7` conformance `.out` cases mutate struct fields (`3` externally, `4` in methods); `4` of `43` structs (`~6` of `98` fields) and `10` methods mutate in `projects/pino-compiler/`. Under Option (a) value semantics, uncalled binding of a `mut fn` (`val inc = m:increment`) conflicts with `inout` receiver semantics.

## Options
1. **MQ-5 (field mutability & write access):**
   - Option 1: Fields are deeply read-only by default; `mut name Type` allows writes (and deep interior mutation) by any code with visibility (`D-163`) and mutable access to the instance.
   - Option 2: `mut` fields writable only inside the struct's own `mut fn` methods.
   - Option 3: Non-`mut` fields prevent only reassignment, still allowing interior mutation.
2. **MQ-6 (`pub mut` meaning):**
   - Option 1 (orthogonal): `pub mut x T` is readable and writable from any module with mutable access (`pub` always precedes `mut`); read-public/write-private uses private `mut x T` + a `pub fn` getter.
   - Option 2 (V-style asymmetric): `pub mut x T` is readable everywhere, writable only in declaring module.
3. **MQ-7 (mutating method syntax & `self` reassignment):**
   - Option 1: `mut fn name(...)` (mutually exclusive with `static fn`); `self` cannot be reassigned (`self = ...` is an error; only `mut` fields of `self` can be mutated).
   - Option 2: `mut fn name(...)` allowing `self = new_value`.
   - Option 3: `fn (mut self) name(...)`.
   - Option 4: Infer mutating methods with no keyword.
4. **MQ-8 (bound `mut fn`):**
   - Option 1: Forbid binding a `mut fn` without calling it (`val f = m:inc` is a checker error); non-mutating bound methods (`STRUCT-14`) stay valid, and stateful closures over `var m` are written explicitly as `fn () { m:inc() }`.
   - Option 2: Allow binding a `mut fn` on local `var` by implicitly desugaring to a `D-056` closure.
5. **MQ-9 (interface `mut fn` and `mut` properties):**
   - Option 1: Interfaces support `mut fn` and `mut prop Type` with safe covariance: a non-`mut` interface method rejects a `mut fn` implementation, a `mut fn` interface method accepts `mut fn` or plain `fn`, a read-only interface property accepts read-only or `mut` struct fields, and a `mut` interface property requires a `mut` struct field.
   - Option 2: Require exact `mut` parity on both methods and properties.
   - Option 3: Disallow `mut` properties in interfaces.

## Decision
Option 1 for each of `MQ-5`, `MQ-6`, `MQ-7`, `MQ-8`, and `MQ-9`, decided by the owner on 2026-11-21:
- **Fields:** Struct fields are deeply read-only by default. `mut name type` permits writes and deep interior mutation by any code with field visibility (`D-163`) and mutable access to the instance (`STRUCT-4`, `STRUCT-6`, `STRUCT-7`, `STRUCT-E15`).
- **`pub mut`:** Orthogonal visibility and mutability — `pub mut name type` is readable and writable cross-module when the receiver is mutable (`STRUCT-20`). `pub` always precedes `mut`; `mut pub` and combining `static` with `mut` (`static mut` / `mut static`) are parse errors (`STRUCT-E18`).
- **Embedding & literals:** Embedded struct fields (`STRUCT-8`) preserve their own `mut` and `pub` modifiers; struct literals still require every field (`STRUCT-E12`, `D-078`).
- **Mutating methods:** Declared `mut fn name(...)` (`STRUCT-2`, `STRUCT-9`), mutually exclusive with `static fn` (`STRUCT-11`, `STRUCT-E18`). Inside plain `fn`, `self` is deeply immutable; reassigning `self = ...` is forbidden even inside `mut fn` (`STRUCT-5`, `STRUCT-E16`).
- **Bound methods:** Uncalled binding of a `mut fn` (`val f = m:inc`) is a checker error (`STRUCT-E17`); `STRUCT-14` stays valid for non-mutating methods, and stateful closures over `var m` use `fn () { m:inc() }`.
- **Interfaces & bounded generics:** Interfaces support `mut fn` and `mut prop Type` (`IFACE-1`, `IFACE-4`, `IFACE-5`, `IFACE-6`) with safe covariance: non-`mut` interface methods reject `mut fn` implementations, `mut fn` interface methods accept `mut fn` or plain `fn`, read-only interface properties accept read-only or `mut` struct fields, and `mut` interface properties require `mut` struct fields (`IFACE-E7`). Generic bounds (`GEN-9`) require a mutable receiver to call a `mut fn` or mutate a `mut` property on a bounded type parameter `T`, while `T` itself carries no mutability qualifier.
