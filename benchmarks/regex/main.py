import re

def main() -> None:
    with open("benchmarks/data/regex.txt", "r", encoding="utf-8") as f:
        text = f.read()

    email_re = re.compile(r"\w+@\w+\.com")
    ident_re = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")

    emails = email_re.findall(text)
    idents = ident_re.findall(text)

    print(len(emails))
    print(len(idents))

if __name__ == "__main__":
    main()
