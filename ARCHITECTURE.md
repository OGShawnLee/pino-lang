> **Note:** This document describes the 0.3 design and is out of date. See [spec/](spec/) and [docs/guide/](docs/guide/) for current documentation.

# Pino Lang Architecture

This document gives a concise overview of the Pino Lang compiler implementations, runtime, specification, and conformance suite.

---

## 1. Overview

Pino is a statically typed, expression-oriented systems and data-science language. Its repository contains:

1. **Legacy Compiler & Runtime (`pino-csharp/` + `runtime/`):** The current working .NET 10.0 implementation with a shared frontend (lexer, parser, type checker/monomorphizer) and three execution backends: a tree-walk evaluator, a frozen bytecode VM, and a C99 transpiler compiled via Tiny C Compiler (TCC) with Boehm GC. Also includes a WebAssembly bridge (`WASMBridge.cs`).
2. **Clean Rewrite (`pino-csharp-re/`):** A `.NET 10.0` LSP-ready rewrite currently implementing `Text`, `Diagnostics`, and `Lexing`. Its full pipeline is **planned** around **AD-001** (single Pino IR, two consumers).
3. **Self-Hosting Compiler (`projects/pino-compiler/`):** A hand-written Pino compiler written in Pino.
4. **Normative Spec & Conformance (`spec/`, `conformance/`):** Rule-ID specification, EBNF grammar, design decisions, and an executable conformance suite run across engines.

---

## 2. Legacy Pipeline (`pino-csharp/` + `runtime/`)

```text
.pino source
  └─> Lexer (Lexer.cs)
        └─> Parser (Parser/)
              └─> Checker & Monomorphizer (Checker/)
                    ├─> Tree-Walk Evaluator (Evaluator/)        [reference oracle]
                    ├─> Bytecode VM (VM/)                       [frozen]
                    └─> C99 Transpiler (Compiler/TranspilerC.cs)
                          └─> TCC (tooling/tcc/) + Boehm GC (runtime/) ──> native binary
```

- **Frontend:** `Lexer.cs` scans tokens, string interpolation (`$var`, `$(expr)`), numeric separators, and runes. `Parser/` uses recursive descent with precedence climbing. `Checker/` performs static type checking, inference, and compile-time generic monomorphization.
- **Tree-Walk Evaluator (`Evaluator/`):** Executes the checked AST directly in scoped lexical environments and serves as the reference oracle.
- **Bytecode VM (`VM/`):** Stack-based bytecode engine (`OperationCode`, `Chunk`, `Compiler`, `VM`). Frozen per AD-001 with no new investment.
- **C99 Transpiler (`Compiler/TranspilerC.cs`):** Emits standard C99 backed by `runtime/runtime.c`, `runtime/runtime.h` (including 16-byte zero-copy `PinoString` slices), and `runtime/re.c`, compiled by bundled TCC (`tooling/tcc/`) and linked with Boehm GC (`gc.h`).

---

## 3. Planned Rewrite (`pino-csharp-re/` — AD-001)

`pino-csharp-re/Pino.sln` (`Pino.Compiler` and `Pino.Compiler.Tests`) is the clean, error-tolerant rewrite designed for LSP and multi-target execution.

- **Built today:** `Text` (`SourceText`, `SourceSpan`, `LinePosition`), `Diagnostics` (`Diagnostic`, `DiagnosticBag`, `DiagnosticCodes`), and `Lexing` (`Lexer`, `Token`, `TokenKind`, `Trivia`, `LexResult`).
- **Planned (not yet built):** Parser, Checker, and **AD-001 Single IR**:
  - The checked AST will lower once into a TACKY-style **Pino IR**.
  - Two consumers will read that IR so semantics are never re-implemented from the AST:
    1. **IR Interpreter / VM:** for the dev loop, REPL, WASM playground, and platforms without TCC.
    2. **C Emitter:** targeting TCC for fast dev builds and GCC/Clang for optimized release builds.
  - Once `pino-csharp-re/` matches the legacy Tree-Walk evaluator on `conformance/`, legacy `pino-csharp/` will be retired.

---

## 4. Self-Hosting Compiler (`projects/pino-compiler/`)

`projects/pino-compiler/` is Pino's hand-written self-hosting compiler written in Pino itself, structured with `modules/`, `test/`, and `main.pino`.

---

## 5. Specification & Conformance (`spec/`, `conformance/`)

- **`spec/`:** Normative language definition (`SPEC.md`), per-feature and stdlib rule files with rule IDs (e.g. `LEX-1`, `LOOP-2`), `grammar.ebnf`, and owner decisions in `decisions/`.
- **`conformance/`:** Executable `.pino` test cases tagged with `# covers: <RULE-ID>`, run across the `ReVm` and `ReC` backends via `dotnet test pino-csharp-re/Pino.sln` (which also runs `SpecLintTests` to verify rule coverage).
