use std::env;
use std::path::Path;

fn my_sqrt(x: f64) -> f64 {
    let mut g = x * 0.5 + 1.0;
    for _ in 0..16 {
        g = 0.5 * (g + x / g);
    }
    g
}

fn round_int(v: f64) -> i64 {
    if v >= 0.0 {
        (v + 0.5) as i64
    } else {
        (v - 0.5) as i64
    }
}

fn eval_a(i: usize, j: usize) -> f64 {
    let ij = i + j;
    1.0 / ((ij * (ij + 1) / 2 + i + 1) as f64)
}

fn eval_a_times_u(u: &[f64], au: &mut [f64], n: usize) {
    for i in 0..n {
        let mut sum = 0.0;
        for j in 0..n {
            sum += eval_a(i, j) * u[j];
        }
        au[i] = sum;
    }
}

fn eval_at_times_u(u: &[f64], au: &mut [f64], n: usize) {
    for i in 0..n {
        let mut sum = 0.0;
        for j in 0..n {
            sum += eval_a(j, i) * u[j];
        }
        au[i] = sum;
    }
}

fn eval_ata_times_u(u: &[f64], out: &mut [f64], tmp: &mut [f64], n: usize) {
    eval_a_times_u(u, tmp, n);
    eval_at_times_u(tmp, out, n);
}

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let n = if quick { 80 } else { 180 };

    let mut u = vec![1.0_f64; n];
    let mut v = vec![0.0_f64; n];
    let mut tmp = vec![0.0_f64; n];

    for _ in 0..10 {
        eval_ata_times_u(&u, &mut v, &mut tmp, n);
        eval_ata_times_u(&v, &mut u, &mut tmp, n);
    }

    let mut vbv = 0.0;
    let mut vv = 0.0;
    for i in 0..n {
        vbv += u[i] * v[i];
        vv += v[i] * v[i];
    }

    let ans = my_sqrt(vbv / vv);
    println!("{}", round_int(ans * 1000000000.0));
}
