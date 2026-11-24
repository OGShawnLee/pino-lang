# Global built-in functions

Status: pending regeneration (ticket 009)

Global built-ins are available in every Pino source file without an import. There is no grammar section for them: they are ordinary function calls.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `panic` | `panic(message string)` | `any` |
| `println` | `println(args...)` | `void` |
| `readline` | `readline(prompt string?)` | `Result[string, string]` |
| `int` | `int(val)` | `int` for numeric arguments, `Result[int, string]` for `string` arguments |
| `float` | `float(val)` | `float` for numeric arguments, `Result[float, string]` for `string` arguments |
| `rand` | `rand(limit int?)` | `float` without `limit`, `int` with `limit` |
| `time` | `time()` | `float` |
| `sleep` | `sleep(ms int)` | `void` |
| `type` | `type(val)` | `string` |
| `rune` | `rune(val)` | `rune` |
| `str` | `str(val)` | `string` |
| `clear` | `clear()` | `void` |
| `regex` | `regex(pattern string)` | `regex` |
| `read_file` | `read_file(path string)` | `Result[string, IOError]` |
| `write_file` | `write_file(path string, content string)` | `Result[string, IOError]` |
| `file_exists` | `file_exists(path string)` | `bool` |

Semantics of `panic` are in [`features/errors.md`](../features/errors.md). Semantics of the conversions and inspectors `int`, `float`, `rune`, `str` and `type` are in [`features/types.md`](../features/types.md); this file lists only their signatures.

## Rules
- BUILTIN-1: Every built-in function is globally available in any Pino source file without an import. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"]
- BUILTIN-2: `println(args...)` prints its arguments separated by single spaces and followed by a newline. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"]
- BUILTIN-3: `println()` with no arguments prints only a newline. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"]
- BUILTIN-4: `println` returns no value (`void`). [src: spec/decisions/D-013-null-primitive-versus-option.md] [no-case: void produces no runtime value]
- BUILTIN-5: `rand()` with no argument returns a `float` in the range `[0.0, 1.0)`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestRandFunction]
- BUILTIN-6: `rand(limit)` with an integer `limit` returns an `int` in the range `[0, limit)`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestRandFunction]
- BUILTIN-7: `time()` returns a `float`: the number of milliseconds since the Unix epoch, 1970-01-01 00:00:00 UTC, on every platform (including Windows; it is wall-clock time, not a monotonic boot timer). [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestTimeFunction; spec/decisions/D-186-print-forms-and-ir-prereqs.md]
- BUILTIN-8: `regex(pattern)` returns a `regex` value for the pattern string, and the value's `pattern` property is that string. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestRegexConstructorAndPattern]
- BUILTIN-9: `readline(prompt)` optionally prints `prompt`, then blocks reading one line from standard input and returns `Result[string, string]`, yielding `Result::Failure("There is no more input to read.")` at end of input or on read errors. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; spec/decisions/D-030-readline-end-of-input.md; spec/decisions/D-189-runtime-texts.md] [no-case: reads standard input, which the conformance runner leaves empty]
- BUILTIN-10: `sleep(ms)` pauses the current thread for `ms` milliseconds and returns no value (`void`). [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestSleepFunction; spec/decisions/D-013-null-primitive-versus-option.md] [no-case: the observable effect is elapsed wall-clock time, which is not deterministic]
- BUILTIN-11: `clear()` clears the terminal and returns no value (`void`). [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; spec/decisions/D-013-null-primitive-versus-option.md] [no-case: the effect is on the terminal, not on captured standard output]
- BUILTIN-12: `read_file(path)` returns the whole text content of the file at `path` as a `Result[string, IOError]`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"] [no-case: depends on file-system state outside the case]
- BUILTIN-13: `write_file(path, content)` writes `content` to the file at `path` and returns `Result[string, IOError]`, yielding `Result::Success(path)` on success. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; spec/decisions/D-189-runtime-texts.md] [no-case: writes to the file system]
- BUILTIN-14: `file_exists(path)` returns `true` when a file or directory exists at `path` and `false` otherwise. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"] [no-case: depends on file-system state outside the case]
- BUILTIN-15: `println` and `str` format `bool` values as `true`/`false` and keep a fractional part on whole `float` values (`1.0`, `42.0`). [src: spec/decisions/D-037-bool-text-form.md; spec/decisions/D-022-println-format-of-bool-and-float.md]
- BUILTIN-16: `int(v)` and `float(v)` return a plain `int` or `float` for numeric arguments and `Result[int, string]` or `Result[float, string]` for `string` arguments. [src: spec/decisions/D-038-int-float-return-result.md; spec/decisions/D-015-int-float-return-result.md]
- BUILTIN-17: On failure, `read_file(path)` and `write_file(path, content)` return `Result::Failure(err)` where `err` is `IOError::NotFound(path)`, `IOError::PermissionDenied(path)` (or a host-supplied message when file access is disabled by the host), `IOError::AlreadyExists(path)`, or `IOError::Gremlin(message)` carrying the host's OS error message. [src: spec/decisions/D-189-runtime-texts.md]

## Errors
None documented.

## Examples
Conformance cases: `conformance/stdlib-builtins/`.

## Open questions
None.
