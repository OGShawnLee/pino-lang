# Conformance suite

Executable proof for the language spec in [`spec/`](../spec/SPEC.md). Every `.pino` case under `conformance/` runs against the new compiler's two engines: the IR VM (`ReVm`, with shadow-RC verification enabled) and the C99 backend through TCC (`ReC`, with `-DPINO_DEBUG_RC` and `PINO_FAIL_ON_LEAK=1`). The runner and `SpecLintTests` live in `pino-csharp-re/Pino.Compiler.Tests/Conformance/`.

## Layout

A feature file `spec/features/<stem>.md` has its cases in `conformance/<stem>/`. Stdlib files use `conformance/stdlib-<stem>/` (for example `conformance/stdlib-string/`).

```
conformance/
  loops/
    infinite-break.pino        # the case
    infinite-break.out         # expected stdout
    known-failures.txt         # optional: engine failures tolerated for now
  STATUS.md                    # generated report
```

## Adding a case

1. Create `conformance/<feature>/<name>.pino`. The **first line** must be a covers header naming the spec rules the case exercises (comma-separated):
   ```pino
   # covers: LOOP-1, LOOP-11
   ```
2. Add exactly one sibling file:
   - `<name>.out`: the exact expected stdout. Line endings are normalized to `\n` and trailing whitespace of the whole output is trimmed before comparing. Both engines must exit `0` with zero RC leaks and produce identical stdout.
   - `<name>.err`: a substring that the failure message must contain. The case passes only if the run **failed** and the message contains the trimmed `.err` text. Runtime `.err` cases also assert the exact exit code (`1` for a `main` failure, `101` for a panic). A C compilation (TCC) failure, IR verifier failure, timeout, or RC leak never satisfies a `.err` case.
   The repository `.gitignore` ignores `*.out`, so stage new expectation files with `git add -f conformance/<feature>/<name>.out` (check `.err` files with `git check-ignore -v` too).
3. Keep cases deterministic: no `readline` or other stdin input (stdin is empty), and no `rand`, `time` or `sleep` that affects the output. Each engine gets 10 seconds per case; exceeding it fails with the message `timeout`.
4. Every rule in `spec/` needs at least one case that names it in a `# covers:` header, unless the rule carries `[no-case: <reason>]`. `SpecLintTests` enforces this and also checks that every ID in a header exists in the spec and that each case has exactly one `.out` or `.err`.

### Helper modules

Files inside a directory named `modules/` at depth two or more below `conformance/` (inside a feature directory, such as `conformance/<feature>/modules/` or `conformance/modules/modules/`) are helper modules for import cases, not cases themselves: they are skipped by case discovery and by the `.out`/`.err` lint. The top-level `conformance/modules/` directory is the feature directory for module tests and its `.pino` files are discovered as cases. Module resolution finds `modules/<Name>.pino` by walking up from the case file's directory, so a case `conformance/<feature>/x.pino` can import `modules/<Name>.pino` placed in `conformance/<feature>/modules/`.

## `known-failures.txt`

Optional per-feature file: `conformance/<feature>/known-failures.txt` (omit the file when empty).

```
# comment lines and blank lines are allowed
<Engine> <case-file-name.pino>  # <reason>
```

`<Engine>` is `ReVm` or `ReC`. A listed case **passes** the test while it fails. If a listed case starts passing, the test fails with `Known failure now passes: remove '<line>' from <file>`, so the list can only shrink as the compiler improves. Never list a case without a reason; reference the decision file (`D-xxx`) when there is one.

If an engine crashes the test host process on a case (for example via a stack overflow), prefix the reason with `CRASH` (e.g. `# CRASH D-011: ...`). Crash-marked lines are skipped by both `RunCase` and `GenerateStatusReport` without running that engine, and count as not passed in `STATUS.md`.

## Running

From the repo root:

```powershell
dotnet test pino-csharp-re/Pino.sln --filter FullyQualifiedName~Conformance      # conformance cases + spec lint
dotnet test pino-csharp-re/Pino.sln                                              # everything
```

The `ReC` engine needs `tooling/tcc/tcc.exe` (it is not tracked by git). If it is missing, `ReC` cases fail at build time. The runner writes generated C and executables to a temporary directory under `pino-re-conformance/` in the system temp folder and deletes it afterwards.

## Regenerating `STATUS.md`

`conformance/STATUS.md` is generated; do not edit it by hand. From the repo root on PowerShell:

```powershell
$env:PINO_CONFORMANCE_STATUS=1; dotnet test pino-csharp-re/Pino.sln --filter GenerateStatusReport
```

Each cell is `passed/total` for that engine and feature directory; known failures count as not passed. Copy the numbers into the `Status:` line of the feature file in `spec/features/`.
