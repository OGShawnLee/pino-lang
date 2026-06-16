/// <reference types="astro/client" />

interface Window {
  // Globals from interpreter.js
}

declare function runPinoCode(
  code: string,
  onOutput: (text: string) => void,
  onInput: () => string
): void;
