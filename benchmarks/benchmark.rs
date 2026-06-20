// Rust Benchmark: recursive fib(28) and loop of 100,000
use std::time::Instant;

fn fib(n: u32) -> u32 {
    if n <= 1 {
        return n;
    }
    fib(n - 1) + fib(n - 2)
}

fn main() {
    println!("--- BENCHMARK RUST ---");

    // 1. Fibonacci
    let fib_start = Instant::now();
    let fib_result = fib(28);
    let fib_duration = fib_start.elapsed().as_secs_f64() * 1000.0;
    println!("1. Fibonacci(28) recursivo:");
    println!("   Resultado: {}", fib_result);
    println!("   Tiempo: {:.2} ms", fib_duration);

    // 2. Loop
    let loop_start = Instant::now();
    let mut count = 0;
    for _ in 0..100_000 {
        count += 1;
    }
    let loop_duration = loop_start.elapsed().as_secs_f64() * 1000.0;
    println!("2. Bucle simple de 100,000 iteraciones:");
    println!("   Resultado: {}", count);
    println!("   Tiempo: {:.2} ms", loop_duration);
}
