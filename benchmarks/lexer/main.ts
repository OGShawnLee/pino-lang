import { readFileSync } from "fs";

const MARKER_MAP: Record<string, string> = {
  "(": "LPAREN", ")": "RPAREN",
  "[": "LBRACKET", "]": "RBRACKET",
  "{": "LBRACE", "}": "RBRACE",
  ":": "COLON", ",": "COMMA",
};

const KEYWORDS = new Set([
  "val", "var", "fn", "return", "if", "then", "else", "match", "when",
  "yield", "for", "in", "break", "continue", "struct", "enum", "union",
  "interface", "pub", "is", "static", "module", "from", "import",
]);

type Atom =
  | { type: "Keyword"; name: string }
  | { type: "Identifier"; name: string }
  | { type: "LiteralInt"; value: number }
  | { type: "LiteralBool"; value: boolean }
  | { type: "Marker"; marker: string }
  | { type: "EOF" };

interface Particle {
  line: number;
  column: number;
  len: number;
  atom: Atom;
}

function isIdentChar(code: number): boolean {
  return (
    (code >= 97 && code <= 122) ||
    (code >= 65 && code <= 90) ||
    code === 95
  );
}

function isNumChar(code: number): boolean {
  return code >= 48 && code <= 57;
}

function lexProgram(program: string): Particle[] {
  const atomList: Particle[] = [];
  let lineNum = 1;
  let column = 1;
  let identifierLen = 0;
  let numericalLen = 0;

  function pushIdentifier(i: number, len: number) {
    const begin = i - len;
    const str = program.substring(begin, i);
    let atom: Atom;
    if (str.length <= 10 && KEYWORDS.has(str)) {
      atom = { type: "Keyword", name: str };
    } else if (str === "true" || str === "false") {
      atom = { type: "LiteralBool", value: str === "true" };
    } else {
      atom = { type: "Identifier", name: str };
    }
    atomList.push({
      line: lineNum,
      column: column - len,
      len,
      atom,
    });
  }

  function pushNumerical(i: number, len: number) {
    const begin = i - len;
    const str = program.substring(begin, i);
    atomList.push({
      line: lineNum,
      column: column - len,
      len,
      atom: { type: "LiteralInt", value: parseInt(str, 10) },
    });
  }

  const totalLen = program.length;
  for (let i = 0; i < totalLen; i++) {
    const code = program.charCodeAt(i);
    const char = program[i];

    if (isIdentChar(code)) {
      identifierLen++;
      column++;
      continue;
    } else if (identifierLen > 0) {
      pushIdentifier(i, identifierLen);
      identifierLen = 0;
    }

    if (isNumChar(code)) {
      numericalLen++;
      column++;
      continue;
    } else if (numericalLen > 0) {
      pushNumerical(i, numericalLen);
      numericalLen = 0;
    }

    if (char === "\n") {
      lineNum++;
      column = 1;
      continue;
    }

    if (char === " " || char === "\t") {
      column++;
      continue;
    }

    if (MARKER_MAP[char]) {
      atomList.push({
        line: lineNum,
        column,
        len: 1,
        atom: { type: "Marker", marker: MARKER_MAP[char] },
      });
      column++;
      continue;
    }

    column++;
  }

  if (identifierLen > 0) {
    pushIdentifier(totalLen, identifierLen);
  } else if (numericalLen > 0) {
    pushNumerical(totalLen, numericalLen);
  }

  atomList.push({
    line: lineNum,
    column,
    len: 1,
    atom: { type: "EOF" },
  });

  return atomList;
}

const content = readFileSync("benchmarks/data/lexer.txt", "utf-8");
const particles = lexProgram(content);

let kwCount = 0;
let identCount = 0;
let intSum = 0;
let boolTrue = 0;
let markerCount = 0;

for (const p of particles) {
  switch (p.atom.type) {
    case "Keyword":
      kwCount++;
      break;
    case "Identifier":
      identCount++;
      break;
    case "LiteralInt":
      intSum = (intSum + p.atom.value) % 1000000007;
      break;
    case "LiteralBool":
      if (p.atom.value) {
        boolTrue++;
      }
      break;
    case "Marker":
      markerCount++;
      break;
  }
}

console.log(particles.length);
console.log(kwCount);
console.log(identCount);
console.log(intSum);
console.log(boolTrue);
console.log(markerCount);
