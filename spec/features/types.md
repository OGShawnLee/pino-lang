# Primitive types and conversions

Status: tree-walk 8/8 | VM 8/8 | C 5/8   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `type`.
The primitive types are `bool`, `int`, `float`, `rune` and `string`. Values of these types are written as literals (see `lexical.md`) and combined with arithmetic, comparison and boolean operators. The builtin `type(value)` returns the name of the type of a value. Types are inferred; no explicit annotation syntax exists today (D-012).

## Rules
- TYPE-1: The primitive types are `bool`, `int`, `float`, `rune` and `string`, and `type(v)` returns the name of the type of `v` as a string: `"bool"`, `"int"`, `"float"`, `"rune"` or `"string"`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"; LANGUAGE_REFERENCE.md §9 "`type(val)`"; StdlibTests.TestTypeFunction; ArithmeticTests.TestRuneConversionsAndUtilities]
- TYPE-2: `int` is a 64-bit signed integer, so `4_000_000_000 + 4_000_000_000` is `8000000000`. [src: LANGUAGE_REFERENCE.md §3 "Primitive Types"]
- TYPE-3: The binary operators `+`, `-`, `*` and `/` follow the usual precedence (`*` and `/` before `+` and `-`) and parentheses group, so `2 + 3 * 4` is `14`, `(2 + 3) * 4` is `20` and `10 / 2 - 1` is `4.0`. [src: ArithmeticTests.TestBasicIntegerArithmetic; spec/decisions/D-019-int-float-mixing-and-division.md]
- TYPE-4: The unary `-` negates an `int` or `float` and binds tighter than `*` and `+`, so `-2 * 3` is `-6`, `-2 + 3` is `1` and `-(2 + 3)` is `-5`. [src: ArithmeticTests.TestUnaryOperators]
- TYPE-5: `bool` values combine with `and`, `or` and `not` and compare with `==` and `!=`. [src: ArithmeticTests.TestBooleanLogic]
- TYPE-6: `and` and `or` evaluate their right operand only when the left operand does not already decide the result. [src: ArithmeticTests.TestBooleanShortCircuit]
- TYPE-7: `not` binds tighter than `and`, so `not true and false` is `false` while `not (true and false)` is `true`. [src: ArithmeticTests.TestUnaryOperators]
- TYPE-8: Postfix `[i]`, `:` member access and calls bind tighter than unary `-` and `not`, so `-arr[0]` is `-(arr[0])`. [src: spec/decisions/D-107-postfix-tighter-than-unary.md]
- TYPE-9: `int` widens implicitly to `float` in arithmetic and comparison (`1 + 2.5` is `3.5`; `1 == 1.0` is `true`), and `/` always returns a `float` (`5 / 2` is `2.5`). [src: spec/decisions/D-019-int-float-mixing-and-division.md]
- TYPE-10: `println` and `str` format `bool` values as `true`/`false` and keep a fractional part on whole `float` values (`1.0`, `42.0`). [src: spec/decisions/D-022-println-format-of-bool-and-float.md]
- TYPE-11: `int(v)` and `float(v)` return a plain `int` or `float` for numeric arguments and `Result[int, string]` or `Result[float, string]` for `string` arguments, selected statically from the argument type. [src: spec/decisions/D-015-int-float-return-result.md]
- TYPE-12: `null` is not a type or value of the language; absence is expressed only with `Option[T]`. [src: spec/decisions/D-013-null-primitive-versus-option.md]

- TYPE-13: `in` and `not in` bind at the relational comparison level (`<`, `<=`, `>`, `>=`), while `is` and `is not` bind at the equality level (`==`, `!=`, `===`, `!==`). [src: spec/decisions/D-141-assignment-and-relational-precedence.md]

## Errors
None documented.

## Examples
Conformance cases: `conformance/types/`.

## Open questions
- D-012: How are explicit type annotations written (`val x: int = 10`, `val x int = 10`)? Today neither form parses.
- D-019 (partial): How integer division is spelled remains open.
