# D-063: How `import Name` finds the module file

Status: open
Raised by: 007
Feature: spec/features/modules.md

## Question
Which file does `import Name` / `from Name import ...` load, and is the match case-sensitive?

## Evidence
- Reference: §6 says modules are imported "from other source files" (`LANGUAGE_REFERENCE.md:474`) and does not say where they live. §12 "Standard Project Layout" (not migrated, it is tooling) shows reusable modules under `modules/` (`LANGUAGE_REFERENCE.md:1334`).
- Tests: `CliTests.cs:21` only checks that `pino init` creates `modules/utils.pino`. The checker and transpiler tests inject module checkers by hand and never resolve a file.
- Implementation: `ModuleResolver.ResolveModuleFilePath` looks for `<name lowercased>.pino`, first in an explicit modules directory, then in `modules/` of the importing file's directory and of each parent directory (`pino-csharp/ModuleResolver.cs:7-34`). The module `Geometry` therefore loads `modules/geometry.pino`.

The ticket text names helper modules `modules/<Name>.pino`; on a case-sensitive file system that spelling would not be found. The conformance helpers use the lowercase file names (`geometry.pino`, `badmain.pino`).

Snippet:

```pino
import DoesNotExist
println(1)
```

All three engines fail with `TYPE CHECK ERROR: Module 'DoesNotExist' not found. Expected file at '<dir>/modules/doesnotexist.pino'.`

## Options
1. Specify the lookup (lowercased module name plus `.pino`, in `modules/` walking up from the importing file) and the not-found error.
2. Make the lookup case-preserving (`Geometry.pino`) so file name and module name match: resolver change.
3. Keep the lookup out of the language spec (project tooling, reference §12).

## Decision
_Pending: owner decides._
