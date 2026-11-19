# Pino Language Specification

`spec/` is the **normative** definition of the Pino language. [`LANGUAGE_REFERENCE.md`](../LANGUAGE_REFERENCE.md) is the human-readable guide, and [`conformance/`](../conformance/) is the executable proof: every rule below is exercised by `.pino` cases that run against the tree-walk evaluator, the bytecode VM and the C backend. Read this index first, then the feature file you need.

## Source-of-truth rule

A rule may be written into the spec only if it is backed by at least one of:
- (a) `LANGUAGE_REFERENCE.md`
- (b) an assertion in an existing xUnit test
- (c) a proposal in `proposals/` whose status line reads `Implemented` (today: 004, 005, 006 and 013). Proposals marked `Draft` or `Proposed` count only as evidence for a decision file.

Cite the source on the rule line. If the sources are silent, contradict each other, or disagree with what the implementation does, **don't write a rule**. Open a decision file (`D-xxx`) instead. Never invent syntax or semantics.

Decisions live in [`decisions/`](decisions/README.md) and are made only by the project owner. Every feature file follows [`TEMPLATE.md`](TEMPLATE.md). The grammar is in [`grammar.ebnf`](grammar.ebnf).

## Features

| Feature | File | Rule prefix | Owner ticket | State |
| :--- | :--- | :--- | :--- | :--- |
| Lexical structure (comments, identifiers, naming, literals, optional commas) | `features/lexical.md` | `LEX` | 005 | pending |
| Variables and constants | `features/variables.md` | `VAR` | 005 | pending |
| Primitive types and conversions | `features/types.md` | `TYPE` | 005 | pending |
| Runes | `features/runes.md` | `RUNE` | 005 | pending |
| Strings and interpolation | `features/strings.md` | `STR` | 005 | pending |
| Equality and identity | `features/equality.md` | `EQ` | 005 | pending |
| Conditionals | `features/conditionals.md` | `IF` | 006 | pending |
| Loops | `features/loops.md` | `LOOP` | 004 | migrated |
| Match | `features/match.md` | `MATCH` | 006 | pending |
| Program entry point | `features/entry-point.md` | `MAIN` | 006 | pending |
| Functions, lambdas, closures | `features/functions.md` | `FN` | 007 | pending |
| Labeled tuples | `features/tuples.md` | `TUP` | 007 | pending |
| Vectors | `features/vectors.md` | `VEC` | 007 | pending |
| Maps | `features/maps.md` | `MAP` | 007 | pending |
| Modules and imports | `features/modules.md` | `MOD` | 007 | pending |
| Structs and embedding | `features/structs.md` | `STRUCT` | 008 | pending |
| Interfaces | `features/interfaces.md` | `IFACE` | 008 | pending |
| Enums | `features/enums.md` | `ENUM` | 008 | pending |
| Unions and `is` | `features/unions.md` | `UNION` | 008 | pending |
| Generics | `features/generics.md` | `GEN` | 008 | pending |
| Error handling (`panic`, `Result`, `Option`, `?`, `or`/`yield`, `main` failure handler) | `features/errors.md` | `ERR` | 008 | pending |

## Standard library

Stdlib files have a `## Signatures` table (`Member | Signature | Returns`) followed by `## Rules`. Their conformance cases live in `conformance/stdlib-<stem>/`, for example `conformance/stdlib-string/`.

| File | Rule prefix | Owner ticket | State |
| :--- | :--- | :--- | :--- |
| `stdlib/string.md` | `STRLIB` | 005 | pending |
| `stdlib/regex.md` | `RGX` | 005 | pending |
| `stdlib/builtins.md` | `BUILTIN` | 006 | pending |
| `stdlib/vector.md` | `VECLIB` | 007 | pending |
| `stdlib/map.md` | `MAPLIB` | 007 | pending |

## Conventions

- **Conformance directory:** a feature file `features/<stem>.md` has its cases in `conformance/<stem>/`.
- **Rule lines:** inside `## Rules` and `## Errors`, every rule starts with `- <PREFIX>-<n>: ` or `- <PREFIX>-E<n>: ` (regex `^- ([A-Z]+-E?\d+): `) and ends with a `[src: ...]` citation. Rule IDs are never renumbered or reused; one sentence per rule.
- **Untestable rules:** a rule that cannot be tested deterministically (for example `readline`, `clear`, `sleep`) appends `[no-case: <reason>]` after its `[src: ...]` citation and is exempt from the coverage lint.
- **Coverage:** every case starts with `# covers: <RULE-ID>, ...`; `SpecLintTests` fails when a rule has no case or a case names an unknown rule.
- **Not language spec:** §11 "Execution Engines" and §12 "Project Tooling and CLI" of the reference are **not** language spec and are not migrated.
