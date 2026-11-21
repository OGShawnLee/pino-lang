# D-165: Declaration hoisting

Status: accepted
Raised by: 028
Feature: spec/features/functions.md

## Question
May `fn`, `struct`, `enum`, `union` and `interface` be used before their textual position, at module level and inside nested blocks?

## Evidence
- Audit 028 section 2.2: the self-host hoists these five declaration kinds at every block entry, top level, function bodies, `if`/`else`, `match` arms, `for` bodies and standalone blocks (`projects/pino-compiler/modules/Checker.pino:1120-1196`, called at `1213`; type pass `3517-3556`, called at `3559`). `val`/`var` are not hoisted.
- Audit 028 section 7, question 3 asked whether hoisting applies inside nested blocks or only at module level.
- Spec: FN-14 says only that a nested `fn` is callable from its body.

## Options
1. Hoist the five declaration kinds at module level and inside nested blocks.
2. Hoist at module level only.

## Decision
Option 1, decided by the owner on 2026-11-20: declarations (`fn`, `struct`, `enum`, `union`, `interface`) are hoisted at module level and inside nested blocks, so they can be used before their textual position in the same scope. `val` and `var` are not hoisted.

**Extended on 2026-11-20 (ticket 031, OQ-16), Option 2, decided by the owner:** top-level `val`s follow strict textual order in both Script Mode and Program Mode. A function (or a method) never sees a top-level `val` declared below it, even when the function is only called after the `val` has been initialized; reading it is a checker error. Hoisting covers declarations only, never `val` or `var`. Normative rules: VAR-7, VAR-E8 and MAIN-3.
