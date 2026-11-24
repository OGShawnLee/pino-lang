# Pino Lang — Agent Guide

Pino is a statically typed, expression-oriented systems and data-science language implemented in C# (.NET 10.0) with a tree-walk evaluator, bytecode VM, C99 transpiler (TCC + Boehm GC), and a clean LSP-ready rewrite (`pino-csharp-re/`), alongside a hand-written self-hosting compiler (`projects/pino-compiler/`).

## Read First
- **[.routing/CONTEXT.md](../.routing/CONTEXT.md)** *(read first)*: repo map, ground rules, stack, and agent roles.
- **[spec/SPEC.md](../spec/SPEC.md)**: normative language specification, rule IDs, grammar, and decisions (backed by `conformance/`).

## Ticket Workflow
1. Router tracks state in `.routing/PROGRESS.md` and writes approved tickets in `.tickets/<id>-<slug>/` (`task.md` + `spec.md`) citing spec rule IDs.
2. Workers execute one ticket at a time strictly within `task.md` scope, verify every `spec.md` check, write `changelog.md`, and make one Conventional Commit.
3. Router re-verifies all `spec.md` checks, updates `.routing/PROGRESS.md`, and regenerates `conformance/STATUS.md` when compiler behavior or conformance cases change.

## Windows / PowerShell
- Chain commands with `;` (never `&&`).
- If `pino-csharp.exe` is locked during build or test runs, run `Stop-Process -Name pino-csharp -Force`.
- Run the test suite (including conformance and spec-lint tests) with `dotnet test pino-csharp-re/Pino.sln`.
