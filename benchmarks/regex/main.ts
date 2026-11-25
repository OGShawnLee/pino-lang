import { readFileSync } from "fs";

const text = readFileSync("benchmarks/data/regex.txt", "utf-8");

const emailRe = /\w+@\w+\.com/g;
const identRe = /[A-Za-z_][A-Za-z0-9_]*/g;

const emails = text.match(emailRe) ?? [];
const idents = text.match(identRe) ?? [];

console.log(emails.length);
console.log(idents.length);
