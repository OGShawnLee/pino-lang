# D-169: Match patterns on union variants

Status: accepted
Raised by: 028
Feature: spec/features/unions.md

## Question
How many fields must a `when Union::Variant(...)` pattern bind, and how are fields bound by position or by name?

## Evidence
- Audit 028 section 2.7 and section 7, question 7: the self-host silently allows fewer bindings than fields (`projects/pino-compiler/modules/Checker.pino:2177` rejects only `field_list:len < binding_list:len`) and rejects named labels (`Checker.pino:98-100`).
- Spec: UNION-3 binds payload values "in order"; UNION-4 restricts `when Union::Variant` without parentheses to variants without a payload.

## Options
1. Allow no bindings, a positional prefix and `@label` bindings; reject mixing them.
2. Require every field to be bound.

## Decision
Option 1, decided by the owner on 2026-11-20. For `union U { Pair(a int, b string) }`:
- **No bindings:** `when U::Pair` matches the variant and binds nothing.
- **Positional:** `when U::Pair(x, y)` binds fields by position under any names. A **prefix** is allowed: `when U::Pair(x)` binds only the first field.
- **Label:** `when U::Pair(@b)` binds field `b` by its name, and `when U::Pair(@b: label)` binds it under another name. Labels can bind any subset in any order and are valid only on variants with named fields. A label that is not a field of the variant is a checker error.
- **No mixing:** positional and label bindings cannot be mixed in one pattern (`when U::Pair(x, @b)` is an error).
