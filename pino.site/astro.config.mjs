import { defineConfig } from 'astro/config';
import Vue from '@astrojs/vue';
import UnoCSS from 'unocss/astro';
import { presetWind3, presetAttributify, presetIcons, presetWebFonts } from 'unocss';

// https://astro.build/config
export default defineConfig({
  integrations: [
    Vue(),
    UnoCSS({
      presets: [
        presetWind3(),
        presetAttributify(),
        presetIcons({
          scale: 1.2,
          warn: true,
        }),
        presetWebFonts({
          provider: 'google',
          fonts: {
            geist: 'Geist:300,400,500,600,700',
            mono: 'JetBrains Mono:400,500,600',
            heading: 'Space Grotesk:500,600,700',
          },
        }),
      ],
      theme: {
        colors: {
          bg: {
            primary: '#000000',
            secondary: '#0a0a0a',
          },
          text: {
            main: '#f3f4f6',
            muted: '#9ca3af',
          },
          accent: {
            green: '#10b981',
            greenHover: '#059669',
            cyan: '#06b6d4',
            purple: '#8b5cf6',
          },
          border: {
            colored: 'rgba(255, 255, 255, 0.06)',
          },
        },
        fontFamily: {
          sans: "-apple-system, BlinkMacSystemFont, 'SF Pro Display', 'SF Pro Text', 'Geist', 'Inter', sans-serif",
          mono: "'JetBrains Mono', 'Fira Code', monospace",
          heading: "'Space Grotesk', sans-serif",
        },
      },
      shortcuts: {
        'btn-primary': 'bg-accent-green text-bg-primary font-600 px-7 py-3 rounded-lg shadow-[0_4px_14px_rgba(16,185,129,0.25)] transition-all duration-200 hover:(bg-accent-greenHover -translate-y-0.5)',
        'btn-secondary': 'bg-white/3 border border-white/8 text-text-main font-600 px-7 py-3 rounded-lg transition-all duration-200 hover:(bg-white/8 border-white/15 -translate-y-0.5)',
        'section-header': 'text-center mb-14',
        'game-badge': 'inline-block bg-white/5 text-text-muted border border-white/8 px-3 py-1 rounded-full text-xs font-600 uppercase tracking-wide mb-4 self-start',
        'variant-badge': 'self-start text-xs font-600 px-2 py-0.5 rounded uppercase tracking-wide',
      }
    })
  ]
});