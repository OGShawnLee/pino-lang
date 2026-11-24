# HANDOFF

Notes between the **compiler router** ([router](../.claude/skills/router/SKILL.md)) and the **site router** ([site-router](../.claude/skills/site-router/SKILL.md)). Add entries at the top, dated and signed (`compiler` or `site`). Keep each entry short. When an entry is done, mark it `[done]` instead of deleting it.

## 2026-11-24 · compiler (075 landed: 0.4 packaging and installers)

- **README updates for 0.4 are yours** (`1bb7a56`). The full suggestions are in `.tickets/075-release-0.4/changelog.md`, section "Suggested README changes". In short:
  - The install one-liners stay (`install.ps1` via `irm … | iex`, `install.sh` via `curl … | bash`). They now install the whole `~/.pino/` folder and verify downloads against `SHA256SUMS`.
  - **Upgrading from 0.3:** the binary moves from `~/.pino/bin/pino` to `~/.pino/pino`. The installer removes the old PATH entry.
  - **Requirements:** `pino run`, `test`, `repl` and `play` need nothing on any OS. For `--c` and `pino build`: Windows has TinyCC bundled; Linux has TinyCC bundled but needs the C library headers (`libc6-dev` / `glibc-devel`); macOS uses Apple's `clang` (`xcode-select --install`). `build --release` needs GCC or Clang.
  - Pino is now **MIT licensed** (`LICENSE`), and TinyCC's LGPL notice is in `THIRD_PARTY_NOTICES.md`.
- **Wait for the GitHub check before publishing install instructions as final.** Linux and macOS haven't run yet (the owner pushes, then CI and a manual release-workflow run).

## 2026-11-24 · compiler (080 landed: `pino test`)

- `pino test` works (`b54917c`): `test "name" { }` blocks, `--vm`/`--c`/`--strict`/`--filter`, `[PASS]`/`[FAIL]` lines, failed asserts that show values, per-test leak warnings. The guide page is `docs/guide/testing.md`; the spec is `spec/features/testing.md`.
- The help is now in plain words. It's fine to copy commands from `pino --help`, except the two memory-check options (`--verify-rc`, `--debug-rc`), which share one description until a small fix lands.
- A failing assert makes a good "errors are a feature" example for the site:
  ```
  [FAIL] first changes count  (test/more_test.pino:9)
         math is broken
         `add(1, 1) == 3` was false.
           add(1, 1) is 2
  ```

## 2026-11-24 · compiler (074 landed: the CLI is `pino`)

- The new compiler's executable is now **`pino`** (`3927893`). For README and site copy: `pino init [name]`, `pino` (runs `main.pino`), `pino run [--vm | --c] [file]`, `pino build`, `pino compile`, `pino watch`, `pino check`, `pino repl`, `pino play [game]`, `pino play update`, `pino version`, `pino update`, `pino explain <code>`. `pino --help` prints the full list.
- `pino test` is coming in 080 (`[PASS]`/`[FAIL]` output, asserts that show values, leak warnings). Don't document it yet.
- The help's wording gets a plain-language pass in 080. Copy the help text after that, not now.

## 2026-11-24 · compiler (070 landed: the playground API)

- **`Pino.Wasm` is on `main`** (`404211c`): `pino-csharp-re/Pino.Wasm/`, a .NET browser-wasm project (`Microsoft.NET.Sdk.WebAssembly`), not in `Pino.sln`.
- **Build:** `dotnet publish pino-csharp-re/Pino.Wasm -c Release` (needs the `wasm-tools` workload). The output is `pino-csharp-re/Pino.Wasm/bin/Release/net10.0/publish/wwwroot/_framework/` (`dotnet.js` and the assemblies; about 3.1 MB with Brotli).
- **JS contract:** the main assembly is `Pino.Wasm.dll`. After `getAssemblyExports("Pino.Wasm.dll")`:
  - `exports.Pino.Wasm.Playground.Run(code: string): string` returns JSON `{"stdout": string, "stderr": string, "exitCode": number}`. Diagnostics and panics are in `stderr`, in the same plain-sentence text as the CLI.
  - `exports.Pino.Wasm.Playground.Evaluate(code: string): string` returns stdout, or stderr on failure. It has the old playground's shape, for an easy switch.
- In the browser, `readline()` sees an empty stdin, file I/O returns an `Err` ("File access is not available in the browser playground."), and `sleep`/`clear` do nothing. A program that runs about 2,000,000 loop steps stops with "Program stopped because it ran too many steps (possible infinite loop)." The call is synchronous, so run it in a Web Worker to keep the page responsive.
- The playground can drop the legacy `pino-csharp` WASM now. The compiler router deletes `pino-csharp/` after 075.

## 2026-11-23 · compiler (owner answers)

- The owner's answers for the site are in SITE.md, under "Owner answers": a likely full redesign, Opus for UI, Neon Low for side tasks, and the uncommitted `CoreTruths.astro` edit doesn't block.
- **`README.md` is now the site router's.** The compiler router won't edit it. When a compiler change makes the README wrong (commands, install, status), it writes the change here for the site router.

## 2026-11-23 · compiler

- **Set-up.** The site router owns `pino-site-next/**`. The compiler router owns the compiler, `spec/`, `conformance/`, `games/` and the **content** of `docs/guide/`. `README.md` belongs to the site router (see the entry above).
- **Language facts:** take them from `spec/`, `docs/guide/` and `README.md`. Every snippet on the site must pass `pino-re check` (`dotnet run --no-build --project pino-csharp-re/Pino.Cli -- check <file>`).
- **The current state, for the site's status page:**
  - the new compiler runs the whole language on a VM and on native C, with 657 conformance cases passing on both
  - the legacy compiler is being retired
  - 0.4 will ship the new compiler as `pino`, with TCC bundled, so nothing else is needed to install
- **Coming from the compiler side:**
  - 070: `Pino.Wasm` for the playground; its API goes here when it lands
  - 074: the CLI commands (`init`, `run`, `build`, `watch`, `test`, `play`, `repl`, `version`, `update`)
  - 075: release assets and installers
  - 079: `docs/guide/patterns.md`
- **The games** now live in `games/`. `pino.games/` is legacy, downloaded by 0.3's `pino play update`.
