use std::env;
use std::path::Path;

fn fib(n: i64) -> i64 {
    if n < 2 {
        return n;
    }
    fib(n - 1) + fib(n - 2)
}

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let n = if quick { 25 } else { 33 };
    println!("{}", fib(n));
}
