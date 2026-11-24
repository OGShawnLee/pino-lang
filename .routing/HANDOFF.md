# HANDOFF

Notes between the **compiler router** ([router](../.claude/skills/router/SKILL.md)) and the **site router** ([site-router](../.claude/skills/site-router/SKILL.md)). Add entries at the top, dated and signed (`compiler` or `site`). Keep each entry short. When an entry is done, mark it `[done]` instead of deleting it.

## 2026-11-23 · compiler (owner answers)

- The owner's answers for the site are in SITE.md, under "Owner answers": a likely full redesign, Opus for UI, Neon Low for side tasks, and the uncommitted `CoreTruths.astro` edit doesn't block.
- **`README.md` is now the site router's.** The compiler router won't edit it. When a compiler change makes the README wrong (commands, install, status), it writes the change here for the site router.

## 2026-11-23 · compiler

- **Set-up.** The site router owns `pino-site-next/**`. The compiler router owns the compiler, `spec/`, `conformance/`, `games/` and the **content** of `docs/guide/`. `README.md` belongs to the site router (see the entry above).
- **Language facts:** take them from `spec/`, `docs/guide/` and `README.md`. Every snippet on the site must pass `pino-re check` (`dotnet run --no-build --project pino-csharp-re/Pino.Cli -- check <file>`).
- **The current state, for the site's status page:**
  - the new compiler runs the whole language on a VM and on native C, with 657 conformance cases passing on both
  - the legacy compiler is being retired
  - 0.4 will ship the new compiler as `pino`, with TCC bundled, so nothing else is needed to install
- **Coming from the compiler side:**
  - 070: `Pino.Wasm` for the playground; its API goes here when it lands
  - 074: the CLI commands (`init`, `run`, `build`, `watch`, `test`, `play`, `repl`, `version`, `update`)
  - 075: release assets and installers
  - 079: `docs/guide/patterns.md`
- **The games** now live in `games/`. `pino.games/` is legacy, downloaded by 0.3's `pino play update`.
