# Unions and `is`

Status: tree-walk 25/25 | VM 7/25 | C 23/25   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `union`.
A `union` declares tagged variants, each optionally carrying a payload of one or more types. A variant is created with `Union::Variant(args)` (or `Union::Variant` when it has no payload) and consumed with `match`/`when` patterns or the `is` / `is not` operators. Unions may be generic; generic syntax is described in `spec/features/generics.md`.

## Rules
- UNION-1: `union Name { Variant(T1, T2) Other Plain }` or `union Name { Variant(a T1, b T2) Other Plain }` declares a tagged union whose variants carry zero or more payload types or named payload fields, and the declaration may be prefixed with `pub`. [src: LANGUAGE_REFERENCE.md §7 "Unions (Sum Types / Tagged Unions)"; ParserUnionsTests.TestParseUnionDeclaration; spec/decisions/D-103-union-payload-forms.md]
- UNION-2: A variant is instantiated with `Union::Variant(args)`, or `Union::Variant` when it has no payload. [src: LANGUAGE_REFERENCE.md §7 "Unions (Sum Types / Tagged Unions)"]
- UNION-3: In `match`, `when Union::Variant(a, b)` selects the branch for that variant and binds the payload values by position to deeply immutable bindings `a`, `b` inside the branch, and a prefix such as `when Union::Variant(a)` binds only the first field. [src: LANGUAGE_REFERENCE.md §7 "Unions (Sum Types / Tagged Unions)"; UnionTests.TestBasicUnionMatchExecution; ParserUnionsTests.TestParseWhenPattern; spec/decisions/D-169-union-variant-match-patterns.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- UNION-4: `when Union::Variant` without parentheses matches that variant, with or without a payload, and binds nothing. [src: UnionTests.TestBasicUnionMatchExecution; spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-5: An `else` branch in a `match` over a union handles every variant that no `when` branch lists. [src: LANGUAGE_REFERENCE.md §7 "Unions (Sum Types / Tagged Unions)"; UnionTests.TestUnionMatchElseFallbackExecution]
- UNION-6: A union is made generic by the `@@Generic[T, ...]` decorator before the `union` keyword, and its type parameters can be used in variant payload types. [src: LANGUAGE_REFERENCE.md §7 "Generic Unions"; proposals/005-tagged-unions-and-patterns-roadmap.md; UnionTests.TestGenericUnionMatchExecution; spec/decisions/D-072-two-generic-syntaxes.md]
- UNION-7: Positional payload types in a union declaration are allowed only when every variant of that union is positional. [src: spec/decisions/D-103-union-payload-forms.md]
- UNION-8: The generic arguments of a union value are inferred from the payload, so `Option::Some(42)` has type `Option[int]` and is accepted where `Option[int]` is expected. [src: LANGUAGE_REFERENCE.md §7 "Monomorphization & Type Inference"; CheckerTests.TestGenericUnionMonomorphizationSuccess]
- UNION-9: When a variant constructor alone cannot determine the generic arguments (for example `Option::None` or `Result::Failure("x")`), they are taken from the expected type of the context, such as a parameter type or a function return type. [src: proposals/005-tagged-unions-and-patterns-roadmap.md; UnionTests.TestGenericUnionAssignmentAndArgDisambiguation; UnionTests.TestGenericResultDisambiguation]
- UNION-10: A generic union may have several type parameters, each used by a different variant. [src: LANGUAGE_REFERENCE.md §7 "Generic Unions"; UnionTests.TestMultiGenericUnionExpectedType]
- UNION-11: A generic union may refer to itself in a payload type (`Cons(T, List[T])`). [src: UnionTests.TestGenericRecursiveUnion]
- UNION-12: `==` and `!=` compare variants without payload: two values are equal exactly when they are the same variant. [src: LANGUAGE_REFERENCE.md §7 "Equality"]
- UNION-13: `==` on variants with a payload is true exactly when both values are the same variant and their payloads are equal. [src: LANGUAGE_REFERENCE.md §7 "Equality"]
- UNION-14: Union equality is recursive: a payload that is itself a union is compared with the same rules. [src: LANGUAGE_REFERENCE.md §7 "Equality"]
- UNION-15: `value is Union::Variant` is true when `value` holds that variant and `value is not Union::Variant` is its negation; both can be used in an `if` condition or as a value. [src: LANGUAGE_REFERENCE.md §7 "Variant Testing (`is` and `is not`)"; CheckerTests.TestUnionIsExpressionChecking; UnionTests.TestUnionIsExpressionExecution]
- UNION-16: `value is Union::Variant(name)` also binds the payload to a deeply immutable binding `name`, visible only inside the `if` block. [src: LANGUAGE_REFERENCE.md §7 "Variant Testing (`is` and `is not`)"; UnionTests.TestUnionIsExpressionExecution; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md]
- UNION-17: For a generic union the type arguments may be written explicitly in an `is` test (`Option[string]::Some(name)`). [src: LANGUAGE_REFERENCE.md §7 "Variant Testing (`is` and `is not`)"; CheckerTests.TestUnionIsExpressionChecking]
- UNION-18: An `is` test with a binding can be combined with further conditions using `and`, and the binding can be used in those conditions. [src: UnionTests.TestUnionIsExpressionWithSubsequentCondition]
- UNION-19: `is` works on a generic union whose type argument is a function's own type parameter. [src: UnionTests.TestGenericUnionIsExpressionExecution]

- UNION-20: Multiple positional payload types in a union variant must be separated by commas (`Pair(int, string)`), whereas named fields remain comma-optional, so `Variant(a B)` declares one named field `a` of type `B`. [src: spec/decisions/D-146-positional-union-payload-commas.md]

- UNION-21: `when Union::Variant(@field)` binds the named payload field `field` to a variable of the same name, and `when Union::Variant(@field: name)` binds it to `name`. [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-22: Label bindings may bind any subset of the named fields in any order, and are valid only on variants declared with named fields. [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-23: An `is` pattern binds positionally only: `value is Union::Variant(a)` binds a prefix of the payload (here the first field), `value is Union::Variant` binds nothing, and `@label` is not allowed in an `is` pattern (UNION-E14). [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-24: Union values have value semantics with copy-on-write (CoW), and union payloads and `when`/`is` pattern bindings are deeply immutable once constructed. [src: spec/decisions/D-177-value-semantics-cow-and-mut.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md] [no-case: pending conformance migration (ticket 050)]

## Errors
- UNION-E1: Constructing a variant with an argument of the wrong payload type is rejected with a message containing "for variant constructor". [src: CheckerTests.TestUnionConstructorArgumentTypeMismatch]
- UNION-E2: A `match` over a union that omits a variant and has no `else` is rejected with a message containing "is not exhaustive". [src: CheckerTests.TestUnionMatchMissingVariantThrows; proposals/005-tagged-unions-and-patterns-roadmap.md]
- UNION-E3: Passing a generic union value to a parameter of a different instantiation (for example an `Option[string]` to `Option[int]`) is rejected with a message containing "expected type 'Option[int]'". [src: CheckerTests.TestGenericUnionMonomorphizationMismatchThrows]
- UNION-E4: Constructing a variant whose payload conflicts with the expected instantiation (`Container::Value("x")` for `Container[int]`) is rejected with a message containing "expected type 'Container[int]'". [src: CheckerTests.TestUnionGenericAsParameterThrows]
- UNION-E5: `is` with a variant the union does not have is rejected with a message containing "has no variant". [src: CheckerTests.TestUnionIsExpressionErrors]
- UNION-E6: `is` on a value that is not a union or enum is rejected with a message containing "Cannot match union pattern". [src: CheckerTests.TestUnionIsExpressionErrors]
- UNION-E7: `is` with explicit generic arguments that differ from the value's type is rejected with a message containing "Cannot match union pattern". [src: LANGUAGE_REFERENCE.md §7 "Variant Testing (`is` and `is not`)"; CheckerTests.TestUnionIsExpressionErrors]
- UNION-E8: Mixing named payload fields (`Int(n int)`) and positional payload types (`Str(string)`) in the same union declaration is rejected as a compile error. [src: spec/decisions/D-103-union-payload-forms.md]

- UNION-E9: Mixing positional and label bindings in one pattern, such as `when U::Pair(x, @b)`, is rejected by the checker with a message containing "cannot mix". [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-E10: A label that is not a field of the variant, such as `when U::Pair(@c)`, is rejected by the checker with a message containing "is not a field". [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-E11: A label binding on a variant whose payload is positional, such as `when P::Pair(@a)` for `Pair(int, string)`, is rejected by the checker with a message containing "named fields". [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-E12: A `when` pattern with more positional bindings than the variant has fields, such as `when U::Pair(a, b, c)` for `Pair(int, string)`, is rejected by the checker with a message containing "too many bindings". [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-E13: A `when` pattern that binds the same label twice, such as `when U::Pair(@a, @a: x)`, is rejected by the checker with a message containing "duplicate label". [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-E14: A `@label` binding in an `is` pattern, such as `value is U::Pair(@a)`, is rejected as a parse error with a message containing "label". [src: spec/decisions/D-169-union-variant-match-patterns.md]
- UNION-E15: An `is` pattern with more positional bindings than the variant has fields, such as `value is U::Pair(a, b, c)` for `Pair(int, string)`, is rejected by the checker with a message containing "too many bindings". [src: spec/decisions/D-169-union-variant-match-patterns.md]

## Examples
Conformance cases: `conformance/unions/`. The predeclared `Option[T]` and `Result[T, E]` unions are specified in `spec/features/errors.md`.

## Open questions
None.
