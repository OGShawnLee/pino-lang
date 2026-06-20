// JS Benchmark: recursive fib(28) and loop of 100,000

function fib(n) {
  if (n <= 1) return n;
  return fib(n - 1) + fib(n - 2);
}

console.log("--- BENCHMARK JAVASCRIPT ---");

// 1. Fibonacci
const fibStart = performance.now();
const fibResult = fib(28);
const fibDuration = performance.now() - fibStart;
console.log(`1. Fibonacci(28) recursivo:`);
console.log(`   Resultado: ${fibResult}`);
console.log(`   Tiempo: ${fibDuration.toFixed(2)} ms`);

// 2. Loop
const loopStart = performance.now();
let count = 0;
for (let i = 0; i < 100000; i++) {
  count += 1;
}
const loopDuration = performance.now() - loopStart;
console.log(`2. Bucle simple de 100,000 iteraciones:`);
console.log(`   Resultado: ${count}`);
console.log(`   Tiempo: ${loopDuration.toFixed(2)} ms`);
