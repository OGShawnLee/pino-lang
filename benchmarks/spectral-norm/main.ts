import { existsSync } from "fs";

function mySqrt(x: number): number {
  let g = x * 0.5 + 1.0;
  for (let i = 0; i < 16; i++) {
    g = 0.5 * (g + x / g);
  }
  return g;
}

function roundInt(v: number): number {
  return v >= 0.0 ? Math.trunc(v + 0.5) : Math.trunc(v - 0.5);
}

function evalA(i: number, j: number): number {
  const ij = i + j;
  return 1.0 / (Math.floor((ij * (ij + 1)) / 2) + i + 1);
}

function evalATimesU(u: number[], au: number[], n: number): void {
  for (let i = 0; i < n; i++) {
    let sum = 0.0;
    for (let j = 0; j < n; j++) {
      sum += evalA(i, j) * u[j];
    }
    au[i] = sum;
  }
}

function evalAtTimesU(u: number[], au: number[], n: number): void {
  for (let i = 0; i < n; i++) {
    let sum = 0.0;
    for (let j = 0; j < n; j++) {
      sum += evalA(j, i) * u[j];
    }
    au[i] = sum;
  }
}

function evalAtATimesU(u: number[], out: number[], tmp: number[], n: number): void {
  evalATimesU(u, tmp, n);
  evalAtTimesU(tmp, out, n);
}

const quick = process.argv.includes("--quick") || existsSync("benchmarks/data/quick");
const n = quick ? 80 : 300;

const u: number[] = new Array(n).fill(1.0);
const v: number[] = new Array(n).fill(0.0);
const tmp: number[] = new Array(n).fill(0.0);

for (let iter = 0; iter < 10; iter++) {
  evalAtATimesU(u, v, tmp, n);
  evalAtATimesU(v, u, tmp, n);
}

let vbv = 0.0;
let vv = 0.0;
for (let i = 0; i < n; i++) {
  vbv += u[i] * v[i];
  vv += v[i] * v[i];
}

const ans = mySqrt(vbv / vv);
console.log(roundInt(ans * 1000000000.0));
