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

**Extended on 2026-11-20 (ticket 031, OQ-21, OQ-22 and OQ-23), Option 1 for each, decided by the owner:**
- More positional bindings than the variant has fields (`when U::Pair(a, b, c)`) is a checker error. Normative rule: UNION-E12.
- The same label bound twice (`when U::Pair(@a, @a: x)`) is a checker error. Normative rule: UNION-E13.
- `is` patterns stay positional-only: `x is U::Pair(a)` (a prefix) and `x is U::Pair` (no bindings) are allowed, and a `@label` in an `is` pattern is a parse error. Normative rules: UNION-23 and UNION-E14.

**Not decided, so no rule is written (logged for the owner):** whether more positional bindings than fields in an `is` pattern (`x is U::Pair(a, b, c)`) is also a checker error. UNION-E12 is written for `when` patterns only.
