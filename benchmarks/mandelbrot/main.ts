import { existsSync } from "fs";

const quick = process.argv.includes("--quick") || existsSync("benchmarks/data/quick");
const size = quick ? 150 : 335;
const maxIter = 50;
const invSize = 1.0 / size;

const t0 = performance.now();
let inside = 0;
for (let y = 0; y < size; y++) {
  const ci = 2.0 * y * invSize - 1.0;
  for (let x = 0; x < size; x++) {
    const cr = 2.0 * x * invSize - 1.5;
    let zr = 0.0;
    let zi = 0.0;
    let escaped = false;
    for (let iter = 0; iter < maxIter; iter++) {
      const tr = zr * zr - zi * zi + cr;
      const ti = 2.0 * zr * zi + ci;
      zr = tr;
      zi = ti;
      if (zr * zr + zi * zi > 4.0) {
        escaped = true;
        break;
      }
    }
    if (!escaped) {
      inside++;
    }
  }
}
const benchMs = performance.now() - t0;
console.log(inside);
console.log(`PINO_BENCH_MS=${benchMs.toFixed(3)}`);
