use std::env;
use std::path::Path;
use std::time::Instant;

fn fib(n: i64) -> i64 {
    if n < 2 {
        return n;
    }
    fib(n - 1) + fib(n - 2)
}

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let n = if quick { 25 } else { 29 };
    let t0 = Instant::now();
    let ans = fib(n);
    let bench_ms = t0.elapsed().as_secs_f64() * 1000.0;
    println!("{}", ans);
    println!("PINO_BENCH_MS={:.3}", bench_ms);
}
