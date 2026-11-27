use std::env;
use std::path::Path;
use std::time::Instant;

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let n = if quick { 1500 } else { 7500 };

    let t0 = Instant::now();
    let mut s = String::new();
    for i in 0..n {
        let piece = format!("w{},", i % 1000);
        s = s + &piece;
    }

    let parts: Vec<&str> = s.split(',').collect();
    let joined = parts.join(":");
    let bench_ms = t0.elapsed().as_secs_f64() * 1000.0;

    println!("{}", s.len());
    println!("{}", parts.len());
    println!("{}", joined.len());
    println!("PINO_BENCH_MS={:.3}", bench_ms);
}
