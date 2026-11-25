# Pino site

The website for the Pino programming language, built with [Astro](https://astro.build).

## Commands

Run these from `pino-site-next/`:

| Command | What it does |
| :--- | :--- |
| `bun install` | Install the dependencies. |
| `bun dev` | Start the local server at `localhost:4321`. |
| `bun run build` | Build the site into `dist/`. |
| `bun run check:pino` | Check every Pino snippet on the site with the compiler in `../pino-csharp-re/`. |

## Content

- Pino snippets live in `src/snippets/`. A `name.pino` file must pass the checker; a `name.out.txt` next to it holds its exact output. A `name.fail.pino` file must fail, with the exact message in `name.fail.txt`. `bun run check:pino --update` rewrites those files from the compiler.
- The language guide is rendered from `../docs/guide/` at build time; the site never keeps a copy.

## Fonts

- Satoshi (text) loads from [Fontshare](https://www.fontshare.com/fonts/satoshi). Its license forbids redistributing the font files, so they are never committed (`public/fonts/Satoshi-*.woff2` is ignored).
- Nordwand Mono (code) and Cormorant Garamond italic (one line per section) are self-hosted from `public/fonts/` under the SIL Open Font License.
