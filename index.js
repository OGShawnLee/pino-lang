const name = "Shawn Lee";
const country = "China";
let is_married = false;
let age = 14;
let message = `${name} lives in ${country}`;
console.log(message);
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
const message = `${name} is married`;
console.log(message);
if (true) {
console.log("Nested If Statement!");
if (true) {
console.log("Yet Another Nested If Statement!");
}
}
} else {
console.log(`${name} is not married!`);
}
if (has_children) {
const message = `${name} has children`;
console.log(message);
} else {
console.log(`${name} has no children!`);
}
function greet(name, is_married, has_newline) {
console.log(`Hello, ${name}!`);
if (is_married) {
console.log("You are married, great!");
} else {
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
console.log(`Thank you for leaving your message, ${name}`);
}
function print_country(name, population) {
console.log(`${name} has this many millions of people: ${population}`);
}
let amount = 1630;
console.log();
print_country(country, amount);
console.log(1630, "millions is a lot of people");
amount = 3;
for (let i = 0; i < amount; i++) {
console.log("Outer loop has run for 3 times");
for (let i = 0; i < 3; i++) {
console.log("Inner loop has run 3 times");
}
}
function handle_spam(message, times, with_index) {
if (with_index) {
for (let time = 0; time < times; time++) {
console.log(time, message);
}
} else {
for (let i = 0; i < times; i++) {
console.log(`${message} has been spammed for ${times} times`);
}
}
}
handle_spam("No one expects the Spanish Inquisition!", 3, false);
handle_spam("No one expects the Spanish Inquisition!", 3, true);
for (let time = 0; time < 4; time++) {
console.log(time);
}
function get_name(name, last_name, is_great) {
if (is_great) {
return `${name} ${last_name} the great`;
}
return `${name} ${last_name}`;
}
let full_name = get_name("John", "China", true);
console.log(full_name);
full_name = get_name(name, country, false);
console.log(full_name);
let nick_name = full_name = "Shawn Lee";
console.log(`full_name: ${full_name}, nick_name: ${nick_name}`);
function with_no_expression() {
console.log("This fn has a return statement with no expression");
return;
}
with_no_expression();
function dollar_to_yen(dollar) {
return dollar * 150;
}
function dollar_to_ruble(dollar) {
return dollar * 91;
}
let dollar = 120;
let yen = dollar_to_yen(dollar);
let ruble = dollar_to_ruble(dollar);
console.log(`${dollar} dollars are`, yen, "yen");
console.log(`${dollar} dollars are`, ruble, "ruble");
ruble = dollar_to_ruble(900);
if (ruble > 30000) {
console.log("That is a lot of rubles!");
}
