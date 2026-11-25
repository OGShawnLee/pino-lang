use std::fs;

#[allow(dead_code)]
#[derive(Debug, Clone)]
enum Atom<'a> {
    Keyword(&'a str),
    Identifier(&'a str),
    LiteralInt(i64),
    LiteralBool(bool),
    Marker(&'static str),
    EOF,
}

#[derive(Debug, Clone)]
struct Particle<'a> {
    #[allow(dead_code)]
    line: usize,
    #[allow(dead_code)]
    column: usize,
    #[allow(dead_code)]
    len: usize,
    atom: Atom<'a>,
}

fn is_ident_char(c: u8) -> bool {
    (b'a'..=b'z').contains(&c) || (b'A'..=b'Z').contains(&c) || c == b'_'
}

fn is_num_char(c: u8) -> bool {
    (b'0'..=b'9').contains(&c)
}

fn get_marker(c: u8) -> Option<&'static str> {
    match c {
        b'(' => Some("LPAREN"),
        b')' => Some("RPAREN"),
        b'[' => Some("LBRACKET"),
        b']' => Some("RBRACKET"),
        b'{' => Some("LBRACE"),
        b'}' => Some("RBRACE"),
        b':' => Some("COLON"),
        b',' => Some("COMMA"),
        _ => None,
    }
}

fn is_keyword(s: &str) -> bool {
    matches!(
        s,
        "val"
            | "var"
            | "fn"
            | "return"
            | "if"
            | "then"
            | "else"
            | "match"
            | "when"
            | "yield"
            | "for"
            | "in"
            | "break"
            | "continue"
            | "struct"
            | "enum"
            | "union"
            | "interface"
            | "pub"
            | "is"
            | "static"
            | "module"
            | "from"
            | "import"
    )
}

fn lex_program<'a>(program: &'a str) -> Vec<Particle<'a>> {
    let bytes = program.as_bytes();
    let total_len = bytes.len();
    let mut atom_list = Vec::with_capacity(total_len / 6);

    let mut line_num = 1;
    let mut column = 1;
    let mut identifier_len = 0;
    let mut numerical_len = 0;

    let push_identifier =
        |i: usize, length: usize, line: usize, col: usize, list: &mut Vec<Particle<'a>>| {
            let begin = i - length;
            let sub = &program[begin..i];
            let atom = if sub.len() <= 10 && is_keyword(sub) {
                Atom::Keyword(sub)
            } else if sub == "true" {
                Atom::LiteralBool(true)
            } else if sub == "false" {
                Atom::LiteralBool(false)
            } else {
                Atom::Identifier(sub)
            };
            list.push(Particle {
                line,
                column: col - length,
                len: length,
                atom,
            });
        };

    let push_numerical =
        |i: usize, length: usize, line: usize, col: usize, list: &mut Vec<Particle<'a>>| {
            let begin = i - length;
            let sub = &program[begin..i];
            let n = sub.parse::<i64>().unwrap_or(0);
            list.push(Particle {
                line,
                column: col - length,
                len: length,
                atom: Atom::LiteralInt(n),
            });
        };

    for i in 0..total_len {
        let c = bytes[i];

        if is_ident_char(c) {
            identifier_len += 1;
            column += 1;
            continue;
        } else if identifier_len > 0 {
            push_identifier(i, identifier_len, line_num, column, &mut atom_list);
            identifier_len = 0;
        }

        if is_num_char(c) {
            numerical_len += 1;
            column += 1;
            continue;
        } else if numerical_len > 0 {
            push_numerical(i, numerical_len, line_num, column, &mut atom_list);
            numerical_len = 0;
        }

        if c == b'\n' {
            line_num += 1;
            column = 1;
            continue;
        }

        if c == b' ' || c == b'\t' {
            column += 1;
            continue;
        }

        if let Some(m) = get_marker(c) {
            atom_list.push(Particle {
                line: line_num,
                column,
                len: 1,
                atom: Atom::Marker(m),
            });
            column += 1;
            continue;
        }

        column += 1;
    }

    if identifier_len > 0 {
        push_identifier(total_len, identifier_len, line_num, column, &mut atom_list);
    } else if numerical_len > 0 {
        push_numerical(total_len, numerical_len, line_num, column, &mut atom_list);
    }

    atom_list.push(Particle {
        line: line_num,
        column,
        len: 1,
        atom: Atom::EOF,
    });

    atom_list
}

fn main() {
    let content = fs::read_to_string("benchmarks/data/lexer.txt")
        .expect("failed to read benchmarks/data/lexer.txt");

    let particles = lex_program(&content);

    let mut kw_count: i64 = 0;
    let mut ident_count: i64 = 0;
    let mut int_sum: i64 = 0;
    let mut bool_true: i64 = 0;
    let mut marker_count: i64 = 0;

    for p in &particles {
        match p.atom {
            Atom::Keyword(_) => kw_count += 1,
            Atom::Identifier(_) => ident_count += 1,
            Atom::LiteralInt(n) => int_sum = (int_sum + n) % 1000000007,
            Atom::LiteralBool(b) => {
                if b {
                    bool_true += 1;
                }
            }
            Atom::Marker(_) => marker_count += 1,
            Atom::EOF => {}
        }
    }

    println!("{}", particles.len());
    println!("{}", kw_count);
    println!("{}", ident_count);
    println!("{}", int_sum);
    println!("{}", bool_true);
    println!("{}", marker_count);
}
