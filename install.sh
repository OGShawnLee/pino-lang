#!/bin/bash
# install.sh - Unix installer for Pino Lang
set -e

INSTALL_DIR="$HOME/.pino"
BIN_DIR="$INSTALL_DIR/bin"

mkdir -p "$BIN_DIR"

echo "🌲 Installing Pino Lang..."

REPO="OGShawnLee/pino-lang"
RELEASE_URL="https://api.github.com/repos/$REPO/releases/latest"

# Fetch metadata
RELEASE_JSON=$(curl -s "$RELEASE_URL")
TAG_NAME=$(echo "$RELEASE_JSON" | grep -o '"tag_name": "[^"]*' | head -n 1 | cut -d'"' -f4)

# Detect OS
OS_TYPE=""
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    OS_TYPE="linux"
elif [[ "$OSTYPE" == "darwin"* ]]; then
    OS_TYPE="macos"
else
    # Fallback check via uname
    UNAME_S=$(uname -s)
    if [ "$UNAME_S" = "Linux" ]; then
        OS_TYPE="linux"
    elif [ "$UNAME_S" = "Darwin" ]; then
        OS_TYPE="macos"
    else
        echo "Unsupported operating system type: $OSTYPE"
        exit 1
    fi
fi

# Locate asset matching OS
if [ "$OS_TYPE" = "macos" ]; then
    ASSET_URL=$(echo "$RELEASE_JSON" | grep -o '"browser_download_url": "[^"]*' | grep "macos" | head -n 1 | cut -d'"' -f4)
else
    ASSET_URL=$(echo "$RELEASE_JSON" | grep -o '"browser_download_url": "[^"]*' | grep "linux" | head -n 1 | cut -d'"' -f4)
fi

if [ -z "$ASSET_URL" ]; then
    echo "Could not find binary asset matching your operating system ($OS_TYPE)."
    exit 1
fi

TEMP_TAR="/tmp/pino-install.tar.gz"
echo "Downloading version $TAG_NAME from $ASSET_URL..."
curl -L -s -o "$TEMP_TAR" "$ASSET_URL"

# Extract
TEMP_EXTRACT="/tmp/pino-extract"
rm -rf "$TEMP_EXTRACT"
mkdir -p "$TEMP_EXTRACT"
tar -xzf "$TEMP_TAR" -C "$TEMP_EXTRACT"

# Find pino executable
EXE_PATH=$(find "$TEMP_EXTRACT" -type f -name "pino" | head -n 1)

if [ -z "$EXE_PATH" ]; then
    echo "Could not find pino executable in archive."
    exit 1
fi

mv "$EXE_PATH" "$BIN_DIR/pino"
chmod +x "$BIN_DIR/pino"

# Clean up
rm -f "$TEMP_TAR"
rm -rf "$TEMP_EXTRACT"

# Suggest PATH addition
SHELL_RC=""
if [ -n "$ZSH_VERSION" ]; then
    SHELL_RC="$HOME/.zshrc"
elif [ -n "$BASH_VERSION" ]; then
    SHELL_RC="$HOME/.bashrc"
else
    # Fallback checks
    if [ -f "$HOME/.zshrc" ]; then
        SHELL_RC="$HOME/.zshrc"
    elif [ -f "$HOME/.bashrc" ]; then
        SHELL_RC="$HOME/.bashrc"
    elif [ -f "$HOME/.profile" ]; then
        SHELL_RC="$HOME/.profile"
    fi
fi

if [ -n "$SHELL_RC" ]; then
    if ! grep -q "$BIN_DIR" "$SHELL_RC"; then
        echo "Adding $BIN_DIR to PATH in $SHELL_RC..."
        echo "" >> "$SHELL_RC"
        echo "# Pino Lang CLI" >> "$SHELL_RC"
        echo "export PATH=\"\$PATH:$BIN_DIR\"" >> "$SHELL_RC"
        export PATH="$PATH:$BIN_DIR"
    fi
else
    echo "Please add $BIN_DIR manually to your PATH environment variable."
fi

echo "🌲 Pino Lang ($TAG_NAME) has been successfully installed!"
echo "Restart your terminal shell and run 'pino' to verify."
