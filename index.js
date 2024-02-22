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
} else {
  console.log("$name is not married!");
}
if (has_children) {
  const message = "$name has children";
  console.log(message);
} else {
  console.log("$name has no children!");
}
function greet(name, is_married, has_newline) {
  if (is_married) {
    console.log("Hello!", name);
    console.log("You are married, great!");
  } else {
    console.log("Hello!", name);
    console.log("You will get married one day!");
  }
  if (has_newline) {
    console.log();
  }
}
function call_planets() {
  console.log("First planet is Mercury");
  console.log("Then we have Venus");
  console.log("The third one is our planet, Earth");
}
greet(name, true, true);
greet(name, false, true);
greet("Richard", true);
call_planets();
message = "Gaius Julius Ceasar Augustus was the first and greatest Roman emperor";
console.log();
print_message(message, "Shawn Lee");
function print_message(message, name) {
  console.log(message);
  console.log("Thank you for leaving your message", name);
}
function print_country(name, population) {
  console.log(name, "has this many millions of people:", population);
}
console.log();
print_country(country, 1630);
