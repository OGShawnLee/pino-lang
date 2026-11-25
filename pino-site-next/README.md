# Pino site

The website for the Pino programming language, built with [Astro](https://astro.build).

## Commands

Run these from `pino-site-next/`:

| Command | What it does |
| :--- | :--- |
| `pnpm install` | Install the dependencies. |
| `pnpm dev` | Start the local server at `localhost:4321`. |
| `pnpm build` | Build the site into `dist/`. |
| `pnpm check:pino` | Check every Pino snippet on the site with the compiler in `../pino-csharp-re/`. |

## Content

- Pino snippets live in `src/snippets/`. A `name.pino` file must pass the checker; a `name.out.txt` next to it holds its exact output. A `name.fail.pino` file must fail, with the exact message in `name.fail.txt`. `pnpm check:pino --update` rewrites those files from the compiler.
- The language guide is rendered from `../docs/guide/` at build time; the site never keeps a copy.

## Fonts

Satoshi's license forbids redistributing the font files, so `public/fonts/Satoshi-*.woff2` is ignored by git. Download Satoshi from [Fontshare](https://www.fontshare.com/fonts/satoshi) (Light, Regular, Medium, Italic) and put the `.woff2` files in `public/fonts/`.
