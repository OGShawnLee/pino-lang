# CONTEXT

Central routing directory for all agents. Read this first.

## Files
- [spec/SPEC.md](../spec/SPEC.md): the normative language spec (features, rule IDs, grammar, decisions).
- [conformance/](../conformance/): executable proof of the spec.
- [PROGRESS.md](PROGRESS.md): where we are, what's next, open decisions. History is in [archive/](archive/).
- [docs/guide/](../docs/guide/): the user guide (`values-and-mutation.md`, `surprises.md`).
- `LANGUAGE_REFERENCE.md` and `ARCHITECTURE.md` are **stale**. Defer to `spec/` and the new compiler.

## Project
- **Pino Lang:** a small, statically typed, expression-oriented language for people and AI. It targets Go-like services and games. Values instead of shared references, no `null`, plain-sentence errors, no garbage collector.
- **The compiler is `pino-csharp-re/`** (C#, .NET 10). It parses and checks, then lowers to one IR (PIR) with reference counting. Two engines read the IR:
  1. `Pino.Vm`: the IR VM. It's the default for `pino-re run`, and it powers the REPL and WASM.
  2. `CodeGen/C`: C99, compiled with bundled TCC (dev) or GCC/Clang (`--release`) against `runtime-re/` plus `runtime/re.c`.
- **The legacy compiler `pino-csharp/`** (Tree-Walk, VM, `TranspilerC` on a GC) **is being disposed of. Don't test it, extend it, or rely on it.** It lives on only until the release (075) and the WASM playground (070) move to the new compiler.
- **Self-host:** `projects/pino-compiler/` is the owner's hand-written compiler in Pino.

## Repo Map
- **Active:**
  - `pino-csharp-re/`: `Pino.Compiler`, `Pino.Vm`, `Pino.Cli`, `Pino.Compiler.Tests`
  - `runtime-re/`: the C RC runtime
  - `spec/`, `conformance/`, `docs/guide/`
  - `games/`: the games for the new Pino
  - `.github/workflows/`: `ci.yml`, `release.yml`
- **Tooling (gitignored):** `tooling/tcc/` (TinyCC 0.9.27 win64; CI downloads it pinned by SHA256) and `tooling/gc/` (legacy only).
- **Legacy, frozen until deleted:** `pino-csharp/`, `pino-csharp.tests/` (still hosts the conformance runner until ticket 077 moves it), `runtime/` (except `re.c`/`re.h`, which `runtime-re` uses), `pino.games/` (downloaded by 0.3's `pino play update`; don't edit).
- **Owner's, off-limits unless asked:** `projects/` (never rewrite `projects/pino-compiler/`), `main.pino`, `pino-site-next/`, `vscode-pino/`, `zed-pino/`, `benchmarks/`.

## Ground rules
- **Quality gate:**
  - `dotnet build pino-csharp-re/Pino.sln -warnaserror`
  - `dotnet test pino-csharp-re/Pino.sln`, with **0 failures**; there are no allowed worktree-only failures any more
  - `dotnet format pino-csharp-re/Pino.sln --verify-no-changes`
  - ReVm and ReC have 0 known failures
  - Until 077 lands, conformance for ReVm and ReC also runs through `pino-csharp.tests/`.
- **Spec and rule IDs:** tickets that change language behavior cite rule IDs (for example `LOOP-2`). Behavior with no rule ID needs a decision file first. Only the owner changes a decision's `Status`.
- **Messages:** every diagnostic and panic is a plain sentence with no compiler jargon. Tickets spell out the exact texts.
- **C output** must compile cleanly with TCC 0.9.27 and with `gcc -O2`.
- **Self-host rule:** never rewrite or bulk-generate `projects/pino-compiler/`. The owner writes it by hand.
- **Grammar invariants:**
  - `for` is the only loop keyword.
  - Empty vectors are `[]T`.
  - Commas are optional in lists, collections, parameters and struct literals. They're required between positional union payload types, and never used between union variants.
  - Member access uses `:`; static access uses `::`.
  - Errors use suffix `?` and `or { yield fallback }`.
  - Types go after the name (`val n int = 12`).
  - Arrows are `->`; `=>` is an error.
  - `/` returns a float; `//` floors.
  - `fn name {` with no parentheses is valid when there are no parameters.
  - `;` is ignored with a hint.
- **Windows:**
  - Chain commands with `;` in PowerShell.
  - Run test suites and GCC **from PowerShell with stdin closed** (`cmd /c "dotnet test ... < NUL"`). From Git Bash, GCC fails silently, and with an open stdin, gcc-built test programs hang.
- **C# style (`pino-csharp-re/`):**
  - uppercase acronyms (`EOF`, `IR`, `AST`)
  - `net10.0`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
  - error-tolerant diagnostics (never throw on invalid user code)
  - `.editorconfig`: 2-space indent, same-line `{`, LF
  - triple-quoted multi-line strings
- **Commits:**
  - English Conventional Commits with detailed bodies, one commit per ticket.
  - Integrate by rebase plus fast-forward; no merge commits.
  - Never `git add -A` or `git add .`.
  - Workers sign with their model's `Co-Authored-By` trailer.

## Agent roles
- **Router:** Claude Opus 5.5 (or Gemini Neon). Keeps [PROGRESS.md](PROGRESS.md), answers architecture questions, writes tickets in `.tickets/` (untracked: they exist only in the main checkout, so say so in every brief), verifies every deliverable by running the tests itself, and integrates. Never writes compiler code. Instructions: [router skill](../.claude/skills/router/SKILL.md).
- **Workers** (owner, 2026-11-23):

  | Work | Model |
  | :--- | :--- |
  | Hard compiler work (IR passes, emitters, RC, arenas, designs) | **Gemini 4 Neon (High)** |
  | Mid-sized, well-specified tickets | **Gemini 4 Neon (Medium)** |
  | Small, mechanical tickets | **Gemini 4 Hydrogen (Medium)** |
  | When Gemini quota is short, or the owner picks it (CI, games) | **Claude Sonnet 5.5** on the `claudeAgent` provider, reviewed extra carefully |

  Gemini workers sometimes end their turn early: check the branch, and nudge them on the same thread.
- **Reviewer:** the owner. Approves plans and owns architecture and language decisions.
