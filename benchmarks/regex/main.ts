import { readFileSync } from "fs";

const text = readFileSync("benchmarks/data/regex.txt", "utf-8");

const t0 = performance.now();
const emailRe = /\w+@\w+\.com/g;
const identRe = /[A-Za-z_][A-Za-z0-9_]*/g;

const emails = text.match(emailRe) ?? [];
const idents = text.match(identRe) ?? [];
const emailCount = emails.length;
const identCount = idents.length;
const benchMs = performance.now() - t0;

console.log(emailCount);
console.log(identCount);
console.log(`PINO_BENCH_MS=${benchMs.toFixed(3)}`);
