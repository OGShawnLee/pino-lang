// Every Pino snippet on the site lives in src/snippets/ and is checked by `pnpm check:pino`.
// A `name.pino` has its exact output in `name.out.txt`; a `name.fail.pino` has its exact error in `name.fail.txt`.

const files = import.meta.glob("../snippets/*", { query: "?raw", import: "default", eager: true }) as Record<
  string,
  string
>;

function read(file: string): string | undefined {
  const text = files[`../snippets/${file}`];
  return text === undefined ? undefined : text.replace(/\r\n/g, "\n");
}

export interface Snippet {
  code: string;
  output?: string;
}

export function snippet(name: string): Snippet {
  const code = read(`${name}.pino`);
  if (code === undefined) throw new Error(`Missing snippet src/snippets/${name}.pino`);
  return { code, output: read(`${name}.out.txt`) };
}

export interface FailingSnippet {
  code: string;
  /** The checker's message, without its closing "checked N file(s)" summary line. */
  message: string;
}

export function failingSnippet(name: string): FailingSnippet {
  const code = read(`${name}.fail.pino`);
  const message = read(`${name}.fail.txt`);
  if (code === undefined || message === undefined) {
    throw new Error(`Missing snippet src/snippets/${name}.fail.pino or its .fail.txt`);
  }
  const lines = message.trimEnd().split("\n");
  if (/^checked \d+ file/.test(lines.at(-1) ?? "")) lines.pop();
  return { code, message: lines.join("\n") };
}
