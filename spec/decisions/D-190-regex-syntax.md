# D-190: Regular expression syntax, semantics, and linear-time engine

Status: accepted
Raised by: 086
Feature: spec/stdlib/regex.md

## Question
What regular expression syntax, matching semantics, replacement behavior, and error messages must `regex(pattern)` support identically across both execution engines (`ReVm` and `ReC`)?

## Evidence
- `D-029` left the regular expression syntax open: the VM delegated to .NET `System.Text.RegularExpressions` (a backtracking engine with .NET syntax and exponential worst-case runtime) while the C runtime delegated to `runtime/re.c` (derived from SLRE with an unknown licence).
- The two engines disagreed on syntax, Unicode handling, empty matches, anchors, and invalid-pattern diagnostics.

## Decision
Decided by the owner on 2026-11-24 (ticket 086):

1. **RE2/Go-style syntax, guaranteed linear time.** Pino implements its own linear-time regex engine from scratch in both runtimes (`Pino.Vm/Regex/` in C# and `runtime-re/pino_regex.c` in C). Backreferences and lookaround are rejected at compile time so no pattern can cause catastrophic backtracking.
2. **Literal replacement.** `r:replace(text, repl)` inserts `repl` literally without interpreting `$1` or other replacement escapes (group references wait for a future groups API).
3. **Replace `runtime/re.c`.** `runtime/re.c` and `runtime/re.h` are deleted, and `System.Text.RegularExpressions` is removed from `Pino.Vm`.
4. **Performance target.** Guaranteed linear time in input length on every valid pattern, with literal/prefix fast paths and a lazy DFA alongside the Pike VM to stay competitive with Go's `regexp` on common workloads.

### Normative syntax and semantics
- **Unit:** Unicode code points (runes), decoded from UTF-8 strings (`RGX-8`).
- **Literals and escapes:** Any non-metacharacter rune matches itself; `\ . + * ? ( ) [ ] { } ^ $ |` escape with `\`; `\n \t \r \f \v \0`, `\xFF`, `\x{10FFFF}`, and `\\` match their code points (`RGX-9`, `RGX-10`).
- **Any rune:** `.` matches any rune except `\n`, or any rune including `\n` under `(?s)` (`RGX-11`).
- **Character classes:** `[abc]`, `[a-z]`, `[^a-z]`, leading `]` or leading/trailing/escaped `-` as literal, shorthands allowed inside brackets (`RGX-12`).
- **Shorthands (ASCII, like RE2):** `\d` (`[0-9]`), `\w` (`[0-9A-Za-z_]`), `\s` (`[\t\n\f\r ]`), and `\D`, `\W`, `\S` (`RGX-13`).
- **Anchors:** `^`, `$` (text start/end; `\n`-delimited line start/end under `(?m)`), `\b`, `\B` (ASCII word boundary), `\A`, `\z` (`RGX-14`).
- **Groups and alternation:** `(...)`, `(?:...)`, `a|b` (`RGX-15`).
- **Repetition:** `*`, `+`, `?`, `{n}`, `{n,}`, `{n,m}` (`n, m <= 1000`), and lazy `*?`, `+?`, `??`, `{n,m}?` (`RGX-16`).
- **Flags:** `(?i)`, `(?m)`, `(?s)`, combined `(?is)`, negated `(?-i)`, and scoped `(?i:...)` (`RGX-17`).
- **Matching semantics:** Leftmost-first (Perl/Go) (`RGX-18`).
- **Case folding:** `(?i)` uses simple one-to-one Unicode case folding (`RGX-19`).
- **Empty matches in `find_all` and `replace`:** After an empty match, advance one rune; skip an empty match immediately at the end position of a preceding non-empty match (matching Go's `regexp`, so `regex("a*"):replace("baaac", "-")` is `"-b-c-"`) (`RGX-20`).
- **Invalid patterns:** Panic with `Invalid pattern '<pattern>': <reason>.` using the exact reason texts in `RGX-E2`..`RGX-E12`.
