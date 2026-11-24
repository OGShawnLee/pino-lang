#!/usr/bin/env bash
# install.sh - Linux and macOS installer for Pino Lang
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/OGShawnLee/pino-lang/main/install.sh | bash
#   bash install.sh [--from-file <archive.tar.gz>] [--install-dir <dir>] [--sha256sums <file>] [--no-path-update]
set -euo pipefail

FROM_FILE=""
INSTALL_DIR="${PINO_INSTALL_DIR:-$HOME/.pino}"
SHA256SUMS_FILE=""
NO_PATH_UPDATE="0"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --from-file)
      FROM_FILE="$2"
      shift 2
      ;;
    --install-dir)
      INSTALL_DIR="$2"
      shift 2
      ;;
    --sha256sums)
      SHA256SUMS_FILE="$2"
      shift 2
      ;;
    --no-path-update)
      NO_PATH_UPDATE="1"
      shift
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

detect_os() {
  case "$(uname -s)" in
    Linux) echo "linux" ;;
    Darwin) echo "macos" ;;
    *)
      echo "Unsupported operating system: $(uname -s)" >&2
      exit 1
      ;;
  esac
}

detect_arch() {
  case "$(uname -m)" in
    x86_64|amd64) echo "x64" ;;
    arm64|aarch64) echo "arm64" ;;
    *)
      echo "Unsupported architecture: $(uname -m)" >&2
      exit 1
      ;;
  esac
}

compute_sha256() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$file" | awk '{print $1}'
  elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$file" | awk '{print $1}'
  else
    echo "Neither sha256sum nor shasum is installed; cannot verify download." >&2
    exit 1
  fi
}

verify_checksum() {
  local archive="$1"
  local asset_name="$2"
  local sums_file="$3"
  local expected=""
  local line hash name base

  while IFS= read -r line || [ -n "$line" ]; do
    case "$line" in
      ""|\#*) continue ;;
    esac
    hash="$(printf '%s\n' "$line" | awk '{print $1}')"
    name="$(printf '%s\n' "$line" | awk '{print $2}' | sed 's/^\*//')"
    base="$(basename "$name")"
    if [ "$name" = "$asset_name" ] || [ "$base" = "$asset_name" ]; then
      expected="$(printf '%s' "$hash" | tr 'A-F' 'a-f')"
      break
    fi
  done < "$sums_file"

  if [ -z "$expected" ]; then
    echo "Could not find a checksum entry for '$asset_name' in '$sums_file'." >&2
    exit 1
  fi

  local actual
  actual="$(compute_sha256 "$archive" | tr 'A-F' 'a-f')"
  if [ "$actual" != "$expected" ]; then
    echo "SHA-256 verification failed for '$asset_name': expected $expected, got $actual." >&2
    exit 1
  fi
}

update_shell_rc() {
  local rc_file="$1"
  local target_dir="$2"
  local legacy_bin="$target_dir/bin"
  local display_dir="$target_dir"

  if [ "$target_dir" = "$HOME/.pino" ]; then
    display_dir="\$HOME/.pino"
  fi

  local tmp_rc
  tmp_rc="$(mktemp)"
  local removed_legacy="0"

  if [ -f "$rc_file" ]; then
    while IFS= read -r line || [ -n "$line" ]; do
      case "$line" in
        *"$legacy_bin"*|*'/.pino/bin'*)
          removed_legacy="1"
          continue
          ;;
      esac
      printf '%s\n' "$line" >> "$tmp_rc"
    done < "$rc_file"
  fi

  local already_present="0"
  if [ -f "$tmp_rc" ] && grep -Fq "$display_dir" "$tmp_rc" 2>/dev/null; then
    already_present="1"
  elif [ -f "$tmp_rc" ] && grep -Fq "$target_dir" "$tmp_rc" 2>/dev/null; then
    already_present="1"
  fi

  if [ "$already_present" = "0" ]; then
    printf '\n# Pino Lang CLI\nexport PATH="%s:$PATH"\n' "$display_dir" >> "$tmp_rc"
    mv "$tmp_rc" "$rc_file"
    echo "Added $target_dir to PATH in $rc_file."
  elif [ "$removed_legacy" = "1" ]; then
    mv "$tmp_rc" "$rc_file"
    echo "Removed legacy $legacy_bin PATH entry from $rc_file."
  else
    rm -f "$tmp_rc"
  fi
}

OS_NAME="$(detect_os)"
ARCH_NAME="$(detect_arch)"
ASSET_NAME=""

case "${OS_NAME}-${ARCH_NAME}" in
  linux-x64) ASSET_NAME="pino-linux-x64.tar.gz" ;;
  macos-x64) ASSET_NAME="pino-macos-x64.tar.gz" ;;
  macos-arm64) ASSET_NAME="pino-macos-arm64.tar.gz" ;;
  *)
    echo "Precompiled Pino binaries are not available for ${OS_NAME}/${ARCH_NAME}." >&2
    exit 1
    ;;
esac

TEMP_WORK="$(mktemp -d "${TMPDIR:-/tmp}/pino-install.XXXXXX")"
trap 'rm -rf "$TEMP_WORK"' EXIT

ARCHIVE_PATH=""
INSTALLED_LABEL=""

