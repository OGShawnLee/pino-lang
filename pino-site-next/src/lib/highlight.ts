import fs from "node:fs";
import path from "node:path";
import { createCssVariablesTheme, createHighlighter, type Highlighter } from "shiki";

// One grammar for the editor and the site: read the VS Code extension's grammar at build time.
const grammarPath = path.resolve(process.cwd(), "../vscode-pino/syntaxes/pino.tmLanguage.json");

const theme = createCssVariablesTheme({
  name: "pino-css-variables",
  variablePrefix: "--shiki-",
  variableDefaults: {},
  fontStyle: true,
});

let highlighter: Promise<Highlighter> | undefined;

function getHighlighter(): Promise<Highlighter> {
  if (!highlighter) {
    const grammar = JSON.parse(fs.readFileSync(grammarPath, "utf8"));
    highlighter = createHighlighter({
      themes: [theme],
      langs: [{ ...grammar, name: "pino", scopeName: grammar.scopeName ?? "source.pino" }, "bash", "powershell"],
    });
  }
  return highlighter;
}

/** Highlight `code` as HTML (a `<pre class="shiki">…</pre>`). */
export async function highlight(code: string, lang: "pino" | "bash" | "powershell" = "pino"): Promise<string> {
  const h = await getHighlighter();
  return h.codeToHtml(code.replace(/\s+$/, ""), { lang, theme: theme.name });
}
