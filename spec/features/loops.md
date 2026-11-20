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
- LOOP-5: `for x in v { ... }` over a vector executes its body once per element, in index order, with `x` bound to that element. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; DataStructureTests.TestVectorLoopSingleVar]
- LOOP-6: `for x in s { ... }` over a string executes its body once per Unicode code point, in order, with `x` bound to a `rune`. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; DataStructureTests.TestStringLoopSingleVar]
- LOOP-7: `for i, x in c { ... }` over a vector or string binds `x` to the current element and `i` to its zero-based position. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"; DataStructureTests.TestVectorLoopDoubleVar; DataStructureTests.TestStringLoopDoubleVar]
- LOOP-8: `for i, v in N { ... }` with an integer N binds both `i` and `v` to the current counter value 0, 1, ..., N-1. [src: ControlFlowTests.TestRangeLoopDoubleVar]
- LOOP-9: `for k in m { ... }` over a map executes its body once per entry with `k` bound to the entry's key; the iteration order is not specified. [src: DataStructureTests.TestMapLoopSingleVar]
- LOOP-10: `for k, v in m { ... }` over a map executes its body once per entry with `k` bound to the key and `v` bound to the value; the iteration order is not specified. [src: DataStructureTests.TestMapLoopDoubleVar]
- LOOP-11: `break` terminates the loop that contains it, and execution continues with the statement after the loop. [src: LANGUAGE_REFERENCE.md §5 "The Universal Loop (`for`)"]
- LOOP-12: `continue` skips the rest of the current iteration and proceeds to the next one, and in an indexed `for i, x in s` loop over a string the index still advances for the skipped element. [src: ControlFlowTests.TestStringLoopWithContinueIncrementsIndex]
- LOOP-13: `for cond { ... }` with a boolean expression `cond` evaluates `cond` before every iteration and executes the body while it is true. [src: ControlFlowTests.TestWhileLoopSimpleCondition; ControlFlowTests.TestWhileLoopBooleanVariable]
- LOOP-14: Loops may be nested, and each iteration of an outer loop runs an inner loop from its beginning. [src: ControlFlowTests.TestWhileLoopNested]
- LOOP-15: The comma between the two bindings in `for i x in coll { ... }` is optional and means the same as `for i, x in coll { ... }`. [src: spec/decisions/D-001-loop-header-commas.md]

## Errors
None documented.

## Examples
Conformance cases: `conformance/loops/`.

## Open questions
- D-002: Should `it` inside `for N { ... }` stay a loop variable when it is (part of) a call argument such as `println("$it")`? Today the parser turns such an argument into an implicit lambda.
- D-003: Should the reference document the condition form `for cond { ... }` (LOOP-13)? It is asserted by tests but absent from the reference's "4 syntactic variants".
- D-004: Should the reference document iteration over maps (LOOP-9, LOOP-10) and fix or leave open the iteration order?
- D-005: How must identifiers that are C keywords (for example `char`, the reference's own loop variable) behave on the C backend?
