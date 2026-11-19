# CONTEXT

Central routing directory for all agents. Read this first.

## Files
- [spec/SPEC.md](../spec/SPEC.md): normative language spec: features, rule IDs, grammar, decisions.
- [conformance/](../conformance/): executable proof of the spec across engines.
- [PROGRESS.md](PROGRESS.md): completed work, active tickets, next steps, and tech debt.
- [LANGUAGE_REFERENCE.md](../LANGUAGE_REFERENCE.md): human-facing guide; defer to `spec/` on conflicts.
- [ARCHITECTURE.md](../ARCHITECTURE.md): compiler frontend & VM overview *(outdated on transpiler target — [TranspilerC.cs](../pino-csharp/Compiler/TranspilerC.cs) and [runtime/](../runtime/) are authoritative)*.
- [proposals/](../proposals/): RFCs and design proposals (FFI, zero-copy strings, tagged unions).

## Project
- **Pino Lang:** statically typed, expression-oriented language bridging systems programming and data science.
- **Dual-Engine Execution:**
  1. *Dev/Scripting:* .NET 10 single-file CLI running Tree-Walk Evaluator and stack-based Bytecode VM.
  2. *Production/Native:* Transpiles AST to standard C ([TranspilerC.cs](../pino-csharp/Compiler/TranspilerC.cs)), compiled via TCC + Boehm GC into ~3 KB standalone binaries.
- **Self-Hosting:** [`projects/pino-compiler/`](../projects/pino-compiler/) is Pino's hand-written compiler in Pino.

## Repo Map
- **Active compiler & runtime:** [`pino-csharp/`](../pino-csharp/) (legacy C# compiler/CLI), [`pino-csharp-re/`](../pino-csharp-re/) (clean LSP-ready C# rewrite), [`pino-csharp.tests/`](../pino-csharp.tests/) (xUnit suite), [`runtime/`](../runtime/) (`runtime.c`/`runtime.h`/`re.c`), [`tooling/tcc/`](../tooling/tcc/) (bundled TCC).
- **Active self-host:** [`projects/pino-compiler/`](../projects/pino-compiler/).
- **Editor tooling:** [`vscode-pino/`](../vscode-pino/), [`zed-pino/`](../zed-pino/).
- **Off-scope unless asked:** [`pino-site-next/`](../pino-site-next/), [`pino.site-legacy/`](../pino.site-legacy/), [`pino.games/`](../pino.games/), [`projects/c-compiler/`](../projects/c-compiler/), [`benchmarks/`](../benchmarks/).

## Stack
- **Compiler & Tooling:** C# (.NET 10.0 SDK), xUnit.
- **Native Runtime:** Tiny C Compiler (TCC), Boehm GC (`gc.h`).
- **Web Playground:** C# compiled to WebAssembly (`WASMBridge.cs`).

## Ground rules
- **Quality Gate:** Compiler/runtime tickets must pass conformance and spec-lint tests (`dotnet test` in `pino-csharp.tests/` covers both) with 0 failures.
- **Spec & Rule IDs:** Tickets that change language behavior cite rule IDs (e.g. `LOOP-2`). Behavior with no rule ID requires a decision file first.
- **Decisions:** Only the owner changes a decision's `Status`.
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
- **C# Rewrite (`pino-csharp-re/`):** Use uppercase acronyms in C# symbols (e.g. `EOF`, `LSP`, `AST`, `IR`, `UTF16`), `net10.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, error-tolerant diagnostics (never throw on invalid user code), and follow [`.editorconfig`](../.editorconfig) (2-space indent, same-line `{`, LF; checked via `dotnet format pino-csharp-re/Pino.sln --verify-no-changes`).
- **Multi-line Strings:** Use triple double-quotes (`"""..."""`), keeping single `"..."` strings single-line for LSP error recovery.
- **Commits:** English, Conventional Commits (`feat`, `fix`, `refactor`, `test`, `docs`, `progress`), with detailed bodies. One commit per ticket; integrate worker branches by rebase + fast-forward (no merge commits), resolving `spec/SPEC.md` row conflicts per ticket.

## Agent roles
- **Router:** Gemini 4 Carbon / Argon / Neon or Claude Opus 5.5. Tracks state in [PROGRESS.md](PROGRESS.md), answers architecture questions, writes tickets in `.tickets/`, and verifies deliverables. Never writes compiler code directly. Instructions: [router skill](../.claude/skills/router/SKILL.md).
- **Implementer Workers:** Gemini 4 Hydrogen (Medium) by default (owner: on par with Sonnet 5.5 High, faster and cheaper; clean results on 009/010), or Claude Sonnet 5.5 (High). Execute one ticket at a time within strict `task.md` scope. Ticket format: [ticket skill](../.claude/skills/ticket/SKILL.md).
- **Reviewer:** The user. Approves plans, reviews diffs, and owns architectural decisions.
