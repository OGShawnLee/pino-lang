// C# Benchmark: recursive fib(28) and loop of 100,000
using System;
using System.Diagnostics;

Console.WriteLine("--- BENCHMARK C# ---");

// 1. Fibonacci
var fibStopwatch = Stopwatch.StartNew();
long fibResult = Fib(28);
fibStopwatch.Stop();
Console.WriteLine("1. Fibonacci(28) recursivo:");
Console.WriteLine($"   Resultado: {fibResult}");
Console.WriteLine($"   Tiempo: {fibStopwatch.Elapsed.TotalMilliseconds:F2} ms");

// 2. Loop
var loopStopwatch = Stopwatch.StartNew();
int count = 0;
for (int i = 0; i < 100000; i++) {
    count += 1;
}
loopStopwatch.Stop();
Console.WriteLine("2. Bucle simple de 100,000 iteraciones:");
Console.WriteLine($"   Resultado: {count}");
Console.WriteLine($"   Tiempo: {loopStopwatch.Elapsed.TotalMilliseconds:F2} ms");

static long Fib(long n) {
    if (n <= 1) return n;
    return Fib(n - 1) + Fib(n - 2);
}
