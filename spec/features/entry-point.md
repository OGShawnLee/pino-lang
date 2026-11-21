# Program entry point

Status: tree-walk 8/8 | VM 8/8 | C 8/8   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf` section `main`.
A Pino file runs in one of two modes. In **script mode** the file has no function named `main` and its top-level statements run in order. In **program mode** the root file defines `fn main()`; `main` is then run automatically and the top level is restricted to declarations and `val` constants. No syntax is specific to the entry point beyond the function name `main`.

## Rules
- MAIN-1: In script mode, a file without a `main` function evaluates its top-level statements sequentially in source order. [src: LANGUAGE_REFERENCE.md §5 "Program Entry Point (`main()`) & Program Mode"]
- MAIN-2: In program mode, a `fn main()` defined in the root file is called automatically once, after the file's top-level declarations and `val` constants have been evaluated, without an explicit call. [src: LANGUAGE_REFERENCE.md §5 "Program Entry Point (`main()`) & Program Mode"]
- MAIN-3: In program mode, top-level `val` declarations are allowed (deeply immutable) and are visible inside `main` and the other functions declared below them (VAR-E8), while top-level `var` and `val mut` declarations remain forbidden in Program Mode root files (MAIN-E3). [src: LANGUAGE_REFERENCE.md §5 "Program Entry Point (`main()`) & Program Mode"; CheckerTests.TestProgramModeTopLevelRestrictions; spec/decisions/D-165-declaration-hoisting.md; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md; spec/decisions/D-183-val-mut-and-module-state.md]
- MAIN-4: In program mode, top-level struct, interface, enum, union, function and import declarations are allowed. [src: LANGUAGE_REFERENCE.md §5 "Program Entry Point (`main()`) & Program Mode"; CheckerTests.TestProgramModeTopLevelRestrictions]

## Errors
- MAIN-E1: In program mode, an explicit top-level call `main()` is rejected with a message containing "not allowed at the top level when a 'main' function is defined". [src: LANGUAGE_REFERENCE.md §5 "Program Entry Point (`main()`) & Program Mode"]
- MAIN-E2: In program mode, a top-level statement with side effects (a loop, a conditional, an assignment or a loose expression such as a call) is rejected with a message containing "not allowed at the top level when a 'main' function is defined". [src: LANGUAGE_REFERENCE.md §5 "Program Entry Point (`main()`) & Program Mode"; CheckerTests.TestProgramModeTopLevelRestrictions]
- MAIN-E3: In program mode, a top-level `var` or `val mut` declaration in the root file is rejected with a message containing "must be declared with 'val'". [src: LANGUAGE_REFERENCE.md §5 "Program Entry Point (`main()`) & Program Mode"; CheckerTests.TestProgramModeTopLevelRestrictions; spec/decisions/D-182-union-payload-and-loop-variable-immutability.md; spec/decisions/D-183-val-mut-and-module-state.md]
- MAIN-E4: A `fn main` that declares parameters is rejected by the checker with a message containing "cannot take parameters", until the design of `main` arguments (D-036) is decided. [src: spec/decisions/D-36-main-with-parameters.md (interim rule, ticket 031 OQ-18)]

## Examples
Conformance cases: `conformance/entry-point/`.

The reference says `main()` "is automatically called at the end of compilation/loading". The runner and the CLI do exactly that: `Program.RunFile` (`pino-csharp/Program.cs:391`) executes the whole file and then calls `main` when it exists and takes no parameters. They also skip the automatic call when a top-level `main()` call is present, but the checker already rejects that program (MAIN-E1). How a `Failure` or `None` returned from `main` ends the process belongs to `features/errors.md`.

## Open questions
- D-036: The design of `main` arguments (for example command-line arguments) is still open; until then MAIN-E4 rejects a `main` with parameters.
