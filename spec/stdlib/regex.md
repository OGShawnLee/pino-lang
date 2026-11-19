# Regular expressions

Status: pending regeneration (ticket 009)

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

## Rules
- RGX-1: `regex(pattern)` compiles the pattern string and returns a value whose `type` is `"regex"`. [src: LANGUAGE_REFERENCE.md §9 "`regex(pattern)`"; StdlibTests.TestRegexConstructorAndPattern]
- RGX-2: `r:pattern` returns the original pattern string that `r` was compiled from. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexConstructorAndPattern]
- RGX-3: `r:has_match(text)` returns `true` when the pattern matches anywhere in `text` and `false` otherwise. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexHasMatch]
- RGX-4: `r:match_prefix(text)` matches the pattern anchored at the beginning of `text` and returns the matched substring, or `""` when `text` does not start with a match. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexMatchPrefix]
- RGX-5: `r:find(text)` returns the first match of the pattern anywhere in `text`, or `""` when there is none. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexFind]
- RGX-6: `r:find_all(text)` returns a vector of all non-overlapping matches of the pattern in `text`, in order. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexFindAll]
- RGX-7: `r:replace(text, repl)` returns a new string in which every match of the pattern in `text` is replaced by `repl`. [src: LANGUAGE_REFERENCE.md §10 "Regex Methods"; StdlibTests.TestRegexReplace]

## Errors
None documented.

## Examples
Conformance cases: `conformance/stdlib-regex/`.

## Open questions
- D-029: The reference does not say which regular expression syntax `regex(pattern)` accepts (the tree-walk interpreter uses .NET regular expressions, the C runtime its own matcher `runtime/re.c`) or what an invalid pattern does.
