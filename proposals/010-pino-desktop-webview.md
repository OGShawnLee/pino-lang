# Proposal 010: Pino Desktop - Native WebView + Pino C Backend

* **Status**: Proposed
* **Author**: OGSShawnLee & Antigravity
* **Date**: 2026-07-10
* **Target Version**: Pino 1.0+ / C-Transpiler Evolution

---

## 1. Vision & Objective

Building desktop applications in the modern landscape has historically been a trade-off between developer productivity and application efficiency:
- **Electron**: Excellent developer experience (HTML/CSS/TS), but results in massive bloated binaries (>150MB) and extreme memory footprints (>100MB idle).
- **Tauri (Rust)**: Great efficiency and safety, but suffers from extremely slow compile times (minutes for a clean cargo build) and verbose/complex Rust code.
- **Java / C#**: Heavy runtimes, boilerplate-heavy syntax, and complex cross-platform distribution issues.

### The Pino Desktop Alternative
By combining **Pino's concise syntax**, the **C Transpiler + TCC (Tiny C Compiler)**, and the **OS's Native WebView** (Edge WebView2 on Windows, WKWebView on macOS, WebKitGTK on Linux), we can construct a desktop framework with:
1. **Instant Compilation**: Fully transpiles and compiles to native code in **less than 100ms** (using TCC).
2. **Microscopic Executables**: Static binaries between **200 KB and 1 MB** in size (with embedded asset compression).
3. **Extremely Low Memory**: Base RAM consumption of **~15 - 25 MB** (using the OS's shared webview process and direct C allocations).
4. **Modern UI Web Standards**: Full support for CSS, HTML5, TypeScript, React, SolidJS, or TailwindCSS.

---

## 2. Proposed Architecture

```
+-------------------------------------------------------+
|                    DESKTOP WINDOW                     |
|                                                       |
|   +-----------------------------------------------+   |
|   |         OS NATIVE WEBVIEW (Edge/WKWebView)    |   |
|   |                                               |   |
|   |   +---------------------------------------+   |   |
|   |   |           FRONTEND (HTML5)            |   |   |
|   |   |        TypeScript / Vanilla CSS       |   |   |
|   |   +---------------------------------------+   |   |
|   |                       |                       |   |
|   |                       | window.ipc.call()...  |   |
|   +-----------------------v-----------------------+   |
|                           |                           |
|              C Bridge (webview_bind)                  |
|                           |                           |
|   +-----------------------v-----------------------+   |
|   |                   BACKEND                     |   |
|   |              Pino Transpiled C                |   |
|   |             (TCC Compiled App)                |   |
|   +-----------------------------------------------+   |
+-------------------------------------------------------+
```

Pino Desktop will utilize a single-header C wrapper library **`webview.h`** that interfaces directly with the native WebView component of each target operating system.

### A. The Pino IPC Bridge (JavaScript <-> Pino C)
Bi-directional communication will be facilitated through direct function bindings:
1. **Call Backend (JS to Pino)**: Pino binds a C-callback function to a specific JavaScript global function identifier. When called in JS, the native Pino handler executes on the CPU thread.
2. **Execute Frontend (Pino to JS)**: Pino executes asynchronous string-based JavaScript snippets directly inside the WebView thread via `webview_eval`.

### B. Static Asset Bundling
For standalone distribution, all web assets (HTML, CSS, JS, images) will be packaged directly inside the executable.
A compiler subcommand (e.g. `pino bundle`) will convert a frontend output folder into static C byte arrays inside a generated header file (e.g. `assets.h`):
```c
const unsigned char INDEX_HTML[] = { 0x3c, 0x21, 0x64, 0x6f, ... };
const unsigned int INDEX_HTML_LEN = 1042;
```
The C webview runtime will then serve these assets directly from memory using custom URI schemes (e.g. `pino://app/index.html`).

---

## 3. Detailed API & Code Design

### A. Pino Backend Code (`main.pino`)
Developers write backend logic in Pino, binding actions to the frontend:

```pino
from Webview import Window, SizeHint

fn main {
    # 1. Initialize native window
    val app = Window::create(width: 800, height: 600, title: "Pino Desktop Client")
    app:set_size_hint(SizeHint::Min)

    # 2. Register native callbacks (IPC)
    app:bind("processData", fn(input string) string {
        # Intensive processing running at C speed
        val reversed = reverse_string(input)
        return "Processed on Backend: $reversed"
    })

    # 3. Load UI
    # In development: point to Vite/TypeScript dev server
    # In production: loads embedded assets automatically
    app:navigate("http://localhost:5173")

    # 4. Start Event Loop (blocks until window is closed)
    app:run()
}

fn reverse_string(s string) string {
    # Custom logic
    return s # Placeholder
}
```

### B. Frontend TypeScript Code (`app.ts`)
The frontend communicates with the backend via asynchronous JavaScript bindings:

```typescript
// Declared by Pino Desktop IPC Bridge
interface Window {
    processData: (input: string) => Promise<string>;
}

async function handleButtonClick() {
    const inputField = document.getElementById("username") as HTMLInputElement;
    
    // Call the native Pino backend function asynchronously
    try {
        const result = await window.processData(inputField.value);
        document.getElementById("output")!.innerText = result;
    } catch (err) {
        console.error("IPC failed:", err);
    }
}
```

---

## 4. Implementation Roadmap

### Phase 1: Native C Bindings (C Bridge)
- Integrate `webview.h` into the Pino C compiler runtime (`runtime/webview.h`).
- Register compiler linkage flags inside `TranspilerC.cs` for target platforms:
  - **Windows**: Link WebView2 loader library and COM libraries (`ole32.lib`, `shell32.lib`).
  - **macOS**: Link Apple Cocoa Frameworks (`-framework WebKit -framework Cocoa`).
  - **Linux**: Link WebKitGTK shared objects via TCC flags.

### Phase 2: Pino Standard Webview Library
- Create `modules/webview.pino` exposing the `Window` struct, configuration bindings, and `bind` callback registrars.
- Implement the internal C mapping of JavaScript promises to Pino function invocation.

### Phase 3: Asset Bundler (`pino bundle`)
- Implement a CLI asset parser that reads a build folder (e.g. `dist/`) and generates `assets.h` containing byte-arrays of the frontend files.
- Enable WebView custom schema protocols to load these arrays directly from program memory, preventing filesystem distribution dependencies.
