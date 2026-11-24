# D-189: Runtime panic texts, error payloads, and float print form

Status: accepted
Raised by: 083
Feature: spec/features/types.md, spec/features/vectors.md, spec/features/maps.md, spec/stdlib/string.md, spec/stdlib/builtins.md

## Question
What exact texts must both execution engines (`ReVm` and `ReC`) produce for runtime panics, runtime `Result::Failure` / `IOError` / `write_file` payloads, and `float` printing (`println` / `str`)?

## Evidence
- **Panic texts differed** between `ReVm` and `ReC`:
  - Vector index out of range: `ReVm` printed `Index <i> is out of range for a vector of length <len>.`, while `ReC` printed `Index <i> out of range for vector of length <len>.`.
  - Missing map key: `ReVm` printed `Key <key> was not found in map.`, while `ReC` printed `Key <key> not found in map.`.
- **Texts introduced by `ReVm` (ticket 063) with no spec rule:**
  - `readline()` at end of input (`D-030` left the exact error text open): `ReVm` returned `Result::Failure("There is no more input to read.")`, while `ReC` returned `Result::Failure("End of input.")`.
  - `int(<string>)` / `float(<string>)` parse failures: `ReVm` returned `Result::Failure("Cannot convert '<text>' to an int.")` and `Result::Failure("Cannot convert '<text>' to a float.")`, while `ReC` returned `"Invalid integer format"` and `"Invalid float format"`.
  - `write_file(path, content)` on success returns `Result::Success(path)`.
  - `read_file` / `write_file` `IOError` payloads map `NotFound`, `PermissionDenied`, and `AlreadyExists` to `path` (or a host-supplied message for `PermissionDenied` in sandboxed hosts) and `Gremlin` to the OS error message.
- **Float print form:** `TYPE-10` and `D-022` required `.0` on whole floats, while `ReVm` and `ReC` both format floats in `.NET` invariant style (`1E+17` upward, `1E-05` downward, `.0` on fixed whole values, `NaN`, `Infinity`, `-Infinity`).

## Decision
1. **Float print form** (owner, 2026-11-24): keep the current `.NET` invariant style as implemented by both engines (`TYPE-20`).
2. **Plain-sentence wording** (router, 2026-11-24): where the engines differ, the complete plain-sentence wording wins (the VM's wording in every differing case).
3. **`readline()` end of input** (`D-030`): `Result::Failure("There is no more input to read.")` (`BUILTIN-9`).
4. **Normative texts:**
   - Vector bounds panic (`VEC-E4`): `Index <i> is out of range for a vector of length <len>.`
   - String bounds panic (`STRLIB-E2`): `Index <i> is out of range for a string of length <len>.`
   - Substring bounds panic (`STRLIB-E1`): `Substring starting at <start> with length <len> is out of range for a string of length <s:len>.`
   - Map missing-key panic (`MAP-E1`): `Key <key> was not found in map.`
   - `int(<string>)` failure (`TYPE-21`): `Cannot convert '<text>' to an int.`
   - `float(<string>)` failure (`TYPE-22`): `Cannot convert '<text>' to a float.`
   - `write_file` success (`BUILTIN-13`): `Result::Success(path)`
   - File `IOError` payloads (`BUILTIN-17`): `IOError::NotFound(path)`, `IOError::PermissionDenied(path)` (or host-supplied message), `IOError::AlreadyExists(path)`, `IOError::Gremlin(message)`.
