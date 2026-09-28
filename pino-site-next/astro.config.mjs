// @ts-check
import { defineConfig } from 'astro/config';
import Svelte from '@astrojs/svelte';
import UnoCSS from '@unocss/astro';

// https://astro.build/config
export default defineConfig({
  integrations: [
    UnoCSS({
      injectReset: true,
    }),
    Svelte(),
  ],
});
