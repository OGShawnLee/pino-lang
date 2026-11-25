import fs from "node:fs";
import path from "node:path";

// The site reads the benchmark results at build time; it never keeps a copy.
const resultsDir = path.resolve(process.cwd(), "../benchmarks/results");

export interface Cell {
  status: string;
  median_ms: number;
}

export interface BenchmarkRun {
  file: string;
  date: string;
  pinoCommit: string;
  machine: string;
  results: Record<string, Record<string, Cell>>;
}

/** The newest results file (by name: `YYYY-MM-DD-os-arch.json`). */
export function latestRun(): BenchmarkRun {
  const files = fs
    .readdirSync(resultsDir)
    .filter((f) => f.endsWith(".json"))
    .sort();
  const file = files.at(-1);
  if (!file) throw new Error(`No benchmark results in ${resultsDir}`);
  const data = JSON.parse(fs.readFileSync(path.join(resultsDir, file), "utf8"));
  const m = data.machine ?? {};
  const cpu = String(m.cpu ?? "").replace(/\((R|TM)\)/g, "").replace(/\s+/g, " ").trim();
  const ram = m.ram_gb ? `${Math.round(m.ram_gb)} GB RAM` : "";
  const machine = [cpu, ram, m.os].filter(Boolean).join(", ");
  return {
    file,
    date: data.date,
    pinoCommit: String(data.pino_commit ?? "").slice(0, 7),
    machine,
    results: data.results,
  };
}

export function ms(cell: Cell | undefined): string {
  if (!cell || cell.status !== "ok") return "—";
  return Math.round(cell.median_ms).toLocaleString("en-US");
}
