# Vectors

Status: tree-walk 13/15 | VM 3/15 | C 12/15   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `vec`.
Vectors are dynamic arrays whose elements all have one type. A vector is created from a literal `[a, b, c]`, from a type with no elements `[]T`, or from a type with an initialization block `[]T { cap: n }` or `[]T { len: n, init: expr, cap: m }`. Elements are read and written with `v[i]`. The methods of a vector are in `spec/stdlib/vector.md`.

## Rules
- VEC-1: `[a, b, c]` creates a vector whose element type is inferred from its elements. [src: LANGUAGE_REFERENCE.md §4 "Vectors (Arrays)"]
- VEC-2: The elements of a vector literal may be separated by whitespace or newlines instead of commas. [src: LANGUAGE_REFERENCE.md §1 "Syntax Guidelines"]
- VEC-3: `v[i]` reads the element at zero-based index `i`. [src: LANGUAGE_REFERENCE.md §4 "Vectors (Arrays)"]
- VEC-4: `v[i] = x` replaces the element at index `i` and requires `v` to be a mutable place. [src: LANGUAGE_REFERENCE.md §4 "Vectors (Arrays)"; spec/decisions/D-181-vector-push-void-and-stdlib-mutation.md]
- VEC-5: `[]T` creates an empty vector of element type `T`, without curly braces. [src: LANGUAGE_REFERENCE.md §4 "Vectors (Arrays)"; DataStructureTests.TestVectorEmptyInitialization]
- VEC-6: The element type of `[]T` may itself be a vector, a map or a function type, as in `[][]int`, `[]map[string, int]` and `[]fn (int) -> int`. [src: DataStructureTests.TestVectorEmptyInitializationComplex; spec/decisions/D-101-function-type-syntax.md]
- VEC-7: `[]T { cap: n }` creates an empty vector of length 0 whose capacity is `n`. [src: LANGUAGE_REFERENCE.md §4 "Vectors (Arrays)"; DataStructureTests.TestVectorCapacityInitialization]
- VEC-8: `[]T { len: n, init: expr }` creates a vector of length `n` whose element `i` is the value of `expr` with the implicit constant `it` bound to `i`. [src: LANGUAGE_REFERENCE.md §4 "Vectors (Arrays)"]
- VEC-9: `[]T { len: n, init: expr, cap: m }` creates the same vector as VEC-8 with capacity `m`. [src: LANGUAGE_REFERENCE.md §4 "Vectors (Arrays)"]
- VEC-10: `x in v` is true when `v` contains an element equal to `x`, and `x not in v` is its negation. [src: DataStructureTests.TestMapAndVectorInNotInOperators; StdlibTests.TestInOperatorVector]
- VEC-11: When `len` is computed at run time and is negative, the vector gets length 0. [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"; DataStructureTests.TestVectorRuntimeSafeClamping]
- VEC-12: When `cap` is computed at run time and is smaller than `len`, the capacity is clamped to `len`. [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"]
- VEC-13: Every dynamically sized vector gets a capacity of at least 4. [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"; DataStructureTests.TestVectorRuntimeSafeClamping]
- VEC-14: Clamping a dynamic length or capacity does not terminate the program. [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"]
- VEC-15: Clamping a dynamic length or capacity writes a `[WARNING]` message describing the adjusted bounds to standard error and nothing to standard output. [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"]
- VEC-16: `println` of an integer vector writes its elements in order between square brackets, separated by a comma and a space, for example `[1, 2, 3]`. [src: LANGUAGE_REFERENCE.md §10 "Vector Methods" (`map`, `filter`)]
- VEC-17: A line break before `[` ends the expression, so a `[` at the start of a new line begins a new statement (a vector literal) rather than indexing the previous expression; indexing must be on the same line as its operand. [src: spec/decisions/D-058-bracket-starts-line-after-expression.md]

- VEC-18: A vector literal whose elements mix `int` and `float` has the type `[]float`, so `[1, 2.5]` holds the floats `1.0` and `2.5`. [src: spec/decisions/D-168-literal-widening.md; spec/decisions/D-019-int-float-mixing-and-division.md]
- VEC-19: Vectors have value semantics with copy-on-write (CoW): assignment and non-`mut` argument passing produce logically independent vectors, and mutating a caller's vector requires a `mut` parameter and call-site `mut`. [src: spec/decisions/D-177-value-semantics-cow-and-mut.md]

## Errors
- VEC-E1: A literal negative `len`, such as `[]int { len: -5, init: 0 }`, is rejected before execution with a type check error whose message contains "cannot be negative". [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"; DataStructureTests.TestVectorCompileTimeValidation]
- VEC-E2: A literal negative `cap`, such as `[]int { cap: -10 }`, is rejected before execution with a type check error whose message contains "cannot be negative". [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"; DataStructureTests.TestVectorCompileTimeValidation]
- VEC-E3: A literal `cap` smaller than a literal `len`, such as `[]int { len: 20, init: 0, cap: 10 }`, is rejected before execution with a type check error whose message contains "cannot be less than". [src: LANGUAGE_REFERENCE.md §4 "Vector Validation & Safety Semantics"; DataStructureTests.TestVectorCompileTimeValidation]
- VEC-E4: Indexing a vector `v[i]` with `i < 0` or `i >= v:len` panics at run time. [src: spec/decisions/D-059-vector-index-out-of-range.md]

## Examples
Conformance cases: `conformance/vectors/`.

## Open questions
None.
