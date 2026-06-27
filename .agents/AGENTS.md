# Pino Project Rules & Guidelines

Welcome to the Pino Lang project workspace. This file acts as persistent memory for agents working on the compiler and runtime.

## Project Structure & Architecture

Pino is implemented in C# (.NET 10.0) and is structured as follows:
- **Parser (`pino-csharp/Parser/`)**: Modular recursive-descent parser. Handles desugaring (e.g. arrow lambdas) and precedence climbing.
- **Checker (`pino-csharp/Checker/`)**: Static type checking and generic monomorphization.
- **Evaluator (`pino-csharp/Evaluator/`)**: AST Tree-walk execution engine.
- **VM (`pino-csharp/VM/`)**: Bytecode compiler and stack-based virtual machine runtime.
- **Tests (`pino-csharp.tests/`)**: xUnit test suite containing atomic tests for all components.

---

## Pino Syntax & Grammar Reference

To avoid compiler parsing errors when writing Pino test code, always follow these grammar rules:

### 1. Loops
- **Keyword**: Always use **`for`**, not `loop`. (The parser maps the `"for"` keyword to `KeywordType.Loop`).
- **Syntax**: `for variable in collection { ... }` or `for limit { ... }`.
- **Example**:
  ```pino
  for x in list {
      println(x)
  }
  ```

### 2. Vectors
- **Declaration**: Declare empty vectors as `[]Type` (e.g. `[]int`, `[]string`, `[]T`).
- **Do not use brackets** like `[]int {}` for empty vectors; use `[]int`.
- **Member Methods**: Vectors support `.push(element)`, `.add(element)`, `.map(callback)`, `.filter(callback)`, `.any(callback)`, `.all(callback)`, `.each(callback)`, and `.find(callback)`.
- **Do not cast variables**: Do not use the following sintax: `val x int = 12` as it is not supported.

### 3. Shorthand Lambdas (`it`)
- Shorthand lambdas (e.g. `it:len <= 4`) wrap arguments using the implicit parameter `it`.
- The Parser assigns `it` type as `"implicit"`, which the Static Checker contextually resolves during call argument validation.

---

## Development & Troubleshooting Guidelines

### 1. Running Unit Tests
Always run unit tests from the `pino-csharp.tests/` folder:
```powershell
dotnet test
```

### 2. Locked Compiler Process (`pino-csharp.exe`)
If `dotnet test` or `dotnet run` fails with a file lock error on `pino-csharp.exe` because the compiler apphost is in use, terminate the locked process using PowerShell:
```powershell
Stop-Process -Name pino-csharp -Force
```
Then re-run the tests.

### 3. PowerShell Commands Chaining
In Windows PowerShell, chaining commands with `&&` fails. Always use semicolon `;` to chain commands instead.

# 4. Git Commits (Conventional Commits)
Always write commit messages following the Conventional Commits specification. Key prefixes to use:
* `feat(scope)`: A new feature (e.g., `feat(vm): add boolean and/or logic support`).
* `fix(scope)`: A bug fix (e.g., `fix(parser): resolve escape character scanning`).
* `docs(scope)`: Documentation-only changes (e.g., `docs(README): update vision statement`).
* `test(scope)`: Adding or modifying test suites (e.g., `test(execution): atomize arithmetic tests`).
* `refactor(scope)`: Code changes that neither fix bugs nor add features.
* `chore(scope)`: Changes to the build process or auxiliary tools and libraries (e.g., `chore(agents): update AGENTS.md`).
* `perf(scope)`: Performance improvements (e.g., `perf(vm): optimize arithmetic operations`).
* `style(scope)`: Code style changes (e.g., `style(parser): format AST output`).
* `i18n(scope)`: Internationalization and localization changes.
* `ci(scope)`: Changes to the CI/CD pipeline (e.g., `ci(github-actions): update workflow`).

