# D-018: Redeclaring a name in the same scope

Status: open
Raised by: 005
Feature: spec/features/variables.md

## Question
Is a second declaration of an existing name in the same scope an error, a replacement, or allowed to change the type?

## Evidence
- Reference: §2 says a `var` holds values "of the same type" and a `val` "cannot be reassigned"; it does not mention redeclaration (`LANGUAGE_REFERENCE.md:61-77`). §5 and the scoping tests only describe shadowing in nested blocks.
- Tests: `CheckerTests.TestScopeShadowingInLocalBlocks` and `ScopingExecutionTests.TestScopeShadowingInLocalBlocksExecution` cover nested blocks only (`CheckerTests.Scoping.cs:63-76`, `ScopingExecutionTests.cs:30-48`).
- Implementation: the parser registers each declared name with `DeclareVariable` without a duplicate check (`Parser.Statement.cs:249-251`), and the tree-walk environment simply overwrites the binding.

Minimal snippet:

```pino
var a = 1
var a = "s"
println(a)
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `s` |
| VM | `s` |
| C | `TCC compilation failed: error: incompatible types for redefinition of 'a'` |

## Options
1. Reject a redeclaration in the same scope with a checker error: catches typos; matches the C backend's refusal, and nested-block shadowing still works.
2. Allow it and treat it as a new binding (shadowing in the same scope), as the tree-walk interpreter does: convenient for conversions such as `val n = int(text)`; the C backend must generate a fresh name.
3. Allow it only when the new type equals the old one: rejects the type-changing case only.

## Decision
_Pending: owner decides._
