# Primitive types and conversions

Status: tree-walk 8/8 | VM 8/8 | C 5/8   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `type`.
The primitive types are `bool`, `int`, `float`, `rune` and `string`. Values of these types are written as literals (see `lexical.md`) and combined with arithmetic, comparison and boolean operators. The builtin `type(value)` returns the name of the type of a value. Types are inferred; no explicit annotation syntax exists today (D-012).

## Rules
- TYPE-1: The primitive types are `bool`, `int`, `float`, `rune` and `string`, and `type(v)` returns the name of the type of `v` as a string: `"bool"`, `"int"`, `"float"`, `"rune"` or `"string"`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LANGUAGE_REFERENCE.md §9 "`type(val)`"; StdlibTests.TestTypeFunction; ArithmeticTests.TestRuneConversionsAndUtilities]
- TYPE-2: `int` is a 64-bit signed integer, so `4_000_000_000 + 4_000_000_000` is `8000000000`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"]
- TYPE-3: The binary operators `+`, `-`, `*` and `/` on integers follow the usual precedence (`*` and `/` before `+` and `-`) and parentheses group, so `2 + 3 * 4` is `14`, `(2 + 3) * 4` is `20` and `10 / 2 - 1` is `4`. [src: ArithmeticTests.TestBasicIntegerArithmetic]
- TYPE-4: The unary `-` negates an `int` or `float` and binds tighter than `*` and `+`, so `-2 * 3` is `-6`, `-2 + 3` is `1` and `-(2 + 3)` is `-5`. [src: ArithmeticTests.TestUnaryOperators]
- TYPE-5: `bool` values combine with `and`, `or` and `not` and compare with `==` and `!=`. [src: ArithmeticTests.TestBooleanLogic]
- TYPE-6: `and` and `or` evaluate their right operand only when the left operand does not already decide the result. [src: ArithmeticTests.TestBooleanShortCircuit]
- TYPE-7: `not` binds tighter than `and`, so `not true and false` is `false` while `not (true and false)` is `true`. [src: ArithmeticTests.TestUnaryOperators]

## Errors
None documented.

## Examples
Conformance cases: `conformance/types/`.

## Open questions
- D-012: How are explicit type annotations written (`val x: int = 10`, `val x int = 10`)? Today neither form parses.
- D-013: The reference lists `null` as a primitive type, but the `null` literal is not recognized by any engine and `Option[T]` is the documented way to model absence (ticket 008 owns the `Option` side).
- D-015: `int(v)` and `float(v)` are documented to return `int` and `float`, but every engine returns a `Result` (`int("123")` prints `Result::Success(123)`).
- D-019: `int` and `float` mix freely (`1 + 2.5`, `var i = 1` then `i = 2.5`), and `5 / 2` is `2`; the reference is silent on numeric promotion and integer division.
- D-022: `println` prints booleans as `True`/`False` and the float `1.0` as `1`, while the reference writes `true`/`false` and `42.0`.
