import fs from "node:fs";
import path from "node:path";
import { createCssVariablesTheme } from "shiki";

export const pinoTheme = createCssVariablesTheme({
  name: "pino-css-variables",
  variablePrefix: "--shiki-",
  variableDefaults: {},
  fontStyle: true,
});

// One grammar for the editor and the site: read the VS Code extension's grammar at build time.
export function loadPinoGrammar() {
  const grammarPath = path.resolve(process.cwd(), "../vscode-pino/syntaxes/pino.tmLanguage.json");
  const grammar = JSON.parse(fs.readFileSync(grammarPath, "utf8"));
  return { ...grammar, name: "pino", scopeName: grammar.scopeName ?? "source.pino" };
}
