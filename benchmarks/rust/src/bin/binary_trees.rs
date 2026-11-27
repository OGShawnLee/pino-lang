use std::env;
use std::path::Path;
use std::time::Instant;

enum Tree {
    Leaf,
    Node(Box<Tree>, Box<Tree>),
}

fn make_tree(depth: i32) -> Tree {
    if depth <= 0 {
        Tree::Leaf
    } else {
        Tree::Node(Box::new(make_tree(depth - 1)), Box::new(make_tree(depth - 1)))
    }
}

fn check_tree(t: &Tree) -> i64 {
    match t {
        Tree::Leaf => 1,
        Tree::Node(left, right) => 1 + check_tree(left) + check_tree(right),
    }
}

fn pow2(exp: i32) -> i32 {
    let mut r = 1;
    for _ in 0..exp {
        r *= 2;
    }
    r
}

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let min_depth = 4;
    let max_depth = if quick { 9 } else { 12 };
    let stretch_depth = max_depth + 1;

    let t0 = Instant::now();
    let stretch_check = check_tree(&make_tree(stretch_depth));
    let long_lived = make_tree(max_depth);

    let mut total_check = stretch_check;
    let mut d = min_depth;
    while d <= max_depth {
        let iterations = pow2(max_depth - d + min_depth);
        let mut check: i64 = 0;
        for _ in 0..iterations {
            check += check_tree(&make_tree(d));
        }
        total_check += check;
        d += 2;
    }

    let long_check = check_tree(&long_lived);
    total_check += long_check;
    let bench_ms = t0.elapsed().as_secs_f64() * 1000.0;

    println!("{}", stretch_check);
    println!("{}", total_check);
    println!("{}", long_check);
    println!("PINO_BENCH_MS={:.3}", bench_ms);
}
