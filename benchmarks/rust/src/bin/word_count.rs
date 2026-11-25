use std::collections::{HashMap, HashSet};
use std::fs;

fn main() {
    let text = fs::read_to_string("benchmarks/data/word-count.txt")
        .expect("failed to read benchmarks/data/word-count.txt");

    let mut counts: HashMap<&str, i64> = HashMap::new();
    let mut total: i64 = 0;

    for line in text.split('\n') {
        for w in line.split(' ') {
            if w.is_empty() {
                continue;
            }
            total += 1;
            *counts.entry(w).or_insert(0) += 1;
        }
    }

    println!("{}", total);
    println!("{}", counts.len());

    let mut used: HashSet<&str> = HashSet::new();
    let keys: Vec<&str> = counts.keys().copied().collect();
    let limit = keys.len().min(10);

    for _ in 0..limit {
        let mut best_word = "";
        let mut best_count: i64 = -1;
        for &k in &keys {
            if used.contains(k) {
                continue;
            }
            let c = counts[k];
            if c > best_count || (c == best_count && k < best_word) {
                best_count = c;
                best_word = k;
            }
        }
        used.insert(best_word);
        println!("{} {}", best_word, best_count);
    }
}
