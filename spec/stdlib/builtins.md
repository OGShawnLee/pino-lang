# Global built-in functions

Status: pending regeneration (ticket 009)

Global built-ins are available in every Pino source file without an import. There is no grammar section for them: they are ordinary function calls.

## Signatures

| Member | Signature | Returns |
| :--- | :--- | :--- |
| `panic` | `panic(message string)` | `any` |
| `println` | `println(args...)` | `null` |
| `readline` | `readline(prompt string?)` | `string` |
| `int` | `int(val)` | `int` |
| `float` | `float(val)` | `float` |
| `rand` | `rand(limit int?)` | `float` without `limit`, `int` with `limit` |
| `time` | `time()` | `float` |
| `sleep` | `sleep(ms int)` | `null` |
| `type` | `type(val)` | `string` |
| `rune` | `rune(val)` | `rune` |
| `str` | `str(val)` | `string` |
| `clear` | `clear()` | `null` |
| `regex` | `regex(pattern string)` | `regex` |
| `read_file` | `read_file(path string)` | `Result[string, IOError]` |
| `write_file` | `write_file(path string, content string)` | `Result[string, IOError]` |
| `file_exists` | `file_exists(path string)` | `bool` |

Semantics of `panic` are in [`features/errors.md`](../features/errors.md). Semantics of the conversions and inspectors `int`, `float`, `rune`, `str` and `type` are in [`features/types.md`](../features/types.md); this file lists only their signatures. The `Returns` column of `int` and `float` follows the reference; see D-038 for what the implementation returns.

## Rules
- BUILTIN-1: Every built-in function is globally available in any Pino source file without an import. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"]
- BUILTIN-2: `println(args...)` prints its arguments separated by single spaces and followed by a newline. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"]
- BUILTIN-3: `println()` with no arguments prints only a newline. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"]
- BUILTIN-4: `println` returns `null`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"]
- BUILTIN-5: `rand()` with no argument returns a `float` in the range `[0.0, 1.0)`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestRandFunction]
- BUILTIN-6: `rand(limit)` with an integer `limit` returns an `int` in the range `[0, limit)`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestRandFunction]
- BUILTIN-7: `time()` returns a `float`: the number of milliseconds since the Unix epoch, 1970-01-01 00:00:00 UTC. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestTimeFunction]
- BUILTIN-8: `regex(pattern)` returns a `regex` value for the pattern string, and the value's `pattern` property is that string. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestRegexConstructorAndPattern]
- BUILTIN-9: `readline(prompt)` optionally prints `prompt`, then blocks reading one line from standard input and returns it as a `string`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"] [no-case: reads standard input, which the conformance runner leaves empty; the end-of-input result is open in D-030]
- BUILTIN-10: `sleep(ms)` pauses the current thread for `ms` milliseconds and returns `null`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"; StdlibTests.TestSleepFunction] [no-case: the observable effect is elapsed wall-clock time, which is not deterministic]
- BUILTIN-11: `clear()` clears the terminal and returns `null`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"] [no-case: the effect is on the terminal, not on captured standard output]
- BUILTIN-12: `read_file(path)` returns the whole text content of the file at `path` as a `Result[string, IOError]`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"] [no-case: depends on file-system state outside the case]
- BUILTIN-13: `write_file(path, content)` writes `content` to the file at `path` and returns a `Result[string, IOError]`. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"] [no-case: writes to the file system]
- BUILTIN-14: `file_exists(path)` returns `true` when a file or directory exists at `path` and `false` otherwise. [src: LANGUAGE_REFERENCE.md §9 "Global Built-in Functions"] [no-case: depends on file-system state outside the case]

## Errors
None documented.

## Examples
Conformance cases: `conformance/stdlib-builtins/`.

## Open questions
- D-030: What does `readline()` return at end of input? Today the tree-walker returns a value that never equals any prompt answer, so a `for` loop that waits for a line loops forever.
- D-037: How must a `bool` be printed by `println` and `str`? The tests assert `True` and `False`, and the reference does not say.
- D-038: Do `int(...)` and `float(...)` return the plain number, as the reference says, or a `Result`, as the implementation does?
