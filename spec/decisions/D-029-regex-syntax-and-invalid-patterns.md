# D-029: Regular expression syntax and invalid patterns

Status: open
Raised by: 005
Feature: spec/stdlib/regex.md

## Question
Which regular expression syntax does `regex(pattern)` accept, and what happens when the pattern is invalid?

## Evidence
- Reference: §9 `regex(pattern)` and §10 "Regex Methods" never name the syntax or the failure mode (`LANGUAGE_REFERENCE.md:985-989`, `LANGUAGE_REFERENCE.md:1113-1146`).
- Tests: `StdlibTests.TestRegexConstructorAndPattern` and the other regex tests use only `[0-9]+` (`StdlibTests.cs:196-270`).
- Implementation: the tree-walk interpreter builds a .NET `Regex` (`Evaluator.BuiltIns.cs`, `RegexFunction`); the C backend uses its own matcher in `runtime/re.c`.

Minimal snippet:

```pino
val r = regex("[")
println(r:has_match("a"))
```

| Engine | Actual output |
| :--- | :--- |
| tree-walk | `System.Text.RegularExpressions.RegexParseException: Invalid pattern '[' at offset 1. Unterminated [] set.` |
| VM | the same `RegexParseException` |
| C | `True` (no error; the pattern is accepted) |

## Options
1. Define a small portable subset (character classes, `+ * ?`, anchors, groups) and make an invalid pattern a catchable error: engines agree; needs a matcher that follows the subset.
2. Adopt a standard syntax (for example POSIX ERE) and make an invalid pattern fail `regex(...)`: well known; the .NET engine must be restricted or replaced.
3. Return a `Result[regex, string]` from `regex(pattern)`: explicit failure; changes the documented return type.

## Decision
_Pending: owner decides._
