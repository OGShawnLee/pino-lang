# D-074: Expression-bodied methods and functions (`fn f() T => expr`)

Status: accepted
Raised by: 008
Feature: spec/features/interfaces.md, spec/features/structs.md

## Question
Should a named function or method be allowed to have an expression body introduced by `=>`, as the reference's interface example writes it, or must every named function body be a block?

Note: named functions belong to ticket 007 (`spec/features/functions.md`); the evidence appeared while migrating the interface example, so the decision is opened here and may be merged with a 007 decision.

## Evidence
- Reference: the structural-interface example declares `fn make_sound() string => "Woof"` and `fn make_sound() string => "Meow"` inside structs (`LANGUAGE_REFERENCE.md:574-580`). `=>` is otherwise documented for lambdas (`fn(n int) => n * 2`) and single-expression `match` arms (`LANGUAGE_REFERENCE.md:441-452`).
- Tests: no xUnit test declares a named function or method with `=>`; lambdas with `=>` are used throughout.
- Implementation: `ParseFunctionDeclaration` expects a block after the signature (`pino-csharp/Parser/Parser.Statement.cs:255`, `ParseBlock` at `:727-731`), so the reference example does not parse.

```pino
fn f() string => "x"
println(f())
```

and the same inside a struct:

```pino
struct Dog {
  fn make_sound() string => "Woof"
}
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `PARSER: Expected '{'` for both snippets |
| VM | `PARSER: Expected '{'` for both snippets |
| C | `PARSER: Expected '{'` for both snippets |

`conformance/interfaces/structural-dispatch.pino` therefore uses block bodies with the same meaning.

## Options
1. Accept `fn name(params) T => expr` for functions and methods (parser change); the reference stays as written.
2. Keep the parser and change the reference examples to block bodies.

## Decision
Duplicate resolved by D-052 (Option 2: named functions and methods have block bodies only), decided by the owner on 2026-11-19. Consequence: expression-bodied named functions and methods are not part of the language; see [D-052](D-052-arrow-bodied-function-declarations.md).
