// @ts-check
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";
import { defineConfig } from "astro/config";
import { loadPinoGrammar, pinoTheme } from "./src/lib/shiki-theme.mjs";
import { remarkGuideLinks } from "./src/lib/remark-guide-links.mjs";

// Astro 7 uses Sätteri as the default Markdown processor; resolve @astrojs/markdown-satteri via astro.
const astroRequire = createRequire(import.meta.resolve("astro/package.json"));
const { satteri } = await import(pathToFileURL(astroRequire.resolve("@astrojs/markdown-satteri")).href);

// https://astro.build/config
export default defineConfig({
  // Keep the spaces between words and inline <code>/<em> in the source.
  compressHTML: false,
  markdown: {
    processor: satteri({
      mdastPlugins: [remarkGuideLinks()],
    }),
    shikiConfig: {
      theme: pinoTheme,
      langs: [loadPinoGrammar()],
    },
  },
});
