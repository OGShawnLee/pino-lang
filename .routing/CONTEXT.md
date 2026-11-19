# CONTEXT

Central routing directory for all agents. Read this first.

## Files
- [PROGRESS.md](PROGRESS.md): completed work, active tickets, next steps, and tech debt.
- [LANGUAGE_REFERENCE.md](../LANGUAGE_REFERENCE.md): canonical syntax, grammar, types, and standard methods.
- [ARCHITECTURE.md](../ARCHITECTURE.md): compiler frontend & VM overview *(outdated on transpiler target — [TranspilerC.cs](../pino-csharp/Compiler/TranspilerC.cs) and [runtime/](../runtime/) are authoritative)*.
- [proposals/](../proposals/): RFCs and design proposals (FFI, zero-copy strings, tagged unions).

## Project
- **Pino Lang:** statically typed, expression-oriented language bridging systems programming and data science.
- **Dual-Engine Execution:**
  1. *Dev/Scripting:* .NET 10 single-file CLI running Tree-Walk Evaluator and stack-based Bytecode VM.
  2. *Production/Native:* Transpiles AST to standard C ([TranspilerC.cs](../pino-csharp/Compiler/TranspilerC.cs)), compiled via TCC + Boehm GC into ~3 KB standalone binaries.
- **Self-Hosting:** [`projects/pino-compiler/`](../projects/pino-compiler/) is Pino's hand-written compiler in Pino.

## Repo Map
- **Active compiler & runtime:** [`pino-csharp/`](../pino-csharp/) (canonical C# compiler/CLI), [`pino-csharp.tests/`](../pino-csharp.tests/) (xUnit suite), [`runtime/`](../runtime/) (`runtime.c`/`runtime.h`/`re.c`), [`tooling/tcc/`](../tooling/tcc/) (bundled TCC).
- **Active self-host:** [`projects/pino-compiler/`](../projects/pino-compiler/).
- **Editor tooling:** [`vscode-pino/`](../vscode-pino/), [`zed-pino/`](../zed-pino/).
- **Off-scope unless asked:** [`pino-site-next/`](../pino-site-next/), [`pino.site-legacy/`](../pino.site-legacy/), [`pino.games/`](../pino.games/), [`projects/c-compiler/`](../projects/c-compiler/), [`benchmarks/`](../benchmarks/).

## Stack
- **Compiler & Tooling:** C# (.NET 10.0 SDK), xUnit.
- **Native Runtime:** Tiny C Compiler (TCC), Boehm GC (`gc.h`).
- **Web Playground:** C# compiled to WebAssembly (`WASMBridge.cs`).

## Ground rules
- **Quality Gate:** Compiler/runtime tickets must pass `dotnet test` in `pino-csharp.tests/` with 0 failures.
- **TCC Constraint:** Transpiled C must compile cleanly with TCC (`--c` flag) using only `runtime/` and Boehm GC.
- **Self-Host Rule:** Never rewrite or bulk-generate `projects/pino-compiler/` unasked; the user writes it by hand. Assist only with scoped fixes, tests, or explicitly requested tasks.
- **Grammar Invariants:**
  - Unified `for` keyword for all loops (never `loop` or `while`).
  - Empty vectors are `[]Type` (e.g. `[]int`), never `[]int {}` (initializers like `{ cap: 100 }` are valid).
  - Commas are optional in lists, collections, parameters, and struct literals.
  - Member access uses `:`; static/namespace access uses `::`.
  - Error handling uses suffix `?` and `or { yield fallback }`.
  - No type-cast variable syntax (`val x int = 12`); never name identifiers `val`.
- **Windows / PowerShell:** Chain commands with `;` (not `&&`). If `pino-csharp.exe` is locked, run `Stop-Process -Name pino-csharp -Force`.
- **Commits:** English, Conventional Commits (`feat`, `fix`, `refactor`, `test`, `docs`, `progress`), with detailed bodies.

## Agent roles
- **Router:** Gemini 4 Carbon / Argon / Neon or Claude Opus 5.5. Tracks state in [PROGRESS.md](PROGRESS.md), answers architecture questions, writes tickets in `.tickets/`, and verifies deliverables. Never writes compiler code directly. Instructions: [router skill](../.claude/skills/router/SKILL.md).
- **Implementer Workers:** Gemini 4 Hydrogen (Medium) or Claude Sonnet 5.5 (High). Execute one ticket at a time within strict `task.md` scope. Ticket format: [ticket skill](../.claude/skills/ticket/SKILL.md).
- **Reviewer:** The user. Approves plans, reviews diffs, and owns architectural decisions.
