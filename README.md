# Pino
Simple and aesthetic programming language built with C++ that intends to turn programming a joyful experience.

## Why?

All the programming languages I've used have something I like and dislike, so I decided to make one that takes the good from all those languages; a language tailored to my taste. I am studying Software Engineering and I thought this project would help me test what I've learnt and become a better programmer by not only doing Web Development but some Systems Programming with a gigachad language such as C++ as well.

## Features
- [X] Constant and Variable Declaration
- [X] Function Declaration
  - [X] Parameter Declaration
  - [X] Optional Parameter List (no parenthesis)
  - [X] Parameter List
    - [X] Optional Commas
  - [X] Return Statement 
  - [ ] Return Typing
- [X] Struct Declaration
  - [X] Attribute Declaration
  - [X] Method Declaration
- [X] Enum Declaration
- [X] Expression
  - [X] Binary Expression
    - [X] Enum Member Access
    - [X] Struct Attribute / Method Access
    - [ ] Order of Precedence
    - [ ] Parenthesis
  - [X] Identifier
  - [X] Literal
    - [X] Boolean
    - [X] Float
    - [X] Integer
    - [X] String
      - [X] String Injection
    - [X] Function Lambda
      - [X] Parameter Declaration
      - [X] Optional Parameter List (no parenthesis)
      - [X] Parameter List (no parenthesis)
        - [X] Optional Commas
      - [X] Return Statement
      - [ ] Return Typing
  - [X] Function Call
  - [X] Loop Statement
    - [X] For In Loop (``for i in <iterable> {}``)
    - [X] For Times Loop (``for i in <integer> {}``)
    - [ ] Loop Keywords (continue, break)
  - [X] Struct Instance
    - [X] Property Accessing
    - [ ] Shorthand Syntax 
  - [X] Vector
    - [X] Initial Elements
    - [X] Init Block
- [X] Conditional
  - [X] If Statement
    - [X] Else If Statement
  - [X] Else Statement
  - [X] Match Statement
    - [X] When Statement
      - [X] Multiple Expressions
    - [X] Default Statement (Else Statement)

## Syntax

The syntax is heavily inspired by the programming language that has the best one in my opinion, Vlang (and Golang I guess). There are also bits inspired by other languages like Kotlin and Ruby.

### Variables

Constants are declared using the **val** keyword and variables with the **var** keyword. Variables must be assigned a value. (This syntax comes from Kotlin).

```
var name = "Shawn Lee"
var country = "China"
var children = 0
var budget = 12.5 # In terms of money, I have none :yikes:
var is_married = false

val planet = "Earth"
val pi = 3.1416
```

### String Injection

Variables can be easily interpolated inside of strings by writting their name preceded by a **#** character. (This syntax comes from Ruby).

```
val name = "Augustus"
val empire = "Roman"

println("#name was the first emperor of the #empire Empire")

val planet = "Earth"
val diameter = 12714
val message = "#planet has a diameter of #diameter kilometres"
```

### Functions
Functions are declared with the **fn** keyword followed by their name and their parameters.

* Parenthesis are optional if the function does not have any parameters.
* Commas are optional to separate parameters, it is recommeded to add them when the parameters are written in a single line otherwise omit them.
* When calling a function, commas are optional to separate its arguments.

```
fn print {
  println("This is a random print, doesn't do much but it's honest work.")
}

fn greet(name str, from str) {
  println("Greetings from #from, #name.")
}

fn get_message(
  name str
  country str
  budget float
) {
  return "#name lives in #country and has a budget of #budget"
}

fn get_screaming_message(message str) {
  return message:uppercase()
}

fn double_it(amount int) {
  return amount * 2
}

fn get_str(prompt str) {
  return readline(prompt)
}

fn get_float(prompt str) {
  return float(get_str(prompt))
}

val final_message = get_message(
  get_str("What is your name?")
  get_str("What country do you live in?")
  get_float("What is your budget?")
)

println("Message: #final_message")
```

### Function Lambdas

