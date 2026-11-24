# String methods and properties

Status: pending regeneration (ticket 009)

Members are accessed with the `:` operator on a `string` value, for example `"PINO":lower()` or `s:len`.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `len` / `length` (property) | `s:len`, `s:length` | `int` |
| `lower` | `s:lower()` | `string` |
| `upper` | `s:upper()` | `string` |
| `trim` | `s:trim()` | `string` |
| `contains` | `s:contains(sub string)` | `bool` |
| `split` | `s:split(sep string)` | `[]string` |
| `replace` | `s:replace(old string, new string)` | `string` |
| `substring` | `s:substring(start int, len int)` | `string` |
| `to_owned` / `clone` | `s:to_owned()`, `s:clone()` | `string` |
| `to_cstring` / `cstr` | `s:to_cstring()`, `s:cstr()` | `string` |
| `starts_with` | `s:starts_with(prefix string)` | `bool` |
| `ends_with` | `s:ends_with(suffix string)` | `bool` |
| `index_of` | `s:index_of(sub string)` | `int` |
| `last_index_of` | `s:last_index_of(sub string)` | `int` |
| `trim_start` | `s:trim_start()` | `string` |
| `trim_end` | `s:trim_end()` | `string` |
| `in` (operator) | `sub in s` | `bool` |

## Rules
- STRLIB-1: `s:len` and `s:length` return the number of runes (Unicode code points) in `s`, so `"Hello":len` is `5` and `"🌲":len` is `1`. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringLength]
- STRLIB-2: `s:lower()` returns a new string with all alphabetic characters converted to lowercase. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringLower]
- STRLIB-3: `s:upper()` returns a new string with all alphabetic characters converted to uppercase. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringUpper]
- STRLIB-4: `s:trim()` returns a new string with all leading and trailing whitespace removed. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringTrim]
- STRLIB-5: `s:contains(sub)` returns `true` when `sub` occurs in `s` and `false` otherwise. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringContains]
- STRLIB-6: `s:split(sep)` returns a vector of the substrings of `s` that are separated by `sep`, in order. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringSplit]
- STRLIB-7: `s:replace(old, new)` returns a new string in which every occurrence of `old` is replaced by `new`. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringReplace]
- STRLIB-8: `s:substring(start, len)` returns the part of `s` that starts at index `start` and is `len` long, so `"hello world":substring(6, 5)` is `"world"`. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringSubstring]
- STRLIB-9: `s:to_owned()` and `s:clone()` return an independent copy of the string with the same content, so the copy is equal to the original under `==`. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringToOwnedAndClone]
- STRLIB-10: `s:to_cstring()` and `s:cstr()` return the string content for C interop, and both yield the same value. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringToCString]
- STRLIB-11: `s:starts_with(prefix)` returns `true` when `s` begins with `prefix` and `false` otherwise. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringStartsWith]
- STRLIB-12: `s:ends_with(suffix)` returns `true` when `s` ends with `suffix` and `false` otherwise. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringEndsWith]
- STRLIB-13: `s:index_of(sub)` returns the zero-based index of the first occurrence of `sub` in `s`, or `-1` when `sub` does not occur. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringIndexOf]
- STRLIB-14: `s:last_index_of(sub)` returns the zero-based index of the last occurrence of `sub` in `s`, or `-1` when `sub` does not occur. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringLastIndexOf]
- STRLIB-15: `s:trim_start()` returns a new string with all leading whitespace removed and trailing whitespace kept. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringTrimStart]
- STRLIB-16: `s:trim_end()` returns a new string with all trailing whitespace removed and leading whitespace kept. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; StdlibTests.TestStringTrimEnd]
- STRLIB-17: `sub in s` is `true` when the string `sub` occurs in the string `s` and `false` otherwise. [src: StdlibTests.TestInOperatorString]
- STRLIB-18: `s:substring(start, len)` is an O(1) zero-copy slice that shares the memory of `s` and allocates no heap memory in the native runtime. [src: LANGUAGE_REFERENCE.md §10 "String Methods"; proposals/013-immutable-string-slices-zero-copy.md] [no-case: memory sharing and allocation are not observable from a Pino program]
- STRLIB-19: `s[i]` returns the `rune` at zero-based Unicode code-point index `i`, consistent with `s:len`. [src: spec/decisions/D-023-string-indexing.md]

## Errors
- STRLIB-E1: `s:substring(start, len)` panics at run time (ERR-3, exit code 101 per ERR-5) when `start < 0`, `len < 0`, or `start + len > s:len` with `Substring starting at <start> with length <len> is out of range for a string of length <s:len>.`. [src: spec/decisions/D-028-substring-out-of-range.md; spec/decisions/D-189-runtime-texts.md]
- STRLIB-E2: Indexing a string `s[i]` with `i < 0` or `i >= s:len` panics at run time (ERR-3, exit code 101 per ERR-5) with `Index <i> is out of range for a string of length <s:len>.`. [src: spec/decisions/D-023-string-indexing.md; spec/decisions/D-189-runtime-texts.md]

## Examples
Conformance cases: `conformance/stdlib-string/`.

## Open questions
None.
