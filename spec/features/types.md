# Primitive types and conversions

Status: tree-walk 8/8 | VM 8/8 | C 5/8   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `type`.
The primitive types are `bool`, `int`, `float`, `rune` and `string`. Values of these types are written as literals (see `lexical.md`) and combined with arithmetic, comparison and boolean operators. The builtin `type(value)` returns the name of the type of a value. Types are inferred, and a variable declaration may state its type (VAR-10). The integer operators are `+`, `-`, `*`, `/`, `//` (floor division) and `%` (floored modulo); `void` is a writable type in the positions listed by TYPE-17.

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

- TYPE-13: `in` and `not in` bind at the relational comparison level (`<`, `<=`, `>`, `>=`), while `is` and `is not` bind at the equality level (`==`, `!=`). [src: spec/decisions/D-141-assignment-and-relational-precedence.md; spec/decisions/D-178-remove-identity-operators.md]

- TYPE-14: `//` is floor division and `%` is the matching floored modulo, so `a == (a // b) * b + a % b` holds for `int` operands: `-7 // 2` is `-4`, `-7 % 2` is `1`, `7 // -2` is `-4` and `7 % -2` is `-1`. [src: spec/decisions/D-164-floor-division-and-modulo.md]
- TYPE-15: `//` on `float` operands returns the floored `float`, so `7.0 // 2` is `3.0`, and with one `int` and one `float` operand the `int` widens to `float` as in TYPE-9. [src: spec/decisions/D-164-floor-division-and-modulo.md; spec/decisions/D-019-int-float-mixing-and-division.md]
- TYPE-16: `//` and `%` bind at the same level as `*` and `/` and group left to right, so `2 + 7 // 2 * 3` is `11`. [src: spec/decisions/D-164-floor-division-and-modulo.md]
- TYPE-17: `void` may be written as the return type of a function, as the return type of a function type (`fn (int) -> void`) and as the success type of `Result` (`Result[void, E]`, a function that can only fail). [src: spec/decisions/D-162-void-as-a-writable-type.md]
- TYPE-18: `%` on `float` operands, or on a `float` and an `int` (the `int` widens as in TYPE-9), is the floored modulo, so `a == (a // b) * b + a % b` holds for floats too: `-7.5 % 2` is `0.5` and `7.5 % -2` is `-0.5`. [src: spec/decisions/D-171-float-modulo.md; spec/decisions/D-164-floor-division-and-modulo.md]
- TYPE-19: An `int` widens to `float` in an annotated declaration (`val x float = 1` holds `1.0`), as an argument for a `float` parameter, and as the value of a `return` in a function declared to return `float`. [src: spec/decisions/D-172-int-to-float-widening-positions.md; spec/decisions/D-012-explicit-type-annotation-syntax.md; spec/decisions/D-019-int-float-mixing-and-division.md]

## Errors
- TYPE-E1: A division or modulo (`/`, `//` or `%`) whose divisor is a compile-time constant zero is rejected by the checker before execution with a message containing "Division by Zero"; a divisor is a compile-time constant when it is a literal (`0` or `0.0`), constant arithmetic or a unary operation on constants, or an immutable `val` initialized with a constant expression, so `val z = 0` followed by `1 / z` is rejected, while a `var`, a parameter and a call result are never constants. [src: spec/decisions/D-166-constant-diagnostics.md]
- TYPE-E2: `void` written as the type of a parameter, a variable, a field or a collection element is rejected by the checker with a message containing "void". [src: spec/decisions/D-162-void-as-a-writable-type.md]
- TYPE-E3: A `float` is never implicitly narrowed to `int`: a `float` initializer for an annotated `int` variable (`val n int = 1.5`), a `float` argument for an `int` parameter and a `float` returned from a function declared to return `int` are rejected by the checker with a message containing "float". [src: spec/decisions/D-172-int-to-float-widening-positions.md]
- TYPE-E4: Division or modulo by zero at run time (`/`, `//` or `%`, on `int` or `float` operands) panics: it ends the program with exit code 101 and a message containing "divide by zero". [src: spec/decisions/D-170-runtime-division-by-zero.md; ERR-3; ERR-5]
- TYPE-E5: Declaring a user `struct`, `enum`, `union` or `interface` named `Option` or `Result` is rejected by the checker with `PN3002` (`'Option' is a built-in type and can't be declared again.`, help `choose another name`), and using either name as a `@@Generic` type parameter name is rejected per `GEN-E4`; they are reserved exactly like `Pool` and `Handle` (`POOL-E5`). [src: spec/decisions/D-185-pool-and-handle.md]
- TYPE-E6: `INT64_MIN // -1` and `INT64_MIN % -1` (where `INT64_MIN` is `-9223372036854775808`, computed at run time) panic: they end the program with exit code 101 and a message containing "Integer division overflowed" (`Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.`). Other integer overflow stays unspecified until sized integer types are decided (`OQ-6`). [src: spec/decisions/D-186-print-forms-and-ir-prereqs.md; ERR-3; ERR-5]

## Examples
Conformance cases: `conformance/types/`.

## Open questions
None.
