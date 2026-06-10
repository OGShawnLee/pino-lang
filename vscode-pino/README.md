# <img src="./logo.png" align="left" width="48" height="48" />&nbsp;Pino Language Support

VS Code extension providing syntax highlighting, bracket matching, and code configuration for **Pino Lang** (`.pino`) files.

[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](#)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](#)
[![VS Code](https://img.shields.io/badge/VS%20Code-%3E%3D%201.40.0-purple.svg)](#)

---

## Features

* ✨ **Rich Syntax Highlighting**: Comprehensive coloring for keywords, built-in types, constants, operators, string interpolation, and comments.
* 📦 **Module System Support**: Complete highlighting for module keywords (`module`, `import`, `from`) and visibility modifiers (`pub`).
* 🔒 **Smart Enclosing & Auto-Closing**: Automatic insertion of matching pairs for quotes (`"`), parentheses (`()`), brackets (`[]`), and curly braces (`{}`).
* 🧩 **Bracket Matching**: Instantly highlights matching structural braces and brackets for clean flow tracing.
* 💬 **Comment Toggling**: Quick toggling of single-line comments using the `#` prefix.

---

## Language Snippet Preview

Here is how a typical **Pino Lang** script looks with the syntax highlighting active:

```pino
module Entities

pub struct Player {
  name string
  hp int
  ram int
  coffee int

  fn heal(amt int) {
    var new_hp = hp + amt
    hp = if new_hp > 100 then 100 else new_hp
  }
}
```

---

## How to Install Locally

You can install this extension locally on your VS Code editor using either of the following methods:

### Method 1: Install to Extensions Directory (Recommended)

1. Copy or link the entire `vscode-pino` directory to your VS Code extensions folder:
   * **Windows**: `%USERPROFILE%\.vscode\extensions\vscode-pino`
   * **macOS / Linux**: `~/.vscode/extensions/vscode-pino`
2. Restart VS Code. `.pino` files will now automatically have syntax highlighting and language configuration active.

### Method 2: Load via Extension Development Host (For testing)

1. Open the `vscode-pino` folder as a root workspace in a new VS Code window.
2. Press **F5** (or go to `Run and Debug` tab and click `Run Extension`).
3. A new VS Code window (Extension Development Host) will open. Open any `.pino` file inside this new window to see the syntax highlighting in action.
