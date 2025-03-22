#!/bin/bash

# Get the absolute path to the build directory
BUILD_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/build" && pwd)"

# Check if the build directory contains the pino executable
if [ ! -f "$BUILD_DIR/pino" ]; then
  echo "Error: pino executable not found in $BUILD_DIR. Make sure to build the project first."
  exit 1
fi

# Add the build directory to the PATH
if [[ ":$PATH:" != *":$BUILD_DIR:"* ]]; then
  export PATH="$BUILD_DIR:$PATH"
  echo "Added $BUILD_DIR to PATH."
else
  echo "$BUILD_DIR is already in PATH."
fi

# Verify the pino command
if command -v pino &> /dev/null; then
  echo "You can now run 'pino' from anywhere."
else
  echo "Error: Failed to add pino to PATH."
fi