---
name: ticket
description: Create a Pino Lang ticket (task.md and spec.md) in .tickets/. Use when a task must be handed to a worker agent. The ticket must be unambiguous with executable verification checks.
---

# Ticket

A ticket is a folder in `.tickets/` with two files: `task.md` (what to do) and `spec.md` (how to verify it). Workers diverge when anything is ambiguous. Remove all guesswork before handing it out.

## Before writing: ask the user

If anything is unclear, **ask the user before writing the ticket**. Ask when:
- The language behavior or grammar edge case is not defined in [spec/SPEC.md](../../../spec/SPEC.md) and its feature files.
- Which execution engines are in scope (`Evaluator`, `VM`, `TranspilerC`, `Self-Host`) is uncertain.
- File boundaries (`May edit` / `Must not touch`) are uncertain.
- The task is too broad for one atomic deliverable or conflicts with [CONTEXT.md](../../../.routing/CONTEXT.md).

## Folder and naming

- Location: `.tickets/<id>-<slug>/`
- `<id>`: next free three-digit number (`001`, `002`, ...). Check `.tickets/` first.
- `<slug>`: lowercase words joined by hyphens naming the outcome.
- New tickets start with status `draft`.
- Examples: `.tickets/001-sync-agents-pointer/`, `.tickets/002-modularize-c-transpiler/`.

## `task.md`

Use this exact structure:

1. **Title:** `# <id> — <short outcome>`
2. **Metadata table:**
   | ID | Status | Model | Role | Engines | Spec |
   | :--- | :--- | :--- | :--- | :--- | :--- |
   | `<id>` | `draft` | `Gemini 4 Hydrogen (Medium)` or `Claude Sonnet 5.5 (High)` | Worker | e.g. `TranspilerC` / `Evaluator` / `N/A` | [spec.md](spec.md) |
3. **Rules:** the list of rule IDs the ticket implements or changes, or `N/A`.
4. **Scope**
   - "May create or edit": exact file paths (e.g. `pino-csharp/Compiler/TranspilerC.*.cs`).
   - "Must not touch": exact files or folders (always include `projects/pino-compiler/` unless explicitly in scope, plus unrelated engines).
5. **Inputs:** relative links to exact files/sections the worker must read first.
6. **Task:** numbered imperative steps (`Create`, `Extract`, `Update`, `Run`). One action per step.
7. **Verification:** one line pointing to [spec.md](spec.md).
8. **Change log:** instruct the worker to write `changelog.md` in the ticket folder listing files touched, warnings observed, and any blockers (stop and log instead of guessing).

## `spec.md`

- Deterministic yes/no checklist with exact commands and expected outputs.
- Include the applicable gate(s) from [CONTEXT.md](../../../.routing/CONTEXT.md):
  - **C# compiler/runtime:** `dotnet test` in `pino-csharp.tests/` exits with `0` (289+ passed, 0 failed) and adds no new build warnings.
  - **Conformance & `known-failures.txt`:** Behavior changes add or update conformance cases covering the cited rule IDs, and remove the matching `known-failures.txt` lines when a fix makes them pass.
  - **C transpiler/runtime:** `dotnet run --project pino-csharp run <test.pino> --c` exits with `0` and matches expected stdout under TCC.
  - **Self-host (`projects/pino-compiler/`):** `dotnet run --project pino-csharp run projects/pino-compiler/main.pino` exits with `0`.
- Include a scope check (`git status` / `git diff --name-only` shows only files listed in `May create or edit`).
- Include commit check when applicable: English Conventional Commit with detailed body.

## Rules

- One ticket, one atomic deliverable.
- Use exact paths, symbol names, and expected outputs — never "etc." or "relevant files".
- Never hand out the ticket until the user approves the draft.
