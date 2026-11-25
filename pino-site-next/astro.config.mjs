// @ts-check
import { defineConfig } from "astro/config";
// Astro 7 renders Markdown with Sätteri; its plugins are mdast visitors.
import { satteri } from "@astrojs/markdown-satteri";
import { loadPinoGrammar, pinoTheme } from "./src/lib/shiki-theme.mjs";
import { remarkGuideLinks } from "./src/lib/remark-guide-links.mjs";

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
