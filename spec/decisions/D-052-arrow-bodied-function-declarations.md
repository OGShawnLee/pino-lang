# D-052: Arrow bodies on function declarations

Status: accepted
Raised by: 007
Feature: spec/features/functions.md

## Question
May a named function declaration use an arrow body (`fn name(params) Type => expr`), as the reference's interface example shows, or is `=>` only for lambdas?

## Evidence
- Reference: the interface example declares struct methods with an arrow body: `fn make_sound() string => "Woof"` (`LANGUAGE_REFERENCE.md:575`, `:579`). §10 uses `=>` only on lambdas (`LANGUAGE_REFERENCE.md:1188`).
- Tests: no test declares a named function with `=>`.
- Implementation: `ParseFunctionDeclaration` always ends with `ParseBlock` (`pino-csharp/Parser/Parser.Statement.cs:296`), so `=>` after the signature fails. `ParseFunctionLambda` is the only place that accepts `=>` after `fn` (`pino-csharp/Parser/Parser.Expression.cs:425`). Methods go through the same `ParseFunctionDeclaration` (`pino-csharp/Parser/Parser.Statement.cs:788`).

Snippets:

```pino
fn double(n int) int => n * 2
println(double(21))
```

```pino
struct Dog {
  fn make_sound() string => "Woof"
}
```

| Engine | Actual output (both snippets) |
| :--- | :--- |
| tree-walk | `PARSER: Expected '{'` |
| VM | `PARSER: Expected '{'` |
| C | `PARSER: Expected '{'` (the parser is shared by all engines) |

## Options
1. Accept `=>` on declarations (functions and methods): parser change, then rules and cases for `fn name(...) [Type] => expr`.
2. Keep declarations block-only and correct the reference's interface example.

## Decision
Option 2: named functions and methods have block bodies only (`{ ... }`); there is no `fn name(...) T -> expr`, while lambdas keep arrow bodies, decided by the owner on 2026-11-19. Consequence: named functions and methods must use `{ ... }` bodies (D-074 is a duplicate resolved by this decision).
