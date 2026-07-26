# RFC 012: Test-Scoped Symbol Visibility (`@pub_testing`)

## Status
- **Status**: Proposed
- **Authors**: Pino Language Core Team
- **Created**: July 26, 2026

---

## Executive Summary

This RFC proposes the `@pub_testing` decorator attribute to enable **Test-Scoped Symbol Visibility** in Pino. `@pub_testing` allows module declarations (functions, structs, enums, unions, variables) to remain private to standard module consumers while making them selectively importable by unit test suites located in the `test/` directory or executed during `pino test`.

---

## Motivation

As Pino codebases (such as the self-hosted Pino compiler `projects/pino-compiler`) grow in scale, embedding inline `test` blocks directly inside production source files (`Parser.pino`, `Lexer.pino`) causes source files to become excessively large and hard to maintain.

Moving unit tests to dedicated test files in `test/` poses a classic software engineering trade-off:
1. **Making helper functions public (`pub`)**: Pollutes the public module API, exposing internal implementation details to external consumers.
2. **Keeping helper functions private**: Prevents dedicated test files from verifying private helper logic (`parse_declaration`, `parse_expr`).

`@pub_testing` solves this problem by providing **controlled, compiler-enforced test visibility**.

---

## Detailed Design

### 1. The `@pub_testing` Decorator

Any declaration decorated with `@pub_testing` is treated as **private for standard application code**, but **public for test targets**:

```pino
module Parser

# Private to external non-test modules
# Public ONLY to test files in test/ or during `pino test`
@pub_testing
fn parse_declaration(stream TokenStream) Result[Declaration, string] {
    # Private parsing helper logic
}
```

### 2. Static Checker Enforcement Rules

The Pino Static Typechecker (`Checker`) enforces strict scoping rules for `@pub_testing` symbols:

1. **Standard Module Import (Denied)**:
   Attempting to import a `@pub_testing` symbol from a non-test source file (e.g. `main.pino` or `app.pino`) results in a compile-time error:
   ```text
   TYPE CHECK ERROR: Symbol 'parse_declaration' is decorated with @pub_testing and cannot be imported outside of test suites.
   ```
2. **Test File Import (Allowed)**:
   Importing `@pub_testing` symbols from files within the `test/` directory, files ending in `.test.pino`, or during `pino test` execution is cleanly permitted:
   ```pino
   # test/parser_test.pino
   from Parser import parse_declaration, parse_expr

   test "Parser: Test private declaration parser" {
       val stream = TokenStream { list: tokens, cursor: 0 }
       val decl = parse_declaration(stream)
       assert decl is Result::Success
   }
   ```

---

## Comparison with Other Languages

| Language | Test Visibility Mechanism | Scope Control |
| :--- | :--- | :--- |
| **Swift** | `@testable import Module` | Elevates internal items at import site |
| **C# / .NET** | `[InternalsVisibleTo("Tests")]` | Assembly-level grant |
| **Java / Android** | `@VisibleForTesting` | Annotation documentation |
| **Rust** | `pub(crate)` / `mod tests` | Submodule / crate level |
| **Pino 🌲** | **`@pub_testing` Decorator** | **Compiler-enforced test-only import visibility** |

---

## Implementation Roadmap

1. **Parser**: Update `Parser` to scan and store `@pub_testing` decorator on declaration AST nodes.
2. **Checker**: Add `IsPubTesting` boolean property to symbol entries. Throw a Type Check Error if imported by non-test source files.
3. **C Transpiler**: Emit C functions for `@pub_testing` symbols without changing C linkage.
