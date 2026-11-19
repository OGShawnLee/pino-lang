# D-012: Explicit type annotation syntax for variables

Status: open
Raised by: 005
Feature: spec/features/variables.md

## Question
How does a program write an explicit type on a variable declaration: `val x: int = 10`, `val x int = 10`, or not at all?

## Evidence
- Reference: §2 and §3 declare every variable without a type (`val name = "Shawn Lee"`) and §3 says the checker relies on "automated type inference" (`LANGUAGE_REFERENCE.md:61-80`). Parameters and fields are written `name type` (`LANGUAGE_REFERENCE.md:215`). `.routing/PROGRESS.md` lists the variable annotation syntax as undecided.
- Tests: no test declares a variable with a type annotation; `CheckerTests` and `ScopingExecutionTests` only use inferred variables.
- Implementation: `ParseVariableDeclaration` reads the identifier and then requires `=` (`Parser.Statement.cs:246-247`), so any annotation is a parse error. The checker nevertheless reads an `expectedType` for declarations (`Checker.Statement.cs:16`).

Minimal snippets:

```pino
val x: int = 10
println(x)
```

```pino
val x int = 10
println(x)
```

| Engine | `val x: int = 10` | `val x int = 10` |
| :--- | :--- | :--- |
| tree-walk | `PARSER: Expected '=', got Operator: : (MemberAccess)` | `PARSER: Expected '=', got Identifier: int` |
| VM | same parse error (the parser is shared) | same parse error |
| C | `Error compiling program: PARSER: Expected '=', got Operator: : (MemberAccess)` | `Error compiling program: PARSER: Expected '=', got Identifier: int` |

## Options
1. Inference only (current behavior): no syntax to add; the spec states that variables have no annotation.
2. `val x: int = 10`: familiar from Kotlin, Rust and TypeScript; conflicts with `:` as the member-access operator.
3. `val x int = 10`: consistent with parameter and field syntax (`name type`); needs a parser change.

## Decision
_Pending: owner decides._
