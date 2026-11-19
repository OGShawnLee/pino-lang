# 009 — Change log

Worker: Gemini 4 Hydrogen.

## Files touched

- `.routing/CONTEXT.md`: updated `## Files` so `spec/SPEC.md` is first and `conformance/` is second, updated `LANGUAGE_REFERENCE.md` description to "human-facing guide; defer to `spec/` on conflicts", updated `**Quality Gate:**` to include conformance and spec-lint tests via `dotnet test` in `pino-csharp.tests/`, and added `**Spec & Rule IDs:**` and `**Decisions:**` ground rules. Preserved all pre-existing router edits (including the rebase + fast-forward rule under `**Commits:**`).
- `.routing/PROGRESS.md`: updated line 3 only so invariants point to `CONTEXT.md` and `spec/SPEC.md`. Preserved all pre-existing router edits (batch status lines, `AD-001`, updated `Next steps` / `Architectural gaps`).
- `.claude/skills/router/SKILL.md`: updated the "do not invent" rule to require a spec rule ID in `spec/SPEC.md` or an open or accepted decision in `spec/decisions/`; updated `## Read first` to list `CONTEXT.md`, `PROGRESS.md`, `spec/SPEC.md`, `ARCHITECTURE.md` (removing `LANGUAGE_REFERENCE.md`); added the `$env:PINO_CONFORMANCE_STATUS=1; dotnet test --filter GenerateStatusReport` step under `### 7. Accept or return`.
- `.claude/skills/ticket/SKILL.md`: updated `## Before writing: ask the user` to point to `spec/SPEC.md` and its feature files; added required `**Rules:**` element to `## task.md`; added the conformance and `known-failures.txt` gate under `## spec.md`.
- `.agents/AGENTS.md`: replaced the 112-line file with a 17-line guide containing a one-paragraph project summary, pointers to `.routing/CONTEXT.md` and `spec/SPEC.md`, the 3-line ticket workflow, and Windows/PowerShell notes (removing all inline syntax rules and ```` ```pino ```` blocks).
- `LANGUAGE_REFERENCE.md`: inserted the `[!NOTE]` block pointing to `spec/SPEC.md` and `conformance/` directly under the `#` title; no other lines modified.
- `conformance/STATUS.md`: regenerated via `$env:PINO_CONFORMANCE_STATUS=1; dotnet test --filter GenerateStatusReport`.
- `spec/features/*.md` (20 files updated; `loops.md` already matched): updated line 3 (`Status:`) of every feature file to match its row in `conformance/STATUS.md`.
- `.tickets/009-routing-point-to-spec/changelog.md`: this file.

## Notes

- `spec/stdlib/*.md` (`builtins.md`, `map.md`, `regex.md`, `string.md`, `vector.md`) still show `Status: pending regeneration (ticket 009)` on line 3; left untouched because `task.md` scope and `spec.md` explicitly restrict `spec/` edits to line 3 of `spec/features/*.md`.

## Verification

- `$env:PINO_CONFORMANCE_STATUS=1; dotnet test --filter GenerateStatusReport` in `pino-csharp.tests/`: `1` total, `1` passed, `0` failed.
- `dotnet test` in `pino-csharp.tests/`: `1481` total, `1481` passed, `0` failed, `0` skipped.
- Content checks: `LANGUAGE_REFERENCE` absent from `.claude/skills/router/SKILL.md`; `spec/SPEC.md` present in `router/SKILL.md` (2), `ticket/SKILL.md` (1), `.routing/CONTEXT.md` (2), `.agents/AGENTS.md` (1); `.agents/AGENTS.md` is 17 lines with no ```` ```pino ```` block; `LANGUAGE_REFERENCE.md` diff contains only added lines under the title; `spec/` diff changes only line 3 of `spec/features/*.md`.

## Blockers

None.
