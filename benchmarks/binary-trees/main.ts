import { existsSync } from "fs";

interface Tree {
  left: Tree | null;
  right: Tree | null;
}

function makeTree(depth: number): Tree {
  if (depth <= 0) {
    return { left: null, right: null };
  }
  return {
    left: makeTree(depth - 1),
    right: makeTree(depth - 1),
  };
}

function checkTree(t: Tree): number {
  if (t.left === null) {
    return 1;
  }
  return 1 + checkTree(t.left) + checkTree(t.right!);
}

function pow2(exp: number): number {
  let r = 1;
  for (let i = 0; i < exp; i++) {
    r *= 2;
  }
  return r;
}

const quick = process.argv.includes("--quick") || existsSync("benchmarks/data/quick");
const minDepth = 4;
const maxDepth = quick ? 9 : 14;
const stretchDepth = maxDepth + 1;

const stretchCheck = checkTree(makeTree(stretchDepth));
const longLived = makeTree(maxDepth);

let totalCheck = stretchCheck;
for (let d = minDepth; d <= maxDepth; d += 2) {
  const iterations = pow2(maxDepth - d + minDepth);
  let check = 0;
  for (let i = 0; i < iterations; i++) {
    check += checkTree(makeTree(d));
  }
  totalCheck += check;
}

const longCheck = checkTree(longLived);
totalCheck += longCheck;

console.log(stretchCheck);
console.log(totalCheck);
console.log(longCheck);