if [ -n "$FROM_FILE" ]; then
  if [ ! -f "$FROM_FILE" ]; then
    echo "Local archive not found: '$FROM_FILE'." >&2
    exit 1
  fi
  ARCHIVE_PATH="$FROM_FILE"
  LOCAL_NAME="$(basename "$ARCHIVE_PATH")"
  INSTALLED_LABEL="from $ARCHIVE_PATH"

  if [ -n "$SHA256SUMS_FILE" ]; then
    if [ ! -f "$SHA256SUMS_FILE" ]; then
      echo "Checksum file not found: '$SHA256SUMS_FILE'." >&2
      exit 1
    fi
    echo "Verifying archive checksum..."
    verify_checksum "$ARCHIVE_PATH" "$LOCAL_NAME" "$SHA256SUMS_FILE"
  else
    SIBLING_SUMS="$(dirname "$ARCHIVE_PATH")/SHA256SUMS"
    if [ -f "$SIBLING_SUMS" ]; then
      echo "Verifying archive checksum against $SIBLING_SUMS..."
      verify_checksum "$ARCHIVE_PATH" "$LOCAL_NAME" "$SIBLING_SUMS"
    fi
  fi
else
  echo "Installing Pino Lang for ${OS_NAME} (${ARCH_NAME})..."
  REPO="OGShawnLee/pino-lang"
  RELEASE_URL="https://api.github.com/repos/$REPO/releases/latest"

  RELEASE_JSON="$(curl -fsSL -H "User-Agent: Pino-Installer/0.4" "$RELEASE_URL")"
  TAG_NAME="$(printf '%s\n' "$RELEASE_JSON" | grep -o '"tag_name": *"[^"]*"' | head -n 1 | cut -d'"' -f4)"
  if [ -z "$TAG_NAME" ]; then
    echo "Could not determine the latest Pino release tag from GitHub." >&2
    exit 1
  fi

  ASSET_URL="$(printf '%s\n' "$RELEASE_JSON" | grep -o '"browser_download_url": *"[^"]*"' | cut -d'"' -f4 | grep "/${ASSET_NAME}$" | head -n 1 || true)"
  if [ -z "$ASSET_URL" ]; then
    echo "Could not find release asset '$ASSET_NAME' in release '$TAG_NAME'." >&2
    exit 1
  fi

  SUMS_URL="$(printf '%s\n' "$RELEASE_JSON" | grep -o '"browser_download_url": *"[^"]*"' | cut -d'"' -f4 | grep '/SHA256SUMS$' | head -n 1 || true)"
  if [ -z "$SUMS_URL" ]; then
    echo "Could not find 'SHA256SUMS' in release '$TAG_NAME'; refusing unverified install." >&2
    exit 1
  fi

  ARCHIVE_PATH="$TEMP_WORK/$ASSET_NAME"
  SUMS_PATH="$TEMP_WORK/SHA256SUMS"

  echo "Downloading Pino $TAG_NAME ($ASSET_NAME)..."
  curl -fsSL -H "User-Agent: Pino-Installer/0.4" -o "$ARCHIVE_PATH" "$ASSET_URL"
  curl -fsSL -H "User-Agent: Pino-Installer/0.4" -o "$SUMS_PATH" "$SUMS_URL"

  echo "Verifying SHA-256 checksum..."
  verify_checksum "$ARCHIVE_PATH" "$ASSET_NAME" "$SUMS_PATH"
  INSTALLED_LABEL="$TAG_NAME"
fi

EXTRACT_DIR="$TEMP_WORK/extract"
mkdir -p "$EXTRACT_DIR"
tar -xzf "$ARCHIVE_PATH" -C "$EXTRACT_DIR"

EXE_PATH="$(find "$EXTRACT_DIR" -maxdepth 3 -type f -name pino | head -n 1 || true)"
if [ -z "$EXE_PATH" ]; then
  echo "Could not find the 'pino' executable inside the archive." >&2
  exit 1
fi
SOURCE_ROOT="$(dirname "$EXE_PATH")"

echo "Installing to $INSTALL_DIR..."
rm -rf "$INSTALL_DIR"
mkdir -p "$INSTALL_DIR"
cp -R "$SOURCE_ROOT/." "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/pino"
if [ -f "$INSTALL_DIR/tooling/tcc/tcc" ]; then
  chmod +x "$INSTALL_DIR/tooling/tcc/tcc"
fi

if [ "$NO_PATH_UPDATE" != "1" ]; then
  SHELL_RC=""
  case "$(basename "${SHELL:-}")" in
    zsh) SHELL_RC="$HOME/.zshrc" ;;
    bash)
      if [ -f "$HOME/.bashrc" ] || [ ! -f "$HOME/.bash_profile" ]; then
        SHELL_RC="$HOME/.bashrc"
      else
        SHELL_RC="$HOME/.bash_profile"
      fi
      ;;
    *)
      if [ -f "$HOME/.zshrc" ]; then
        SHELL_RC="$HOME/.zshrc"
      elif [ -f "$HOME/.bashrc" ]; then
        SHELL_RC="$HOME/.bashrc"
      else
        SHELL_RC="$HOME/.profile"
      fi
      ;;
  esac

  if [ -n "$SHELL_RC" ]; then
    update_shell_rc "$SHELL_RC" "$INSTALL_DIR"
  else
    echo "Add $INSTALL_DIR to your PATH to run 'pino' from any folder."
  fi
fi

INSTALLED_VERSION="$("$INSTALL_DIR/pino" version 2>/dev/null || true)"
if [ -n "$INSTALLED_VERSION" ]; then
  echo "Installed $INSTALLED_VERSION to $INSTALL_DIR ($INSTALLED_LABEL)."
else
  echo "Installed Pino ($INSTALLED_LABEL) to $INSTALL_DIR."
fi
echo "Run 'pino init hello' to start."
