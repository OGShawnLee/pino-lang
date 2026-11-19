# 010 — Change log

Worker: Gemini 4 Hydrogen.

## Files touched

- `pino-csharp.tests/Conformance/ConformanceRunner.cs`: updated `IsInModulesDir` (and added its doc comment) so the first path segment (the feature directory) is ignored and only a `modules` directory at depth two or more is excluded as a helper directory. (`SpecLintTests.cs` already delegates discovery to `ConformanceRunner.DiscoverCases()` and needed no changes.)
- `spec/features/modules.md`: removed all 11 `[no-case: ...]` markers that cited the `ConformanceRunner.IsInModulesDir` discovery bug (`MOD-1` through `MOD-6`, `MOD-E1` through `MOD-E5`).
- `conformance/modules/known-failures.txt`: removed the stale ticket `007` note and removed `C err-from-import-unlisted-name.pino`, which passes under the conformance runner (TCC fails with an implicit declaration error containing `greet`).
- `conformance/README.md`: updated the `Helper modules` paragraph to state the depth rule (`modules/` at depth two or more below `conformance/`).
- `spec/decisions/D-051-function-optional-commas.md`: linked the related-decision cross-reference to `[D-010](D-010-optional-commas-vs-unary-minus.md)`.
- `spec/decisions/D-071-null-vs-option.md`: linked the cross-reference to `[D-013](D-013-null-primitive-versus-option.md)`.
- `.tickets/010-fix-conformance-modules-discovery/changelog.md`: this file.

## Final known failures for `modules` (`conformance/modules/known-failures.txt`)

Total: 6 entries (9 cases × 3 engines = 27 module conformance runs; 21 pass as expected, 6 are listed as known failures):

- **TreeWalk (1):**
  - `from-import-no-commas.pino` — `MOD-4`: parser reads only the first name of `from M import a b c`, so `add` is undeclared at runtime.
- **VM (4):**
  - `import-qualified.pino` — `RUNTIME ERROR 'Value is not callable.'` on module function call.
  - `from-import.pino` — `RUNTIME ERROR 'Value is not callable.'` on module function call.
  - `from-import-no-commas.pino` — `RUNTIME ERROR 'Value is not callable.'` on module function call.
  - `err-from-import-unlisted-name.pino` — `RUNTIME ERROR 'Value is not callable.'` instead of a message naming `greet`.
- **C (1):**
  - `from-import-no-commas.pino` — `MOD-4`: same parser limitation; TCC error `'add' undeclared`.

`import-qualified.pino` and `from-import.pino` both pass on `TreeWalk` and `C`. `C err-from-import-unlisted-name.pino` passes under `ConformanceRunner.Judge` (TCC errors with implicit declaration of `greet`), so it was removed from `known-failures.txt`.

## Verification

- `dotnet test --filter FullyQualifiedName~Conformance --list-tests`: lists all 9 `modules/*.pino` cases on `TreeWalk`, `VM`, and `C` (27 test cases), and nothing under `modules/modules/`.
- `dotnet test` (full suite in `pino-csharp.tests/`): `1481` total, `1481` passed, `0` failed, `0` skipped.
- `spec/features/modules.md`: `0` remaining `[no-case:` markers (down from `11`); `SpecLintTests` pass.

## Blockers

None.
