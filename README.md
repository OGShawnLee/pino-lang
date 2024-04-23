# Pino

All the programming languages I've used have something I like and dislike, so I decided to make one based on the language I like the most, the V programming language.
I am studying Software Enginnering and C++ is the language I am being taught, so that is the language used in the project. 
Pino is transpiled to JavaScript because it is the language I know the best and I don't know how to compile it to a lower level.

## Why Pino?
Every cool name I thought of was already used and at that moment I was thinking about **Ergo Proxy**, *my all time favourite anime*. Since **Pino** is a little adorable character from that anime and it is a short name as well... **Pino! Pino! Pino!**

## Comments

Multi Line Comments are not yet supported but the intended syntax is shown right below.

```
# Single Line Comment
###
  Multi
  Line
  Comment
###
```

## Variables

```
# Variable
var name = "Shawn Lee"
# Constant
val country = "China"
# Reassignment
name = "John China"
# To Variable
var people = name
```

## String Injection

```
val name = "Shawn Lee"
val country = "China"
val budget = 0
var message = "$name lives in $country and has a budget of $budget"
```

## Vectors

```
fn get_str(it int) {
  return "$it: What is going on fella!"
}

var arr_int = []int { len: 6, init: it * 2 }
var arr_str = []str { len: 9, init: get_str(it) }

println(arr_int, arr_str)

struct Game {
  name str
  characters arr
}

println(Game {
  name: "Gears of War"
  characters: ["Marcus", "Dominic", "Baird", "Cole"]
})

val game = Game {
  name: "Halo"
  characters: ["Master Chief", "Cortana", "Captain Keyes", "Sergeant Johnson", "343 Guilty Spark"]
}

fn print_game_characters(game Game) {
  val len = game:characters:length
  println("$game:name Characters $len")
  for i in len {
    val char = game:characters[i]
    println("  Character $i: $char")
  }
}

print_game_characters(game)

val languages = ["Vlang", "Swift"]
val vlang = languages[0]

println("Languages:", languages, vlang)
```

## Functions

```
fn get_full_name(name str, last_name str) {
  return "$name $last_name"
}

var full_name = get_full_name("Shawn", "Lee")

println(full_name)

fn to_ruble(dollar int) {
  return dollar * 91
}

fn to_yen(dollar int) {
  return dollar * 150
}

val dollar = 10
val rubble = to_rubble(dollar)
val yen = to_yen(dollar)

println("$dollar dollars are $rubble rubbles and $yen yens")
```

## Control Flow

```
var has_girlfriend = false

if has_girlfriend {
  println("Shawn Lee has a girlfriend!")
} else {
  println("Shawn Lee is still single after all these years!")
}
```

## Loop Statement

```
# Times Loop
for 10 {
  println("This loop has run for 10 times")
}

# In Loop
var times = 10
for i in times {
  println("This has run for the $i time a total of $times")
}
```

## Structs

```
struct Phone {
  brand str
  name str
}

struct Person {
  full_name str
  is_married bool
  budget int
  phone Phone
}

fn create_phone(brand str, name str) {
  return Phone { brand: brand name: name }
}

val person = Person {
  full_name: "Shawn Lee"
  is_married: 50 < 10
  budget: 1000 - 950
  phone: create_phone("Apple", "15 Pro Max")
}

val name = person:full_name
val phone_name = person:phone:name

println(person)
println("$name owns a $phone_name")
println("$person:full_name has a budget of $person:budget $")
```

## Functional Programming

```
# Higher Order Function
fn get_multiplier_fn(multiplier int) {
  return fn (num int) {
    return num * multiplier
  }
}

val double_it = get_multiplier_fn(2)

fn times_ten(num int) {
  return num * 10
}

fn map(array arr, fun function) {
  return []any { len: array:length, init: fun(array[it]) }
}

val arr_int = []int { len: 4, init: times_ten(it) }
val arr_double = map(arr_int, double_it)
val arr_triple = map(arr_int, get_multiplier_fn(3))

println("Array Integers x 1:", arr_int)
println("Array Integers x 2:", arr_double)
println("Array Integers x 3:", arr_triple)

fn fold(array arr, initial any, fun function) {
  var acc = initial

  for i in array:length {
    acc = fun(array[i], acc)
  }

  return acc
}

var total = fold(arr_int, 0, fn (current int, acc int) {
  return acc + current
})

println("Total of [$arr_int] = $total")

# Assigning a Lambda to a Constant
val add = fn (a int, b int) {
  return a + b
}

total = fold(arr_double, 0, add) 
println("Total of [$arr_double] = $total")

total = fold(arr_triple, 0, add) 
println("Total of [$arr_double] = $total")
```

## Missing Features
- [X] Binary Expressions
  - [X] Dual Character Bool Operators (==, !=, >=, <=)
  - [ ] Order of Precedence (JavaScript handles it once it is transpiled but it is not built in Pino)
  - [ ] Parenthesis
- [ ] Checker (Parser output is transpiled without validation)
- [ ] Comments
  - [X] Single Line Comment
  - [ ] Multi Line Comment
- [ ] Descriptive Parser and Lexer Errors
- [ ] Floats
- [ ] Functions
  - [ ] Default Parameter Value
  - [ ] Function Return Typing
  - [X] Lambda (Anonymous Function)
  - [X] Return Statement Vector Initialisation Support
- [ ] Else If Statement
- [ ] Match Statement
- [ ] Modules and Import Statements
- [X] Strings
  - [X] Struct Property Access Injection Support ("$struct:property")
- [ ] Structs
  - [X] Struct Definition
  - [X] Struct Initialisation
  - [ ] Struct Operations (delete, read, set)
  - [X] Struct Type for fn parameters
  - [ ] Optional Commas
  - [ ] Property Shortcut (when a variable is used as a value and has the same of a property, we skip the property and the colon)
- [ ] Vectors
  - [X] Vector Accesing
  - [X] Vector Initialisation
  - [X] Vector Literal (["Marcus", "Dominic", "Baird", "Cole"])
  - [ ] Vector Operations (pop, prepend, push, shift)
  - [ ] Vector Type for fn parameters
- [ ] Yield Statement
