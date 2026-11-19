# D-072: Two generic syntaxes (`@generic[T]` decorator and inline `[T]`)

Status: open
Raised by: 008
Feature: spec/features/generics.md, spec/features/unions.md

## Question
Keep both spellings for declaring type parameters, the `@generic[T]` decorator and inline brackets after the name, or pick one?

## Evidence
- Reference: §7 "Generic Unions" shows both for unions: `@generic[T, E] union Result { ... }` and `union Option[T] { ... }` ("You can declare generic unions using either ... or inline brackets", `LANGUAGE_REFERENCE.md:646-660`). Function generics appear only with the decorator in `proposals/004-generics-evolution-roadmap.md`.
- Tests: struct generics use inline brackets (`struct Pair[Key Value]`, `CheckerTests.TestStructGenericsExplicitInstantiation`, `pino-csharp.tests/Checker/CheckerTests.Generics.cs:9-22`); functions, methods, interfaces and bounded structs use the decorator.
- Implementation: the parser reads the decorator in `ParseGenericParamsList` (`pino-csharp/Parser/Parser.Statement.cs:7`, `:43-44`). Inline brackets are accepted only on `struct` and `union`; `fn` and `interface` reject them. The separator between parameters may be a comma or whitespace in both spellings (`[Key Value]` and `[A, B]`). Bounds (`T is Interface`) appear only in the decorator form in the sources.

Snippets and results (the same on tree-walk and C unless noted; the VM crashes on every generic struct with a NullReferenceException):

| Snippet | Result |
| :--- | :--- |
| `struct Pair[A, B] { ... }` with `Pair[int, int] { a: 1, b: 2 }` | accepted, prints `3` |
| `struct Pair[A B] { ... }` | accepted, prints `3` |
| `@generic[A B] struct Pair { ... }` | accepted, prints `3` |
| `@generic[A, B] struct Pair { ... }` | accepted, prints `3` |
| `fn ident[T](v T) T { ... }` | all engines: `PARSER: Expected '('` |
| `interface Box[T] { fn get() T }` | all engines: `PARSER: Expected '{' to start interface body` |
| `union Maybe[T] { ... }` | accepted (`conformance/unions/generic-inline.pino`) |

## Options
1. Keep both and document which declarations accept which spelling (today: inline on `struct` and `union`, decorator everywhere).
2. Decorator only: one uniform spelling and the only place bounds exist; the inline form would be removed (a breaking change for generic structs).
3. Inline only: needs new syntax for function, method and interface generics and for bounds.

## Decision
_Pending: owner decides._
