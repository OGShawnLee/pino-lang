# Decision files

A decision file records a question the spec cannot answer from its allowed sources (see the source-of-truth rule in [`../SPEC.md`](../SPEC.md)): the reference is silent, the sources contradict each other, or they disagree with what the implementation does.

## Who decides

**Decisions are made only by the project owner.** Workers open a decision with `Status: open` and fill in `Question`, `Evidence` and `Options`. They never mark a decision `accepted` or `rejected`, never fill in `## Decision`, and never write a spec rule for the disputed behavior until the owner has decided.

Allowed `Status:` values: `open`, `accepted`, `rejected`.

## File naming and format

- File name: `D-<nnn>-<kebab-case-title>.md`, for example `D-001-loop-header-commas.md`.
- Copy [`TEMPLATE.md`](TEMPLATE.md). Each file has `Status:`, `Raised by:` and `Feature:` fields and the sections `## Question`, `## Evidence`, `## Options` and `## Decision`.
- Evidence cites `file:line` for the reference, the tests and the implementation, and includes a minimal `.pino` snippet with its actual output on each engine when relevant.
- IDs are never renumbered or reused.

## Reserved ID ranges

| Ticket | IDs |
| :--- | :--- |
| `004` | D-001 to D-009 |
| `005` | D-010 to D-029 |
| `006` | D-030 to D-049 |
| `007` | D-050 to D-069 |
| `008` | D-070 to D-089 |
