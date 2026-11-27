use regex::Regex;
use std::fs;
use std::time::Instant;

fn main() {
    let text = fs::read_to_string("benchmarks/data/regex.txt")
        .expect("failed to read benchmarks/data/regex.txt");

    let t0 = Instant::now();
    let email_re = Regex::new(r"\w+@\w+\.com").unwrap();
    let ident_re = Regex::new(r"[A-Za-z_][A-Za-z0-9_]*").unwrap();

    let emails = email_re.find_iter(&text).count();
    let idents = ident_re.find_iter(&text).count();
    let bench_ms = t0.elapsed().as_secs_f64() * 1000.0;

    println!("{}", emails);
    println!("{}", idents);
    println!("PINO_BENCH_MS={:.3}", bench_ms);
}
