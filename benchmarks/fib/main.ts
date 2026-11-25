import { existsSync } from "fs";

function fib(n: number): number {
  if (n < 2) {
    return n;
  }
  return fib(n - 1) + fib(n - 2);
}

const quick = process.argv.includes("--quick") || existsSync("benchmarks/data/quick");
const n = quick ? 25 : 29;
console.log(fib(n));
