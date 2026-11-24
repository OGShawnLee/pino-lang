# Third-Party Notices

This release of Pino Lang includes the following third-party software.

## Tiny C Compiler (TinyCC / TCC)

- **Version:** 0.9.27
- **License:** GNU Lesser General Public License, version 2.1 (LGPL-2.1)
- **Bundled on:** Windows (`x64`) under `tooling/tcc/`
- **Modifications:** Unmodified. The Windows build is the official `tcc-0.9.27-win64-bin.zip` distribution.
- **Official source code:**
  - Windows binary archive: <https://download.savannah.gnu.org/releases/tinycc/tcc-0.9.27-win64-bin.zip>
  - Project homepage: <https://bellard.org/tcc/>
  - Repository: <https://repo.or.cz/tinycc.git>
- **License text:** The full LGPL-2.1 license text (`COPYING`) is included in the official TinyCC source distribution and at <https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt>.

TinyCC is invoked as a separate command-line tool (`tooling/tcc/tcc.exe`) on Windows when compiling Pino programs with `--c` or `pino build`.

## SLRE (Super Light Regular Expression library)

- **Location:** `runtime/re.c`, `runtime/re.h`
- **Notice:** Derived from Sergey Lyubka's SLRE implementation, carried over in `runtime/` for Pino's regex support.
