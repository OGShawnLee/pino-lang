import { createHighlighter, type Highlighter } from "shiki";
import { loadPinoGrammar, pinoTheme } from "./shiki-theme.mjs";

let highlighter: Promise<Highlighter> | undefined;

function getHighlighter(): Promise<Highlighter> {
  if (!highlighter) {
    highlighter = createHighlighter({
      themes: [pinoTheme],
      langs: [loadPinoGrammar(), "bash", "powershell"],
    });
  }
  return highlighter;
}

/** Highlight `code` as HTML (a `<pre class="shiki">…</pre>`). */
export async function highlight(code: string, lang: "pino" | "bash" | "powershell" = "pino"): Promise<string> {
  const h = await getHighlighter();
  return h.codeToHtml(code.replace(/\s+$/, ""), { lang, theme: pinoTheme.name });
}