Lambdas are anonymous functions that are treated as expressions. They are declared with the **fn** keyword and evidently they don't have a name, and they benefit from the syntax of normal functions: optional commas and parenthesis.

```
val first_name = "Shawn"
val last_name = "Lee"

val get_full_name = fn (first_name str, last_name str) {
  return "#first_name #last_name"
}

val greet = fn {
  val full_name = get_full_name(first_name, last_name)
  println("Hello from the creator of Pinolang, #full_name")
}

val print_person = fn (
  first_name str 
  last_name str
  country str
) {
  val full_name = get_full_name(first_name, last_name)
  println("#full_name lives in #country")
}

greet()
print_person("John", "China", "China")
```

### Structs

Structs are declared with the **struct** keyword followed by their name and their body declaration. Attributes are declared with their name followed by their type.

* Commas are optional to separate attributes. It is recommended to keep them if the struct declaration is written in a single line, otherwise omit them.

```
struct Country { name str, continent str }
struct Person {
  name str
  country Country
  children int
  height float
  is_married bool
}
```

### Struct Instances

To initialise a struct instance declare the name of the struct followed by the definition of its body composed by properties. Properties are defined by their **identifier**, a **colon**, and their **value** which must be an expression (``name: "Shawn"``).

* Commas are optional to separate properties, it is recommended to keep them when multiple properties are declared on a single line, otherwise omit them.
* Accessing an struct instance is done with the member access operator ``:``.

```
struct Country { name str, continent str }
struct Person {
  name str
  country Country # Nested Struct
  children int
  height float
  is_married bool
  country Country
}

val person = Person {
  name: "Shawn Lee"
  country: Country { name: "China", "Asia" } # Nested Struct Instance
  children: 0
  height: 1.74
  is_married: false
}

println(person:name + "lives in " + country:name) # Accesing a Property
```

### Vectors

A vector is a dynamic array of elements of the same type.

* Use ``[<element-1>, <element-2>, <element-n>]`` for initialising a vector with literal elements, its type will be infered from the first element.
  * Commas are optional to separate members, it is recommended to keep them when multiple elements are declared on the same line, otherwise omit them.
* Use ``[]<type>`` for initialising an empty vector that will be filled with elements of the given type.
* Use ``[]<type> { len: <integer>, init: <expression> }`` for quickly declaring an array with a given length and filling each index with the result of an expression. Both ``len`` and ``init`` must be declared.
  * ``len``: is the length of the vector and the number of times the init expression will be evaluated.
  * ``init``: is an expression that will be called at each index of the vector and its value will be assigned at that position. This expression has access to a context variable called ``it`` that represents the current index. A lambda can be used if multiple stataments are needed for computing a value, or for renaming ``it``.

```
val countries = [
  "Portugal"
  "Spain"
  "France"
  "Italy"
  "England"
  "Scotland"
  "Ireland"
]

for country in countries {
  println("#country is in Europe.")
}

val scores = []float
val integers = []int {
  len: 30
  init: it * 1
}

# '[]int' as a type is not supported yet
val matrix = [][]int {
  len: 3
  init: fn (row int) {
    return []int {
      len: 3
      init: fn (col int) {
        for {
          val integer = int(readline("Enter a cero or positive integer for position (#row, #col): "))
          # Conditional Statements are not supported yet
          if integer < 0 { 
            println("Negative numbers are not allowed, please enter a number number")
          }
          return integer
        }
    }
  }
}
```

### Enums

Enums are declared with the **enum** keyword followed by their name and their members. Members are just identifiers and I recommend following the SCREMING_SNAKE_CASE naming convention... not sure if I should enforce it.

* Commas are optional to separate members, it is recommended to keep them when multiple members are declared on the same line, otherwise omit them.
* Accessing an enum member value is done with the ``::`` operator.

```
enum Planet {
  MERCURY, VENUS, EARTH, MARS
  JUPITER, SATURN, URANUS, NEPTUNE
}

val planet = Planet::EARTH
```

### Loop Statement

