use std::env;
use std::path::Path;

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let size = if quick { 150 } else { 335 };
    let max_iter = 50;
    let inv_size = 1.0 / (size as f64);

    let mut inside: i64 = 0;
    for y in 0..size {
        let ci = 2.0 * (y as f64) * inv_size - 1.0;
        for x in 0..size {
            let cr = 2.0 * (x as f64) * inv_size - 1.5;
            let mut zr = 0.0_f64;
            let mut zi = 0.0_f64;
            let mut escaped = false;
            for _ in 0..max_iter {
                let tr = zr * zr - zi * zi + cr;
                let ti = 2.0 * zr * zi + ci;
                zr = tr;
                zi = ti;
                if zr * zr + zi * zi > 4.0 {
                    escaped = true;
                    break;
                }
            }
            if !escaped {
                inside += 1;
            }
        }
    }
    println!("{}", inside);
}
