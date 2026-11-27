import { existsSync } from "fs";

const quick = process.argv.includes("--quick") || existsSync("benchmarks/data/quick");
const n = quick ? 1500 : 7500;

const t0 = performance.now();
let s = "";
for (let i = 0; i < n; i++) {
  const piece = "w" + (i % 1000) + ",";
  s = s + piece;
}

const parts = s.split(",");
const joined = parts.join(":");
const benchMs = performance.now() - t0;

console.log(s.length);
console.log(parts.length);
console.log(joined.length);
console.log(`PINO_BENCH_MS=${benchMs.toFixed(3)}`);
