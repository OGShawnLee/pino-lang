console.log("Pino!");
console.log("This was written in Pino and transpiled to JavaScript!");
let name = "Shawn Lee";
let country = "China";
console.log(`${name} has created this language!`);
function print_budget(name, budget) {
console.log(`${name} has a budget of ${budget} $`);
}
print_budget(name, 0);
const person = {
  name: "Shawn Lee",
  country: "China",
  is_married: false,
};
function print_person(person) {
const message = `${person.name} lives in ${person.country} and`;
if (person.is_married) {
console.log(`${message} is married!`);
} else {
console.log(`${person.name} lives in ${person.country} and is married!`);
}
}
function times(a, b) {
return a * b;
}
function dollar_to_ruble(dollar) {
return times(dollar, 91);
}
function dollar_to_yen(dollar) {
return times(dollar, 151);
}
const dollars = 100;
rubles = dollar_to_ruble(100);
yens = dollar_to_yen(100);
console.log(`${dollars} are ${rubles} rubles and ${yens} yens`);
if (yens > 10000 && true) {
console.log("That is a lot of yens!");
}
for (let i = 0; i < 3; i++) {
console.log("This has run for 3 times!");
}
const amount = 3;
for (let time = 0; time < amount; time++) {
console.log(`This has run for the ${time} a total of ${time}s times!`);
}
function get_str(it) {
return `${it}: What is going on fella!`;
}
let arr_int = [];
for (let it = 0; it < 6; it++) arr_int[it] = it * 2;
let arr_str = [];
for (let it = 0; it < 9; it++) arr_str[it] = get_str(it);
console.log(arr_int, arr_str);
const character = {
  person: person,
  weapon: {
  name: "Sword",
  durability: 100,
},
};
console.log(character);
console.log(`${character.person.name} wields a ${character.weapon.name} as a weapon!`);
console.log({
  name: "Gears of War",
  planets: ["Marcus", "Dominic", "Baird", "Cole"],
});
const languages = ["Vlang", "Swift"];
console.log("Languages:", languages);
