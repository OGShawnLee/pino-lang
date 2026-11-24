#!/usr/bin/env bash
# Packages a Unix (Linux or macOS) release archive (pino-<os>-<arch>.tar.gz) for Pino Lang.
set -euo pipefail

RID=""
ARTIFACT_NAME=""
OUTPUT_DIR="."
TCC_DIR=""
NO_TRIM="0"

while [ "$#" -gt 0 ]; do
  case "$1" in
    --rid)
      RID="$2"
      shift 2
      ;;
    --artifact-name)
      ARTIFACT_NAME="$2"
      shift 2
      ;;
    --output-dir)
      OUTPUT_DIR="$2"
      shift 2
      ;;
    --tcc-dir)
      TCC_DIR="$2"
      shift 2
      ;;
    --no-trim)
      NO_TRIM="1"
      shift
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

if [ -z "$RID" ] || [ -z "$ARTIFACT_NAME" ]; then
  echo "Usage: $0 --rid <rid> --artifact-name <name> [--output-dir <dir>] [--tcc-dir <dir>] [--no-trim]" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
mkdir -p "$OUTPUT_DIR"
RESOLVED_OUTPUT_DIR="$(cd "$OUTPUT_DIR" && pwd)"

WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/pino-package.XXXXXX")"
trap 'rm -rf "$WORK_DIR"' EXIT

PUBLISH_DIR="$WORK_DIR/publish"
STAGE_ROOT="$WORK_DIR/$ARTIFACT_NAME"
mkdir -p "$PUBLISH_DIR" "$STAGE_ROOT"

PUBLISH_ARGS=(
  publish "$REPO_ROOT/pino-csharp-re/Pino.Cli/Pino.Cli.csproj"
  -c Release
  -r "$RID"
  --self-contained true
  -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true
  -p:DebugType=none
  -p:DebugSymbols=false
  -o "$PUBLISH_DIR"
)

if [ "$NO_TRIM" != "1" ]; then
  PUBLISH_ARGS+=(-p:PublishTrimmed=true)
fi

echo "Publishing Pino.Cli for $RID..."
dotnet "${PUBLISH_ARGS[@]}"

if [ ! -f "$PUBLISH_DIR/pino" ]; then
  echo "Expected published binary at '$PUBLISH_DIR/pino' was not found." >&2
  exit 1
fi

cp "$PUBLISH_DIR/pino" "$STAGE_ROOT/pino"
chmod +x "$STAGE_ROOT/pino"

# runtime-re/*.c and *.h (excluding runtime-re/tests/)
mkdir -p "$STAGE_ROOT/runtime-re"
cp "$REPO_ROOT"/runtime-re/*.c "$REPO_ROOT"/runtime-re/*.h "$STAGE_ROOT/runtime-re/"

# Optional tooling/tcc/ (omitted on Linux and macOS)
if [ -n "$TCC_DIR" ]; then
  if [ ! -f "$TCC_DIR/tcc" ]; then
    echo "TinyCC was not found at '$TCC_DIR/tcc'." >&2
    exit 1
  fi
  mkdir -p "$STAGE_ROOT/tooling/tcc"
  cp -R "$TCC_DIR/." "$STAGE_ROOT/tooling/tcc/"
  chmod +x "$STAGE_ROOT/tooling/tcc/tcc"
fi

# games/*.pino only
mkdir -p "$STAGE_ROOT/games"
cp "$REPO_ROOT"/games/*.pino "$STAGE_ROOT/games/"

# Notices and license
if [ ! -f "$REPO_ROOT/THIRD_PARTY_NOTICES.md" ]; then
  echo "THIRD_PARTY_NOTICES.md is missing at repo root." >&2
  exit 1
fi
cp "$REPO_ROOT/THIRD_PARTY_NOTICES.md" "$STAGE_ROOT/THIRD_PARTY_NOTICES.md"

if [ ! -f "$REPO_ROOT/LICENSE" ]; then
  echo "LICENSE is missing at repo root." >&2
  exit 1
fi
cp "$REPO_ROOT/LICENSE" "$STAGE_ROOT/LICENSE"

ARCHIVE_PATH="$RESOLVED_OUTPUT_DIR/$ARTIFACT_NAME.tar.gz"
rm -f "$ARCHIVE_PATH"

echo "Creating archive $ARCHIVE_PATH..."
tar -czf "$ARCHIVE_PATH" -C "$WORK_DIR" "$ARTIFACT_NAME"

BIN_SIZE="$(wc -c < "$STAGE_ROOT/pino" | tr -d ' ')"
TAR_SIZE="$(wc -c < "$ARCHIVE_PATH" | tr -d ' ')"
echo "Packaged $ARCHIVE_PATH ($TAR_SIZE bytes; pino $BIN_SIZE bytes)."
