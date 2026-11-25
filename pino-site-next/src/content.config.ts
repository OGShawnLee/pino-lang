import { defineCollection } from "astro:content";
import { glob } from "astro/loaders";

const guide = defineCollection({
  loader: glob({ pattern: "*.md", base: "../docs/guide" }),
});

export const collections = { guide };
