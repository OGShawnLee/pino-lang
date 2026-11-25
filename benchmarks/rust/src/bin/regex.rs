use regex::Regex;
use std::fs;

fn main() {
    let text = fs::read_to_string("benchmarks/data/regex.txt")
        .expect("failed to read benchmarks/data/regex.txt");

    let email_re = Regex::new(r"\w+@\w+\.com").unwrap();
    let ident_re = Regex::new(r"[A-Za-z_][A-Za-z0-9_]*").unwrap();

    let emails = email_re.find_iter(&text).count();
    let idents = ident_re.find_iter(&text).count();

    println!("{}", emails);
    println!("{}", idents);
}
