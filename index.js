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
  name: "Halo Reach",
  release_year: 2010,
  characters: ["Carter", "Kat", "Jun", "Emile", "Jorge", "Noble 6"],
});
const game_a = {
  name: "Gears of War",
  release_year: 2005,
  characters: ["Marcus", "Dominic", "Cole", "Baird"],
};
const game_b = {
  name: "Halo",
  release_year: 2001,
  characters: ["Master Chief", "Cortana", "Captain Keyes", "Sergeant Johnson", "343 Guilty Spark"],
};
function print_game_characters(game) {
const len = game.characters.length;
console.log(`${game.name} Characters ${len}`);
for (let i = 0; i < len; i++) {
const char = game.characters[i];
console.log(`  Character ${i}: ${char}`);
}
}
print_game_characters(game_a);
print_game_characters(game_b);
const languages = ["Vlang", "Swift"];
const vlang = languages[0];
console.log("Languages:", languages, vlang);
function create_characters_from_game(game, weapon) {
const temp_arr = [];
for (let it = 0; it < game.characters.length; it++) temp_arr[it] = {
  name: game.characters[it],
  weapon: {
  name: weapon,
  durability: 100,
},
};
return temp_arr;
}
const characters = create_characters_from_game(game_a, "Assault Lancer Rifle");
console.log(`${game_a.name}`, characters);
