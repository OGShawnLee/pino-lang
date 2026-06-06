# Pino Language Support for VS Code

This extension provides syntax highlighting, bracket matching, and auto-closing configurations for **Pino Lang** files (`.pino`).

## Features
* **Syntax Highlighting**: Supports keywords, types, number underscores, strings, string variable injections (`$variable`), operators, booleans, and single-line comments.
* **Auto-closing Pairs**: Automatically inserts matching quotes `"`, parenthesis `()`, braces `{}`, and brackets `[]`.
* **Bracket Matching**: Highlights and matches structural braces and brackets.
* **Line Comments**: Toggles commenting for lines via the standard `#` character using the default comment commands.

## How to Install Locally

You can install this extension locally on your VS Code editor using either of the following methods:

### Method 1: Link/Copy to Extensions Directory (Recommended)
1. Copy the entire `vscode-pino` directory to your VS Code extensions folder:
   * **Windows**: `%USERPROFILE%\.vscode\extensions\vscode-pino`
   * **macOS / Linux**: `~/.vscode/extensions/vscode-pino`
2. Restart VS Code. `.pino` files will now automatically have syntax highlighting active!

### Method 2: Load via Extension Development Host
1. Open this folder (`vscode-pino`) as a root workspace in a new VS Code window.
2. Press **F5** (or go to `Run and Debug` tab -> click `Run Extension`).
3. A new VS Code window (Extension Development Host) will open. Open any `.pino` file inside this new window to see the syntax highlighting in action.
