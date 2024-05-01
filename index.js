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
const rubles = dollar_to_ruble(100);
const yens = dollar_to_yen(100);
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
for (let time = 0; time < 2 * 2; time++) {
console.log(`This has run for the ${time} a total of 4 times!`);
}
for (let name of ["Augustus", "Tiberius", "Caligula"]){
console.log(`${name} was a Roman Emperor!`);
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
console.log(`${game.name} Characters ${game.characters.length}`);
for (let i = 0; i < game.characters.length; i++) {
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
let random = ["Not an Integer"];
random = [12];
function get_full_name(name, last_name) {
return `${name} ${last_name}`;
}
let full_name = get_full_name("James", "China");
name = get_full_name("Julius", "Ceasar");
name = full_name;
country = "Japan";
function can_drink(age) {
return age >= 21;
}
function has_great_name(first_name, last_name) {
return first_name == "Shawn" && last_name == "Lee";
}
function handle_drinking_age(age) {
if (can_drink(age)) {
console.log("You can drink!");
} else {
console.log("You can't drink!");
}
}
function handle_great_name(first_name, last_name) {
if (has_great_name(first_name, last_name)) {
console.log(`${first_name}, you have a great name!`);
} else {
console.log(`${first_name}, you dont have a great name!`);
console.log("What a shame...");
}
}
handle_drinking_age(20);
handle_drinking_age(21);
handle_great_name("Shawn", "Smith");
handle_great_name("Shawn", "Lee");
function get_multiplier_fn(multiplier) {
return function (num) {
return num * multiplier;
}
;
}
const double_it = get_multiplier_fn(2);
function times_ten(num) {
return num * 10;
}
function map(array, fun) {
const temp_arr = [];
for (let it = 0; it < array.length; it++) temp_arr[it] = fun(array[it]);
return temp_arr;
}
const arr_int_big = [];
for (let it = 0; it < 4; it++) arr_int_big[it] = times_ten(it);
const arr_double = map(arr_int_big, double_it);
const arr_triple = map(arr_int_big, get_multiplier_fn(3));
console.log("Array Integers x 1:", arr_int_big);
console.log("Array Integers x 2:", arr_double);
console.log("Array Integers x 3:", arr_triple);
const arr_quadruple = map(arr_int_big, function (num) {
return num * 4;
}
);
console.log("Array Integers x 4:", arr_quadruple);
function fold(array, initial, fun) {
let acc = initial;
for (let i = 0; i < array.length; i++) {
acc = fun(array[i], acc);
}
return acc;
}
let total = fold(arr_int_big, 0, function (current, acc) {
return acc + current;
}
);
console.log(`Total of [${arr_int_big}] = ${total}`);
const add = function (a, b) {
return a + b;
}
;
total = fold(arr_quadruple, 0, add);
console.log(`Total of [${arr_quadruple}] = ${total}`);
let animal = {
  name: "Penguin",
  is_extinct: false,
};
console.log(animal);
