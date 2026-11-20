# D-059: Reading a vector element out of range

Status: accepted
Raised by: 007
Feature: spec/features/vectors.md

## Question
What must `v[i]` do when `i` is negative or not below `v:len`: fail at run time on every engine, or is it unspecified?

## Evidence
- Reference: §4 "Vector Validation & Safety Semantics" (`LANGUAGE_REFERENCE.md:300-314`) covers only the construction parameters (`len`, `cap`). Indexing is "0-indexed" (`LANGUAGE_REFERENCE.md:295`); out-of-range access is not mentioned. `pop()` on an empty vector is documented as returning `null` (`LANGUAGE_REFERENCE.md:1167`), which suggests a forgiving runtime, but that is a different operation.
- Tests: none reads or writes out of range.
- Implementation: the tree-walk evaluator raises `RUNTIME ERROR: Index <i> out of range for vector of size <n>.` for reads and writes (`pino-csharp/Evaluator/Evaluator.Expression.cs:121`, `:183`, `:358`).

Snippet:

```pino
val numbers = [1, 2, 3]
println(numbers[5])
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `RUNTIME ERROR: Index 5 out of range for vector of size 3.` |
| VM | `null` (no error) |
| C | `2327` (garbage read, no bounds check; the value varies) |

## Options
1. Specify a run-time error (as tree-walk does) and fix the VM and the C backend; add a `.err` case.
2. Specify `null`/default values (consistent with `pop()` on an empty vector): change tree-walk and C.
3. Declare it unspecified and keep the spec silent.

## Decision
Option 1, decided by the owner on 2026-11-19: `v[i]` with `i < 0` or `i >= v:len` panics at run time on every engine. Consequence: `spec/features/vectors.md` adds `VEC-E4`, and VM/C silent out-of-range reads are recorded as known failures.
