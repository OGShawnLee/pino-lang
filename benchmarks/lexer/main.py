import time

MARKER_MAP = {
    '(': "LPAREN", ')': "RPAREN",
    '[': "LBRACKET", ']': "RBRACKET",
    '{': "LBRACE", '}': "RBRACE",
    ':': "COLON", ',': "COMMA",
}

KEYWORDS = {
    "val", "var", "fn", "return", "if", "then", "else", "match", "when",
    "yield", "for", "in", "break", "continue", "struct", "enum", "union",
    "interface", "pub", "is", "static", "module", "from", "import",
}

class Particle:
    __slots__ = ("line", "column", "len", "atom")
    def __init__(self, line, column, length, atom):
        self.line = line
        self.column = column
        self.len = length
        self.atom = atom

def is_ident_char(c: str) -> bool:
    return ('a' <= c <= 'z') or ('A' <= c <= 'Z') or c == '_'

def is_num_char(c: str) -> bool:
    return '0' <= c <= '9'

def lex_program(program: str):
    atom_list = []
    line_num = 1
    column = 1
    identifier_len = 0
    numerical_len = 0

    def push_identifier(i: int, length: int) -> None:
        begin = i - length
        val = program[begin:i]
        if len(val) <= 10 and val in KEYWORDS:
            atom = ("Keyword", val)
        elif val == "true" or val == "false":
            atom = ("Bool", val == "true")
        else:
            atom = ("Identifier", val)
        atom_list.append(Particle(line_num, column - length, length, atom))

    def push_numerical(i: int, length: int) -> None:
        begin = i - length
        val = program[begin:i]
        atom_list.append(Particle(line_num, column - length, length, ("Int", int(val))))

    for i, c in enumerate(program):
        if is_ident_char(c):
            identifier_len += 1
            column += 1
            continue
        elif identifier_len > 0:
            push_identifier(i, identifier_len)
            identifier_len = 0

        if is_num_char(c):
            numerical_len += 1
            column += 1
            continue
        elif numerical_len > 0:
            push_numerical(i, numerical_len)
            numerical_len = 0

        if c == '\n':
            line_num += 1
            column = 1
            continue

        if c == ' ' or c == '\t':
            column += 1
            continue

        if c in MARKER_MAP:
            atom_list.append(Particle(line_num, column, 1, ("Marker", MARKER_MAP[c])))
            column += 1
            continue

        column += 1

    total_len = len(program)
    if identifier_len > 0:
        push_identifier(total_len, identifier_len)
    elif numerical_len > 0:
        push_numerical(total_len, numerical_len)

    atom_list.append(Particle(line_num, column, 1, ("EOF", None)))
    return atom_list

def main() -> None:
    with open("benchmarks/data/lexer.txt", "r", encoding="utf-8") as f:
        content = f.read()

    t0 = time.perf_counter()
    particles = lex_program(content)

    kw_count = 0
    ident_count = 0
    int_sum = 0
    bool_true = 0
    marker_count = 0

    for p in particles:
        kind = p.atom[0]
        if kind == "Keyword":
            kw_count += 1
        elif kind == "Identifier":
            ident_count += 1
        elif kind == "Int":
            int_sum = (int_sum + p.atom[1]) % 1000000007
        elif kind == "Bool":
            if p.atom[1]:
                bool_true += 1
        elif kind == "Marker":
            marker_count += 1
    total_particles = len(particles)
    bench_ms = (time.perf_counter() - t0) * 1000.0

    print(total_particles)
    print(kw_count)
    print(ident_count)
    print(int_sum)
    print(bool_true)
    print(marker_count)
    print(f"PINO_BENCH_MS={bench_ms:.3f}")

if __name__ == "__main__":
    main()
