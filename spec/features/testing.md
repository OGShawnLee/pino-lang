# Testing

Status: ReVm 12/12 | ReC 12/12   (from conformance/STATUS.md)

## Syntax
Grammar productions: see `spec/grammar.ebnf`.
A test block is written `test "name" { ... }` at top level, where `"name"` is a string literal and the body contains statements. Inside tests and normal programs, `assert(cond)` or `assert(cond, message)` checks a boolean condition and panics with diagnostic details when it is false.

## Rules
- TEST-1: A `test "<name>" { ... }` block declares a named test at module top level, where `<name>` is a `string` literal and the body is a block of statements. [src: spec/decisions/D-188-pino-test.md]
- TEST-2: `pino test` with no path recursively discovers every `*_test.pino` file, every `.pino` file under any `test/` directory, and every other `.pino` file that contains at least one `test` block, skipping `bin/`, `obj/`, `tooling/` and `.git/`; `pino test <file>` tests one file and `pino test <dir>` searches that directory the same way. [src: spec/decisions/D-188-pino-test.md]
- TEST-3: Discovered test files run in ordinal path order and tests within a file run in source order. [src: spec/decisions/D-188-pino-test.md]
- TEST-4: Each test runs in a fresh program state: imported module initializers and the file's top-level code run before the test body, mutations made by one test are not visible to the next, and `fn main` is not called. [src: spec/decisions/D-188-pino-test.md]
- TEST-5: A test passes when its body completes without panicking and fails on the first failed `assert` or runtime panic; remaining statements in a failing test do not run, while other tests in the file and other files still run. [src: spec/decisions/D-188-pino-test.md]
- TEST-6: A failing `assert(<cond>)` with no message panics with `` `<cond source>` was false. `` (where `<cond source>` is the trimmed source text of `<cond>`), followed when `<cond>` is a comparison (`==`, `!=`, `<`, `<=`, `>`, `>=`) by one line `<side source> is <value>` for each non-literal operand formatted as by `str()` (`STR-5`) with strings quoted; each operand is evaluated once, both inside tests and in normal programs. [src: spec/decisions/D-188-pino-test.md]
- TEST-7: A failing `assert(<cond>, <message>)` panics with `<message>` followed by a newline and the same detail as `TEST-6`. [src: spec/decisions/D-188-pino-test.md]
- TEST-8: A test that finishes with live reference-counted objects still allocated passes with a `warning:` line below `[PASS]` listing each leaked object on its own `- <type>` line in allocation order. [src: spec/decisions/D-188-pino-test.md]
- TEST-9: With `--strict`, a test that finishes with live reference-counted objects still allocated fails (`[FAIL]`) with `error:` instead of `warning:` and causes `pino test` to exit `1`. [src: spec/decisions/D-188-pino-test.md]
- TEST-10: Output printed by a test is captured; it is hidden for a passing test and shown indented under `[FAIL]` after the failure detail for a failing test. [src: spec/decisions/D-188-pino-test.md]
- TEST-11: A panic other than a failing `assert` is shown as `panicked: <message>` under the `[FAIL]` line with the panic's `file:line` in parentheses. [src: spec/decisions/D-188-pino-test.md]
- TEST-12: `pino test` accepts `--vm` (default), `--c` (native code built with debug reference-count leak tracking), `--strict`, and `--filter <text>` (case-insensitive substring match on the test name), producing matching output on both engines. [src: spec/decisions/D-188-pino-test.md]
- TEST-13: `pino test` prints a summary omitting zero parts (`2 passed, 1 failed, 1 with leaks (0.42 s)`), prints `No tests found.` and exits `0` when no tests match, exits `0` when every test passes (warnings allowed), exits `1` when any test fails or a file fails to compile, and exits `2` on bad usage. [src: spec/decisions/D-188-pino-test.md]
- TEST-14: `pino run` and `pino build` ignore `test` blocks. [src: spec/decisions/D-188-pino-test.md]

## Errors
- TEST-E1: A `test` block whose name is not a string literal is rejected as a parse error with a message containing "Expected string literal for test name". [src: spec/decisions/D-188-pino-test.md]
- TEST-E2: Under `pino test`, a file that fails to compile prints its diagnostics under the file header, counts its tests as failed, and causes `pino test` to exit `1`. [src: spec/decisions/D-188-pino-test.md]

## Examples
Conformance cases: `conformance/testing/`.

## Open questions
None.
