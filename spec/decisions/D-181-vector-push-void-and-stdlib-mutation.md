# D-181: `vec:push` / `vec:add` return `void` and stdlib/collection mutation places

Status: accepted
Raised by: 043 (MQ-12)
Feature: spec/stdlib/vector.md, spec/stdlib/map.md, spec/stdlib/set.md, spec/features/vectors.md, spec/features/maps.md

## Question
Should `vec:push` and `vec:add` return `void` (retiring `VECLIB-6` chaining) under value semantics, and which standard-library methods and collection operations require a mutable receiver place?

## Evidence
- Spec & Decisions: `VECLIB-5`–`VECLIB-8`, `MAPLIB-6`, `SETLIB-3`, `SETLIB-4`, `VEC-4`, `MAP-5`, `MAP-6`, `D-175`.
- Design `043` (§7, `MQ-12`): under value semantics (`D-177`), returning `[]T` by value from `vec:push(x)` produces a temporary rvalue copy, breaking `v:push(a):push(b)` chaining (`2` conformance cases: `push-chain.pino`, `add.pino`; `0` occurrences in `main.pino` or `projects/pino-compiler/`). Across all `74` `IntrinsicId`s in `StdlibCatalog`, only `5` mutate their receiver: `VecPush` (`push`/`add`), `VecPop` (`pop`), `MapRemove` (`remove`), `SetAdd` (`add`), and `SetRemove` (`remove`).

## Options
1. Change `vec:push(x)` and `vec:add(x)` to return `void` (matching `set:add` in `SETLIB-3` and Swift's `Array.append`), retiring `VECLIB-6` chaining, and require a mutable receiver place for all 5 mutating stdlib methods and all collection index writes.
2. Special-case `v:push(a):push(b)` chained mutation in the checker and IR lowering.
3. Keep `push` returning a value only for `[]T`.

## Decision
Option 1, decided by the owner on 2026-11-21:
- **`vec:push(x)` and `vec:add(x)` return `void`:** matching `set:add` (`SETLIB-3`), `VECLIB-6` chaining is retired and chaining such as `v:push(a):push(b)` or using `push`/`add` as a value is a checker error (`VECLIB-6`, `VECLIB-E1`).
- **Mutable receiver place required:** the five mutating standard-library methods (`vec:push`/`add`, `vec:pop`, `map:remove`, `set:add`, `set:remove`; marked `mut fn` in `spec/stdlib/*.md`) and collection index writes (`v[i] = x`, `m[k] = v`, `m[k] op= v`) require a mutable receiver place (`VECLIB-5`, `VECLIB-7`, `VECLIB-8`, `MAPLIB-6`, `SETLIB-3`, `SETLIB-4`, `VEC-4`, `MAP-5`, `MAP-6`).
