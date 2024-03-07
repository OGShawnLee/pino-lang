# Pino

All the programming languages I've used have something I like and dislike, so I decided to make one based on the language I like the most, the V programming language.
I am studying Software Enginnering and C++ is the language I am being taught, so that is the language used in the project. 
Pino is transpiled to JavaScript because it is the language I know the best and I don't know how to compile it to a lower level.

## Comments

Comments are not yet supported but the intended syntax is shown right below.

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

Structs definitions are supported, however instances are not but the intended syntax is defined.

```
struct Person {
  name str
  last_name str
  country str
  children int
  is_married bool
}

# not supported yet
val person = Person {
  name: "Shawn Lee"
  last_name: "Lee"
  country: "China"
  children: 0
  is_married: false
}
````

## Missing Features
- [x] Binary Expressions
  - [ ] Dual Character Bool Operators (==, !=, >=, <=)
  - [ ] Order of Precedence (JavaScript handles it once it is transpiled but it is not built in Pino)
  - [ ] Parenthesis
- [ ] Checker (Parser output is transpiled without validation)
- [ ] Descriptive Parser and Lexer Errors
- [ ] Floats
- [ ] Function Return Typing
- [ ] Else If Statement
- [ ] Match Statement
- [ ] Modules and Import Statements
- [ ] Structs
  - [x] Struct Definition
  - [ ] Struct Initialisation
  - [ ] Struct Operations (delete, read, set)
  - [ ] Struct Type for fn parameters
- [ ] Vectors
  - [ ] Vector Accesing
  - [x] Vector Initialisation
  - [ ] Vector Operations (pop, prepend, push, shift)
  - [ ] Vector Type for fn parameters
- [ ] Yield Statement
