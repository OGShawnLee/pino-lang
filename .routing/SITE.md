# SITE

The tracker for the site router ([skill](../.claude/skills/site-router/SKILL.md)). It covers `pino-site-next/` only. Keep it short: one bullet of 3–5 lines per finished ticket.

## Goals (owner, 2026-11-23)

- Bring the site in line with the rewritten `README.md`: the pitch (a language for people and AI), the memory model (values, reference counting, no garbage collector), what Pino is good for, strengths and weaknesses, status and roadmap, the story (option B), and how Pino is made (AI usage).
- Remove the "AI-slop" feel left by earlier versions made with Gemini Flash / Argon.
- Serve the language guide from `docs/guide/` (rendered at build time, never copied).
- For the 0.4 release: download and install instructions for the new `pino`, an "upgrading from 0.3" page, and the browser playground on the new compiler (`Pino.Wasm`, from compiler ticket 070).

## Before the first ticket

- [ ] **The owner commits or approves their uncommitted site changes** (for example `src/components/CoreTruths.astro`). Don't build on uncommitted work.
- [ ] The owner chooses the **design freedom**: a refresh of the current look, or a redesign.
- [ ] The owner confirms the worker models (suggested: Sonnet for copy and design, Neon Medium for Astro mechanics).
- [ ] The owner decides who owns `README.md` (suggested: the site router, since it's the public face; today the compiler router edits it on request).

## First session (for the site router)

1. Read CONTEXT.md, this file, HANDOFF.md and README.md.
2. Read `pino-site-next/`: the structure, the pages, the components, the build, the existing playground wiring and the content sources.
3. Write an **audit** in `.tickets/S01-site-audit/audit.md`:
   - every page, with what it says
   - where it disagrees with the README
   - every "AI-slop" pattern, quoted
   - performance and accessibility problems
   - how the playground works today (it uses the legacy `WASMBridge.cs`)
   - what's needed to render `docs/guide/`
4. Propose an ordered plan of S-tickets to the owner. Don't dispatch before the owner approves.

## Decisions

*(none yet)*

## Tickets

*(none yet)*

## Dependencies on the compiler side

- **Playground:** compiler ticket 070 builds `Pino.Wasm`. The site switches to it once its API is written in HANDOFF.md. Until then, the playground stays on the legacy bridge.
- **Install and download pages:** compiler ticket 075 (the release) sets the asset names, the installers and the binary name `pino`.
- **Guide pages:** compiler ticket 079 adds `docs/guide/patterns.md`.
