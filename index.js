const name = "Shawn Lee";
const country = "China";
let is_married = false;
let age = 14;
let message = "$name lives in $country";
is_married = true;
age = 15;
message = "This was written in Pino and transpiled to JavaScript!";
const planet = "Earth is the planet where humans live";
const person = "Gaius Julius Ceasar Augustus";
is_married = false;
console.log(message, planet, person);
console.log(person, is_married);
console.log("What is going on?", 404, false, true);
let has_children = false;
is_married = true;
if (is_married) {
  const message = "$name is married";
  console.log(message);
  if (true) {
    console.log("Nested If Statement!");
    if (true) {
      console.log("Yet Another Nested If Statement!");
    }
  }
}
if (has_children) {
  const message = "$name has children";
  console.log(message);
}
