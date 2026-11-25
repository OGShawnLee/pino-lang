use std::env;
use std::path::Path;

fn quicksort(arr: &mut [i64], lo: isize, hi: isize) {
    if lo >= hi {
        return;
    }
    let pivot = arr[(lo + (hi - lo) / 2) as usize];
    let mut i = lo;
    let mut j = hi;
    while i <= j {
        while arr[i as usize] < pivot {
            i += 1;
        }
        while arr[j as usize] > pivot {
            j -= 1;
        }
        if i <= j {
            arr.swap(i as usize, j as usize);
            i += 1;
            j -= 1;
        }
    }
    if lo < j {
        quicksort(arr, lo, j);
    }
    if i < hi {
        quicksort(arr, i, hi);
    }
}

fn main() {
    let quick = env::args().skip(1).any(|a| a == "--quick")
        || Path::new("benchmarks/data/quick").exists();
    let n: usize = if quick { 5000 } else { 100000 };

    let mut state: i64 = 123456789;
    let mut arr = Vec::with_capacity(n);
    for _ in 0..n {
        state = (state * 1664525 + 1013904223) % 2147483648;
        arr.push(state);
    }

    quicksort(&mut arr, 0, (n - 1) as isize);

    let mut checksum: i64 = 0;
    for (i, &v) in arr.iter().enumerate() {
        checksum = (checksum + ((i as i64 + 1) * (v % 1000003))) % 1000000007;
    }

    println!("{}", arr[0]);
    println!("{}", arr[n / 2]);
    println!("{}", arr[n - 1]);
    println!("{}", checksum);
}
