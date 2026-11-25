import { readFileSync } from "fs";

const text = readFileSync("benchmarks/data/word-count.txt", "utf-8");

const counts = new Map<string, number>();
let total = 0;

const lines = text.split("\n");
for (const line of lines) {
  const words = line.split(" ");
  for (const w of words) {
    if (w.length === 0) {
      continue;
    }
    total++;
    counts.set(w, (counts.get(w) ?? 0) + 1);
  }
}

console.log(total);
console.log(counts.size);

const used = new Set<string>();
const keys = Array.from(counts.keys());
const limit = Math.min(10, keys.length);

for (let iter = 0; iter < limit; iter++) {
  let bestWord = "";
  let bestCount = -1;
  for (const k of keys) {
    if (used.has(k)) {
      continue;
    }
    const c = counts.get(k)!;
    if (c > bestCount || (c === bestCount && k < bestWord)) {
      bestCount = c;
      bestWord = k;
    }
  }
  used.add(bestWord);
  console.log(`${bestWord} ${bestCount}`);
}
