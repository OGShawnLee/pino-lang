---
layout: ../../layouts/Layout.astro
title: Getting Started | Pino Lang Docs 🌲
description: Quick start guide to install and run your first Pino program.
---

<div class="max-w-3xl mx-auto px-8 py-16 font-sans">
  <h1 class="font-heading font-bold text-4xl mb-6 text-accent-green">Getting Started with Pino 🌲</h1>
  <p class="text-text-muted mb-8 text-lg">Pino is designed for visual clarity, rapid terminal gaming, and aesthetic programming.</p>

  <h2 class="font-heading font-semibold text-2xl mt-10 mb-4 text-text-main border-b border-border-colored pb-2">Installation</h2>
  
  <h3 class="font-bold text-lg mt-6 mb-2">Windows (PowerShell)</h3>
  <pre class="bg-bg-secondary border border-border-colored p-4 rounded-lg font-mono text-sm text-accent-cyan mb-6"><code>irm https://raw.githubusercontent.com/OGShawnLee/pino-lang/main/install.ps1 | iex</code></pre>

  <h3 class="font-bold text-lg mt-6 mb-2">macOS & Linux (Bash/Zsh)</h3>
  <pre class="bg-bg-secondary border border-border-colored p-4 rounded-lg font-mono text-sm text-accent-cyan mb-6"><code>curl -fsSL https://raw.githubusercontent.com/OGShawnLee/pino-lang/main/install.sh | bash</code></pre>

  <h2 class="font-heading font-semibold text-2xl mt-12 mb-4 text-text-main border-b border-border-colored pb-2">Your First Script</h2>
  <p class="text-text-muted mb-4">Create a file named <code>hello.pino</code> and add the following code:</p>
  
  <pre class="bg-bg-secondary border border-border-colored p-4 rounded-lg font-mono text-sm text-text-main mb-6"><code><span class="keyword">val</span> name = <span class="string">"Reader"</span>
println(<span class="string">"Hello $name, welcome to Pino!"</span>)</code></pre>

  <p class="text-text-muted mb-4">Run the script using the Pino CLI interpreter:</p>
  <pre class="bg-bg-secondary border border-border-colored p-4 rounded-lg font-mono text-sm text-text-main mb-6"><code>pino run hello.pino</code></pre>
</div>
