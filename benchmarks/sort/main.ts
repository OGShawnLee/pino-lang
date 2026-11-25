import { existsSync } from "fs";

function quicksort(arr: number[], lo: number, hi: number): void {
  if (lo >= hi) {
    return;
  }
  const pivot = arr[lo + Math.floor((hi - lo) / 2)];
  let i = lo;
  let j = hi;
  while (i <= j) {
    while (arr[i] < pivot) {
      i++;
    }
    while (arr[j] > pivot) {
      j--;
    }
    if (i <= j) {
      const tmp = arr[i];
      arr[i] = arr[j];
      arr[j] = tmp;
      i++;
      j--;
    }
  }
  if (lo < j) {
    quicksort(arr, lo, j);
  }
  if (i < hi) {
    quicksort(arr, i, hi);
  }
}

const quick = process.argv.includes("--quick") || existsSync("benchmarks/data/quick");
const n = quick ? 5000 : 25000;

let state = 123456789;
const arr: number[] = [];
for (let i = 0; i < n; i++) {
  state = (state * 1664525 + 1013904223) % 2147483648;
  arr.push(state);
}

quicksort(arr, 0, n - 1);

let checksum = 0;
for (let i = 0; i < n; i++) {
  checksum = (checksum + (i + 1) * (arr[i] % 1000003)) % 1000000007;
}

console.log(arr[0]);
console.log(arr[Math.floor(n / 2)]);
console.log(arr[n - 1]);
console.log(checksum);
