# Loops

Status: tree-walk 20/21 | VM 13/21 | C 17/21   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `loop`.
Every loop is introduced by the keyword `for` and has a braced body. The head is empty (infinite loop), a single expression (a count `for N` or a boolean condition), or one or two variable names followed by `in` and an expression (`for x in coll`, `for i, x in coll`). `break` and `continue` are statements usable inside a loop body.

## Rules
- LOOP-1: `for { ... }` repeats its body until a `break` is executed. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"]
- LOOP-2: A `return` executed inside `for { ... }` ends the containing function and therefore the loop. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"]
- LOOP-3: `for N { ... }` executes its body N times with the implicit constant `it` bound to the zero-based index of the current iteration. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"]
- LOOP-4: `for x in N { ... }` with an integer N executes its body N times with `x` bound to 0, 1, ..., N-1 in order. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; ControlFlowTests.TestRangeLoopSingleVar]
- LOOP-5: `for x in v { ... }` over a vector executes its body once per element, in index order, with `x` bound as a deeply immutable loop variable to that element. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; DataStructureTests.TestVectorLoopSingleVar; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-6: `for x in s { ... }` over a string executes its body once per Unicode code point, in order, with `x` bound as an immutable `rune` loop variable. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; DataStructureTests.TestStringLoopSingleVar; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-7: `for i, x in c { ... }` over a vector or string binds `x` to the current element and `i` to its zero-based position, both as deeply immutable loop variables. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; DataStructureTests.TestVectorLoopDoubleVar; DataStructureTests.TestStringLoopDoubleVar; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-8: `for i, v in N { ... }` with an integer N binds both `i` and `v` as immutable loop variables to the current counter value 0, 1, ..., N-1. [src: ControlFlowTests.TestRangeLoopDoubleVar; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-9: `for v in m { ... }` over a map executes its body once per entry in insertion order with `v` bound as a deeply immutable loop variable to the entry's value. [src: spec/decisions/D-120-for-single-var-map-value.md; spec/decisions/D-004-map-iteration.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-10: `for k, v in m { ... }` over a map executes its body once per entry in insertion order with `k` bound to the key and `v` bound to the value, both as deeply immutable loop variables. [src: DataStructureTests.TestMapLoopDoubleVar; spec/decisions/D-004-map-iteration.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-11: `break` terminates the loop that contains it, and execution continues with the statement after the loop. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"]
- LOOP-12: `continue` skips the rest of the current iteration and proceeds to the next one, and in an indexed `for i, x in s` loop over a string the index still advances for the skipped element. [src: ControlFlowTests.TestStringLoopWithContinueIncrementsIndex]
- LOOP-13: `for cond { ... }` with a boolean expression `cond` evaluates `cond` before every iteration and executes the body while it is true. [src: ControlFlowTests.TestWhileLoopSimpleCondition; ControlFlowTests.TestWhileLoopBooleanVariable]
- LOOP-14: Loops may be nested, and each iteration of an outer loop runs an inner loop from its beginning. [src: ControlFlowTests.TestWhileLoopNested]
- LOOP-15: The comma between the two bindings in `for i x in coll { ... }` is optional and means the same as `for i, x in coll { ... }`. [src: spec/decisions/D-001-loop-header-commas.md]
- LOOP-17: `it` is declared in the scope of a `for N { ... }` body, so inside the body it always refers to the loop's index, also inside call arguments such as `println("$it")`, which are therefore not turned into implicit lambdas (FN-17); a lambda passed inside such a body is written explicitly. [src: spec/decisions/D-2-it-as-call-argument.md]
- LOOP-18: `for x in s { ... }` over a set executes its body once per element in insertion order (removing an element and inserting it again puts it at the end) with `x` bound as a deeply immutable loop variable to that element. [src: spec/decisions/D-175-set-library-api.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-19: `for x in p { ... }` over a `Pool[T]` walks the pool as it was when the loop began and executes its body once per live slot in increasing slot-index order (explicitly differing from the insertion order of maps in D-004 and sets in D-175 once slots are reused), with `x` bound as a deeply immutable loop variable to the slot's value. [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-177-value-semantics-cow-and-mut.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- LOOP-20: `for h, x in p { ... }` over a `Pool[T]` walks the pool as it was when the loop began and executes its body once per live slot in increasing slot-index order (explicitly differing from the insertion order of maps in D-004 and sets in D-175 once slots are reused), with `h` bound to the slot's `Handle[T]` and `x` bound to its value (the last name is always the element per D-120), both as deeply immutable loop variables. [src: spec/decisions/D-185-pool-and-handle.md; spec/decisions/D-120-for-single-var-map-value.md; spec/decisions/D-177-value-semantics-cow-and-mut.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]

- LOOP-16: `for false { ... }` is a checker warning because its body can never run, and warnings never change the exit status. [src: spec/decisions/D-166-constant-diagnostics.md] [no-case: warnings are not observable by the conformance runner]

## Errors
- LOOP-E1: A `break` or `continue` that is not inside a loop in the same function (including inside a lambda or nested function declared inside an outer loop) is rejected by the checker with a message containing "outside of a loop". [src: spec/decisions/D-174-break-continue-outside-loop.md]
- LOOP-E2: Reassigning or mutating the interior of a loop variable declared by `for x in coll` or `for i, x in coll` is rejected by the checker. [src: spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]

## Examples
Conformance cases: `conformance/loops/`.

## Open questions
None.
