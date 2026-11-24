# Regular expressions

A regular expression is a value of type `regex` created with the global function `regex(pattern)`. Its members are accessed with the `:` operator, for example `r:has_match("abc123")`.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `regex` (constructor) | `regex(pattern string)` | `regex` |
| `pattern` (property) | `r:pattern` | `string` |
| `has_match` | `r:has_match(text string)` | `bool` |
| `match_prefix` | `r:match_prefix(text string)` | `string` |
| `find` | `r:find(text string)` | `string` |
| `find_all` | `r:find_all(text string)` | `[]string` |
| `replace` | `r:replace(text string, repl string)` | `string` |

## Syntax

| Feature | Syntax |
| :--- | :--- |
| Literals | any rune; escape the metacharacters `\ . + * ? ( ) [ ] { } ^ $ \|` with `\` |
| Escapes | `\n \t \r \f \v \0`, `\x{10FFFF}` and `\xFF` (code point), `\\` |
| Any rune | `.` (not `\n`, unless `(?s)`) |
| Classes | `[abc]`, `[a-z]`, `[^a-z]`, `]` first or escaped, `-` first/last or escaped; shorthands work inside classes |
| Shorthands | `\d` = `[0-9]`, `\w` = `[0-9A-Za-z_]`, `\s` = `[\t\n\f\r ]`, and `\D \W \S` (ASCII) |
| Anchors | `^`, `$` (text start/end; line start/end with `(?m)`), `\b`, `\B` (ASCII word boundary), `\A`, `\z` |
| Groups | `(...)`, non-capturing `(?:...)` |
| Alternation | `a\|b` |
| Repetition | `* + ?`, `{n}`, `{n,}`, `{n,m}` (`n, m <= 1000`), and lazy `*? +? ?? {n,m}?` |
| Flags | `(?i)`, `(?m)`, `(?s)`, combined like `(?is)`, negated like `(?-i)`, scoped `(?i:...)` |

## Rules
- RGX-1: `regex(pattern)` compiles the pattern string and returns a value whose `type` is `"regex"`. [src: LANGUAGE_REFERENCE.md §9 "`regex(pattern)`"; StdlibTests.TestRegexConstructorAndPattern]
- RGX-2: `r:pattern` returns the original pattern string that `r` was compiled from. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexConstructorAndPattern]
- RGX-3: `r:has_match(text)` returns `true` when the pattern matches anywhere in `text` and `false` otherwise. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexHasMatch]
- RGX-4: `r:match_prefix(text)` matches the pattern anchored at the beginning of `text` and returns the matched substring, or `""` when `text` does not start with a match. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexMatchPrefix]
- RGX-5: `r:find(text)` returns the first match of the pattern anywhere in `text`, or `""` when there is none. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexFind]
- RGX-6: `r:find_all(text)` returns a vector of all non-overlapping matches of the pattern in `text`, in order. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexFindAll]
- RGX-7: `r:replace(text, repl)` returns a new string in which every match of the pattern in `text` is replaced by `repl`. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexReplace]
- RGX-8: Patterns match Unicode code points (runes), decoding UTF-8 strings rune by rune. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-9: Any non-metacharacter rune matches itself literally, and any metacharacter (`\ . + * ? ( ) [ ] { } ^ $ |`) escaped with `\` matches itself literally. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-10: Escape sequences `\n`, `\t`, `\r`, `\f`, `\v`, `\0`, `\xFF`, `\x{10FFFF}`, and `\\` match their corresponding code points. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-11: `.` matches any single rune except `\n`, or any rune including `\n` when the `(?s)` flag is active. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-12: Character classes `[abc]`, `[a-z]`, and negated classes `[^a-z]` match one rune in or outside the specified set, treating a leading `]` or a leading, trailing, or escaped `-` as literal, and allowing shorthand classes inside brackets. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-13: Shorthand classes `\d` (`[0-9]`), `\w` (`[0-9A-Za-z_]`), `\s` (`[\t\n\f\r ]`), and their negations `\D`, `\W`, and `\S` match ASCII sets. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-14: Anchors `^` and `$` match the start and end of text (or of each `\n`-delimited line under `(?m)`), `\A` and `\z` always match the start and end of text, and `\b` and `\B` match ASCII word boundaries and non-boundaries. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-15: Groups `(...)` and non-capturing groups `(?:...)` group subexpressions, and `a|b` matches either branch in order. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-16: Quantifiers `*`, `+`, `?`, `{n}`, `{n,}`, and `{n,m}` (with `n, m <= 1000`) repeat greedily, and their `?`-suffixed forms repeat lazily. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-17: Inline flags `(?i)`, `(?m)`, `(?s)`, combined flags such as `(?is)`, negated flags such as `(?-i)`, and scoped groups such as `(?i:...)` control case-insensitive, multiline, and dot-matches-newline modes. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-18: Matching uses leftmost-first semantics: among matches starting at the earliest possible position, the matcher chooses the first branch or quantifier path in pattern order. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-19: Case-insensitive matching `(?i)` uses simple one-to-one Unicode case folding across both literals and character classes. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-20: After an empty match in `find_all` or `replace`, scanning advances by one rune, and an empty match at the end position of an immediately preceding non-empty match is skipped. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-21: `r:replace(text, repl)` inserts `repl` literally without interpreting `$1` or other replacement escapes. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-22: Matching runs in time linear in the length of the input text for every valid pattern, preventing catastrophic backtracking. [src: spec/decisions/D-190-regex-syntax.md]

## Errors
- RGX-E1: `regex(pattern)` with an invalid pattern panics with `Invalid pattern '<pattern>': <reason>.`. [src: spec/decisions/D-029-regex-syntax-and-invalid-patterns.md; spec/decisions/D-190-regex-syntax.md]
- RGX-E2: An unterminated character class panics with reason `missing ]`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E3: An unclosed group panics with reason `missing )`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E4: An unmatched closing parenthesis panics with reason `unexpected )`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E5: A quantifier with no preceding repeatable atom panics with reason `nothing to repeat before '<op>'`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E6: An unrecognized or malformed escape sequence panics with reason `invalid escape '\<c>'`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E7: A malformed `{...}` repetition specifier or reversed bounds `{n,m}` with `n > m` panics with reason `invalid repetition count {<text>}`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E8: A repetition count above `1000` panics with reason `repetition count above 1000`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E9: A reversed character class range `a-b` with `a > b` panics with reason `invalid character range <a>-<b>`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E10: A backreference `\1` through `\9` panics with reason `backreferences like '\<n>' aren't supported; Pino's regex always runs in linear time`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E11: A lookaround prefix (`(?=`, `(?!`, `(?<=`, or `(?<!`) panics with reason `lookaround like '<prefix>' isn't supported; Pino's regex always runs in linear time`. [src: spec/decisions/D-190-regex-syntax.md]
- RGX-E12: An unrecognized flag character in `(?...)` panics with reason `unknown flag '<c>'`. [src: spec/decisions/D-190-regex-syntax.md]

## Examples
Conformance cases: `conformance/stdlib-regex/`.

## Open questions
- None (syntax and semantics are decided in `D-190`; capture groups and replacement group references are deferred to a future groups API).