Loops can only be declared with the **for** keyword, there is no while nor do keyword.

* Use ``for <it> in <iterable> {}`` for iterating over an iterable expression such as a vector or an string.
* Use ``for <index> in <integer> {}`` for executing a set of instructions a certain number of times while keeping a reference to the current iteration index.
* Use ``for <integer> {}`` for executing a set of instructions a certain number of times.
* Use ``for <condition> {}`` for executing a "while" kind of loop. Break out of the loop with keywords (**break, continue, return**).
* Use ``for {}`` for executing infinite loop, Break out of the loop with keywords.
 
```
val characters = [
  "Marcus"
  "Dominic"
  "Baird"
  "Cole"
]

for character in characters {
  println("#character is an awesome Gears of War character!")
}

for time in 100 {
  println("This has run for the #time time for a total of 100 times")
}

for 100 {
  println("This will run a hundred times")
}

var is_sleeping = true
for is_sleeping {
  println("Pablo is sleeping...")
  is_sleeping = false
}

println("Pablo is no longer sleeping!")

for {
  println("This will run forever!")
  break # not really
}
```

### Conditional Statements

Conditional statements are straightforward and there is not much to say about them, the condition for all of them doesn't require parenthesis.

* `if`: an if statement is declared with the `if` keyword followed by a condition and its block body.
* `else if`: an else if statement is declared with the keywords `else if` followed by a condition and its block body.
* `else`: an else statement is declared with the keyword `else` followed by its block body.
* `match`: a match statement is declared with the `match` keyword followed by a condition and `when` statements or a default `else` branch.
* `when`: a when statements acts as a branch for a `match` statement, it is declared with the `when` keyword followed by one or multiple expressions.

```
if true {
  println("This if statement will always run")
}

if false {
  println("This if statement will never run")
} else {
  println("This else statement will always run")
}

val budget = 12.5

if budget > 10000 {
  println("Damn, you a G!")
} else if budget > 5000 {
  println("Ight, not bad!")
} else if budget > 3000 {
  println("Almost decent, common get you shit together!")
} else {
  println("Damn, you a brokie!")
}

match readline("Enter a planet of the solar system to teleport: ")
when "Earth" {
  println("You are alive")
}
when "Sun" {
  println("The sun is not a planet! You dead anyways")
}
else {
  println("You dead")
}

match readline("Enter a planet of the solar system: ") 
when 
"Mercury"
"Venus"
"Earth"
"Mars" {
  println("Rocky Planet")
}
when "Jupiter", "Saturn" {
  println("Gas Giant")
}
when "Uranus", "Neptune" {
  println("Ice Giant")
}
when "Sun" {
  println("The sun is a star not a planet!")
}
else {
  println("Not a planet I know of")
}
```

<!-- # Pino

Simple and aesthetic programming language built with C++ that intends to turn programming a joyful experience.

All the programming languages I've used have something I like and dislike, so I decided to make one based on the language I like the most, the V programming language.
I am studying Software Enginnering and C++ is the language I am being taught, so that is the language used in the project. 
Pino is transpiled to JavaScript because it is the language I know the best and I don't know how to compile it to a lower level.

REWRITE: PINO WILL BE REWRITTEN FROM THE GROUND UP BUT THERE WILL NOT BE MANY CHANGES TO THE SYNTAX 

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
val weight = 64.5
var message = "$name lives in $country, has a budget of $budget and weighs $weight kg"
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
  return Phone { brand, name }
  # or Phone { brand name } (commas are optional)
  # or Phone { brand: brand, name: name } (with no prop shortcut)
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
- [X] Floats
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
  - [X] Optional Commas
  - [X] Property Shortcut
- [ ] Vectors
  - [X] Vector Accesing
  - [X] Vector Initialisation
  - [X] Vector Literal (["Marcus", "Dominic", "Baird", "Cole"])
  - [ ] Vector Operations (pop, prepend, push, shift)
  - [ ] Vector Type for fn parameters
- [ ] Yield Statement -->
