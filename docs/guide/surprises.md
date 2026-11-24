# Things that surprise newcomers

Pino borrows from many languages, so most of it will feel familiar. This page lists the places where it does something *different* from what Python, JavaScript, Go or Java would do. Each entry says what you'd expect, what Pino does, why, and where the rule is written down.

Most snippets run as short scripts (no `fn main`). Every example was run through the checker (`pino check`). Lines marked with an error code show exactly what the checker says.

Looking for the big one, copies and `mut`? That has its own page: [Values, not objects](values-and-mutation.md).

**On this page:** [Numbers](#numbers) · [Syntax](#syntax) · [Functions and structs](#functions-and-structs) · [Order, modules and `main`](#order-modules-and-main) · [Collections](#collections) · [Strings and runes](#strings-and-runes) · [Absence, errors and `match`](#absence-errors-and-match)

---

## Numbers

### `/` always gives a float, `//` divides integers

- **You'd expect:** `5 / 2` is `2` (C, Java, Go), or `2.5` (Python 3).
- **Pino does:** `/` always returns a `float`. `//` is integer (floor) division and rounds *down*, so `-7 // 2` is `-4`.

<!-- check: ok -->
```pino
println(5 / 2)     # 2.5
println(6 / 3)     # 2.0
println(5 // 2)    # 2
println(-7 // 2)   # -4
```

- **Why:** an integer division should never happen by accident. You write `//` when you mean it. This is also why `n /= 2` on an `int` is an error (use `//=`):

<!-- check: PN3102 -->
```pino
var n = 10
n /= 2   # PN3102: Cannot use '/=' on an 'int' target because '/' produces a 'float'; use '//=' ...
```

*Spec: TYPE-9, TYPE-14, TYPE-15, VAR-E9, D-019, D-164.*

### `%` rounds the same way as `//`, and works on floats

- **You'd expect:** `-7 % 2` is `-1` (C, Java, Go, JavaScript) and `%` doesn't work on floats (Go).
- **Pino does:** `%` is the *floored* modulo, the partner of `//`, so the answer takes the sign of the divisor. It also works on floats.

<!-- check: ok -->
```pino
println(-7 % 2)      # 1
println(-7.5 % 2)    # 0.5
println(2.0)         # 2.0: a float keeps its ".0" when printed
```

- **Why:** one rule that always holds: `a == (a // b) * b + a % b`, for ints and floats. (Python does the same.)

*Spec: TYPE-14, TYPE-18, TYPE-10, D-164, D-171.*

### Dividing by zero panics, even for floats

- **You'd expect:** `1.0 / 0.0` is `Inf` (JavaScript, Go, Java), or an exception (Python).
- **Pino does:** at run time, `/`, `//` and `%` by zero **panic** and end the program (exit code 101). There is no `Inf` or `NaN`. If the divisor is a constant zero, the checker catches it before you run anything.

<!-- check: PN3501 -->
```pino
println(1 / 0)   # PN3501: Division by Zero: divisor is statically zero.
```

<!-- check: ok -->
```pino
var zero = 0
println(10 / zero)   # compiles fine, then panics at run time
```

- **Why:** a quiet `Inf` or `NaN` travels a long way before it hurts. A loud stop is easier to debug.

*Spec: TYPE-E1, TYPE-E4, ERR-3, ERR-5, D-166, D-170.*

### `int` widens to `float`, never the other way

- **You'd expect:** `val n int = 1.5` quietly truncates (C), or you get an `int` anyway.
- **Pino does:** an `int` becomes a `float` where one is needed (`1 + 2.5`, `val x float = 1`). A `float` is never narrowed to an `int` silently.

<!-- check: PN3102 -->
```pino
val n int = 1.5   # PN3102: Cannot assign type 'float' to variable 'n' of type 'int'.
```

- **Why:** narrowing loses information, so you have to ask for it.

*Spec: TYPE-9, TYPE-19, TYPE-E3, D-019, D-172.*

### `int("12")` gives a `Result`, not a number

- **You'd expect:** `int("12")` is `12`, or throws.
- **Pino does:** converting a *string* returns a `Result[int, string]`, because the text might not be a number. Converting a number gives a plain `int`.

<!-- check: ok -->
```pino
val n = int("12") or { yield 0 }
println(n + 1)   # 13
```

- **Why:** failure is part of the type, so you can't forget to handle it. (See [Absence, errors and `match`](#absence-errors-and-match).)

*Spec: TYPE-11, BUILTIN-16, D-015.*

---

## Syntax

### `:` reads a member, `::` reaches into a type or module

- **You'd expect:** `p.x` and `Point.origin()`.
- **Pino does:** `:` is for members of a *value* (`p:x`, `v:push(1)`, `s:len`). `::` is for things that belong to a *type or module* (`Point::origin()`, `Color::Red`, `Ids::new_id()`).

<!-- check: ok -->
```pino
struct Point {
  x int
  y int

  static fn origin() Point {
    return Point { x: 0, y: 0 }
  }
}

enum Color { Red Green }

val p = Point::origin()
println(p:x)        # 0
println(Color::Red) # Color::Red
```

- **Why:** you can tell at a glance whether you're looking inside a value or at a name.

*Spec: STRUCT-1, STRUCT-11, ENUM-2, MOD-2, ENUM-5.*

### Map literals use `->`, and `=>` is not a thing

- **You'd expect:** `{"a": 1}` (Python, JavaScript) or `"a" => 1`.
- **Pino does:** `map[string, int] { "a" -> 1 }`. The arrow is `->` everywhere: map entries, lambdas, function types, `match` arms.

<!-- check: ok -->
```pino
val ages = map[string, int] { "ann" -> 31, "bo" -> 27 }
println(ages["ann"])   # 31
```

<!-- check: PN2010 -->
```pino
val m = map[string, int] { "a" => 1 }   # PN2010: '=>' is not valid; use '->' instead.
```

- **Why:** `->` is the only arrow in the language, so it reads the same everywhere. A bare `{ ... }` is for blocks, so maps and sets always say what they are.

*Spec: MAP-1, MAP-E4, LEX-18, LEX-E5, D-100.*

### Commas are optional, but a trailing comma is an error

- **You'd expect:** commas required between items (most languages), and a trailing comma is fine (Go, JavaScript, Python).
- **Pino does:** items can be separated by whitespace or newlines, with or without commas. A comma after the *last* item is an error.

<!-- check: ok -->
```pino
val a = [1 2 3]
val b = [1, 2, 3]
println(a == b)   # true
```

<!-- check: PN2007 -->
```pino
val v = [1, 2,]   # PN2007: Trailing comma is not allowed.
```

- **Why:** one way to write a list, with no dangling commas. (In lists where commas are optional, `a -b` is flagged as ambiguous. Write `a - b`, or `a, -b` for two items.)

*Spec: LEX-9 to LEX-19, LEX-E2, LEX-E4, D-010, D-108.*

### `and`, `or`, `not`, and conditions must be `bool`

- **You'd expect:** `&&`, `||`, `!`, and `if x` working on numbers or strings ("truthy" values).
- **Pino does:** conditions are spelled with words, and only a real `bool` is accepted. Braces are required, parentheses are not. The expression form is `if c then a else b`.

<!-- check: ok -->
```pino
val a = 1
val b = 2
println(a < b and not (a == b))        # true
val word = if a < b then "less" else "more"
println(word)                          # less
```

<!-- check: PN3112 -->
```pino
if 0 {                      # PN3112: The 'if' condition must have type 'bool', but got 'int'.
  println("zero")
}
```

- **Why:** no accidental truthiness (is `0` false? an empty string?). Say what you mean: `n != 0`.

*Spec: TYPE-5, IF-1, IF-5, IF-E1, IF-E4, D-032.*

### There is one loop keyword, `for`

- **You'd expect:** `while`, `for (;;)`, `for i in range(n)`.
- **Pino does:** `for` covers all of it. The head can be empty (forever), a count, a `bool` condition, or `x in collection`. In a counted loop, the index is the implicit `it`.

<!-- check: ok -->
```pino
for 3 {
  println(it)   # 0, then 1, then 2
}

var n = 3
for n > 0 {
  n -= 1
}
```

- **Why:** one word to learn. And loop variables can't be changed inside the loop.

*Spec: LOOP-1, LOOP-3, LOOP-4, LOOP-13, LOOP-E2, D-182.*

### `for v in map` gives the *values*

- **You'd expect:** looping over a map gives keys (Python, and JavaScript's `for...in`), or key-value pairs (Go's `range`).
- **Pino does:** `for v in m` gives the **values**. Use `for k, v in m` for both. With a vector, `for i, x in v` puts the index *first*.

<!-- check: ok -->
```pino
var m = map[string, int] { "a" -> 1 }
for v in m {
  println(v)           # 1
}
for k, v in m {
  println("$k $v")     # a 1
}
for i, x in [10, 20] {
  println("$i $x")     # 0 10, then 1 20
}
```

- **Why:** it matches how Pino's own compiler loops over maps, and it's the same rule for every collection: the *last* name is always the element.

*Spec: LOOP-7, LOOP-9, LOOP-10, D-120.*

### Names of types start with a capital letter

- **You'd expect:** any spelling works, as in Python or JavaScript (`class point`).
- **Pino does:** every struct, interface, enum, union, enum member and union variant must begin with `A` to `Z`. Variables, fields and functions can be spelled any way you like.

<!-- check: PN3010 -->
```pino
struct point {   # PN3010: Name 'point' must be capitalized
  x int
}
```

- **Why:** Pino enforces its naming style: `PascalCase` for types, `snake_case` for everything else. Only the capital letter on type names is checked today.

*Spec: LEX-20, LEX-21, LEX-E6, D-014.*

### A few line breaks matter

- **You'd expect:** a statement ends when the expression looks finished; `return` followed by a new line returns nothing (JavaScript and Go both end the statement there).
- **Pino does:** `return` at the end of a line *continues* onto the next line. It returns nothing only when the next thing is `}` or the end of the file. A call's `(` must be on the same line as the function name.

<!-- check: ok -->
```pino
fn plus_one(a int) int {
  return
    a + 1
}

println(plus_one(1))   # 2
```

- **Why:** it lets you break a long returned expression across lines, which the self-hosted compiler's own code does.

*Spec: FN-22, FN-23, D-142, D-144.*

---

## Functions and structs

### No `this`: fields are in scope inside methods

- **You'd expect:** `this.x` (Java, JavaScript) or `self.x` (Python), or a receiver name (Go).
- **Pino does:** inside a method you use a field by its bare name (`x`), and a sibling method by its bare name too. `self` is still there if you want to be explicit, and it's how you get at a field a parameter has shadowed.

<!-- check: ok -->
```pino
struct Point {
  x int
  y int

  fn sum() int {
    return x + self:y
  }
}

println(Point { x: 1, y: 2 }:sum())   # 3
```

- **Why:** less noise. A local name or parameter that shadows a field wins, so `self:x` reaches the field.

*Spec: STRUCT-3, STRUCT-5, STRUCT-18, STRUCT-19, D-073, D-075.*

### A struct literal must name every field

- **You'd expect:** missing fields get a zero value (Go) or `undefined` (JavaScript), or there's a constructor with defaults.
- **Pino does:** you write all of them, or it's an error. A bare name is shorthand: `Point { x, y }` means `Point { x: x, y: y }`.

<!-- check: PN3207 -->
```pino
struct Point {
  x int
  y int
}

val p = Point { x: 1 }   # PN3207: Missing property 'y' in struct literal 'Point'.
```

- **Why:** no hidden zero values and no half-built objects.

*Spec: STRUCT-E12, STRUCT-15, D-078, D-104.*

### Interfaces are matched by shape

- **You'd expect:** a Java-style `implements` clause.
- **Pino does:** there is none. A struct satisfies an interface just by having the right methods (like Go).

<!-- check: ok -->
```pino
interface Named {
  fn name() string
}

struct Dog {
  fn name() string {
    return "Rex"
  }
}

fn greet(n Named) {
  println(n:name())
}

greet(Dog {})   # Rex
```

*Spec: IFACE-2, IFACE-3.*

### No overloading, and no default parameter values

- **You'd expect:** two functions with the same name but different parameters (Java), or `greet(name, punct = "!")` (Python, JavaScript).
- **Pino does:** one name, one function per scope. A default value is an error. *Defaults aren't supported yet, since the feature hasn't been designed.* Until then, write two functions with two names.

<!-- check: PN3002 -->
```pino
fn show(n int) {
  println(n)
}

fn show(s string) {    # PN3002: Identifier 'show' is already declared in this scope.
  println(s)
}
```

<!-- check: PN3113 -->
```pino
fn greet(name string, punctuation string = "!") {   # PN3113: ... default parameter values are not supported.
  println(name + punctuation)
}
```

- **Why:** a name should tell you which function runs. (Standard-library members that take several argument shapes are fixed by the language.)

*Spec: FN-E12, FN-E13, D-173, D-176.*

### Arrow bodies are for lambdas, not named functions

- **You'd expect:** `fn one() int -> 1` as a short form (Kotlin, Rust-style).
- **Pino does:** `fn name(...) { ... }` always has braces. The `->` body form is for lambdas (`fn (n) -> n + 1`), and a lambda can't declare a return type.

<!-- check: PN2019 -->
```pino
fn one() int -> 1   # PN2019: Named functions and methods must have a '{ ... }' block body, not '-> expr'.
```

*Spec: FN-8, FN-E2, FN-E3, D-052, D-053.*

### The return type can be left out

- **You'd expect:** a Go or Java signature always names its return type.
- **Pino does:** you can leave it out and Pino works it out from your `return` statements. (Write it when you want to be clear, and where it's required: recursion with no base-case `return`.)

<!-- check: ok -->
```pino
fn add(a int, b int) {
  return a + b
}

println(add(1, 2))   # 3
```

*Spec: FN-27, FN-28, FN-E4, D-050.*

### `it` is an implicit lambda parameter

- **You'd expect:** you must write `fn (x) -> x > 2` every time.
- **Pino does:** if a call argument uses the undeclared name `it`, the argument *is* a lambda with one parameter called `it`. Inside a counted `for`, `it` is the loop index instead, so there you write lambdas out in full.

<!-- check: ok -->
```pino
println([1, 2, 3, 4]:filter(it > 2))   # [3, 4]
```

- **Why:** short callbacks stay short. If you declare your own `it`, Pino leaves it alone.

*Spec: FN-16, FN-17, LOOP-3, LOOP-17, D-002.*

### Tuples only exist to return several values

- **You'd expect:** `val t = (1, 2)` you can store, pass around and index.
- **Pino does:** a labeled tuple `@(x: 1, y: 2)` can appear only in a `return`, and the caller unpacks it right away with `val @(x, y) = call()`.

<!-- check: PN3309 -->
```pino
val t = @(x: 1, y: 2)   # PN3309: Tuple literals can only be used as a return value.
```

- **Why:** for anything you want to keep, a struct has names and types. A tuple just saves you a one-use struct.

*Spec: TUP-1 to TUP-5, TUP-E1, TUP-E2.*

---

## Order, modules and `main`

### Declarations can come in any order, `val` and `var` can't

- **You'd expect:** either everything is hoisted (JavaScript functions) or nothing is (C).
- **Pino does:** `fn`, `struct`, `enum`, `union` and `interface` can be used before the line where they're written. A top-level `val` or `var` follows strict top-to-bottom order, and a function that reads one declared *below* it is an error, even if you only call the function later.

<!-- check: PN3001 -->
```pino
fn show() {
  println(limit)   # PN3001: Identifier 'limit' is not declared.
}

val limit = 3
show()
```

- **Why:** it keeps start-up order simple and predictable: a constant exists from the line where it's written.

*Spec: FN-26, VAR-7, VAR-E8, D-165.*

### A file with `fn main` can only hold declarations

- **You'd expect:** any statements at the top level, next to `main` (Python's `if __name__ == ...`).
- **Pino does:** a file *without* `main` is a script, and its top-level statements run in order. A file *with* `fn main` is a program: the top level is for declarations and `val` constants only, and `main` is called for you.

<!-- check: PN3410 -->
```pino
println("hello")   # PN3410: Top-level statements are not allowed at the top level when a 'main' function is defined.

fn main() {
}
```

*Spec: MAIN-1 to MAIN-4, MAIN-E2.*

### `fn main` takes no parameters (for now)

- **You'd expect:** `main(args)` to get the command-line arguments.
- **Pino does:** `main` can't take parameters yet. Command-line arguments haven't been designed, and this is a temporary stop.

<!-- check: PN3412 -->
```pino
fn main(args []string) {   # PN3412: Function 'main' cannot take parameters.
}
```

*Spec: MAIN-E4, D-036 (the design is still open).*

### Modules: one file, `module Name`, and a `modules/` folder

- **You'd expect:** the import path is the file's path (Python, JavaScript), or the package is a folder (Go).
- **Pino does:** a module file starts with `module Name`, and lives in a `modules/` folder next to the file that imports it (or next to a parent folder's). `import Name` gives you `Name::thing`. `from Name import a b` brings names in directly. Only `pub` things are visible. A module can't define `main`.
- **File lookup:** for `import Greeter`, Pino looks for `modules/Greeter.pino` first, then `modules/greeter.pino`, in the importing file's folder and then in each parent folder.

<!-- check: ok group=greeter file=modules/Greeter.pino -->
```pino
module Greeter

pub fn hello(name string) string {
  return "Hello, $name!"
}

fn private_helper() {
}
```

<!-- check: ok group=greeter file=main.pino -->
```pino
import Greeter

fn main() {
  println(Greeter::hello("Ana"))   # Hello, Ana!
}
```

- **Why:** everything in a module is private unless you mark it `pub`, so a module decides exactly what it shows.

*Spec: MOD-1 to MOD-7, MOD-E4, MOD-E6, D-063.*

---

## Collections

### Sets and maps keep insertion order

- **You'd expect:** a random-looking order (Go maps, JavaScript sets of mixed keys), or hash order.
- **Pino does:** sets and maps iterate, print and list their keys in the order you put things in. Changing an existing key's value keeps its place. Removing a key and adding it again moves it to the end.

<!-- check: ok -->
```pino
var m = map[int, string] { 3 -> "c", 1 -> "a" }
m[2] = "b"
println(m:keys())   # [3, 1, 2]
m:remove(3)
m[3] = "c"
m[1] = "A"
println(m:keys())   # [1, 2, 3]

var s = set[int] { 3, 1 }
s:add(2)
for x in s {
  println(x)        # 3, then 1, then 2
}
```

- **Why:** the same program prints the same output on every run.

*Spec: MAP-9, MAPLIB-3, MAPLIB-4, SETLIB-7, LOOP-9, LOOP-10, LOOP-18, D-004, D-175.*

### Empty collections have no braces, and `{ 1, 2, 3 }` is not a set

- **You'd expect:** `[]` for an empty list, `{}` for an empty dict, and `{1, 2, 3}` for a set (Python, JavaScript).
- **Pino does:** `[]int`, `map[string, int]`, `set[int]`. These say the element type, with nothing after them. A set literal needs `set[T]`.

<!-- check: ok -->
```pino
val nums = []int
val ages = map[string, int]
val seen = set[int]
println(nums:len)   # 0
```

<!-- check: PN2017 -->
```pino
val s = { 1, 2, 3 }   # PN2017: Unexpected '{' in expression position.
```

*Spec: VEC-5, MAP-3, SET-2, SET-E1, D-102.*

### A bad index or a missing key stops the program

- **You'd expect:** `undefined` (JavaScript), a default (`dict.get`), or a quiet zero value (Go's missing key).
- **Pino does:** `v[i]` out of range **panics**. `m[key]` for a key that isn't there fails at run time too. Ask first with `in`:

<!-- check: ok -->
```pino
var v = [1, 2, 3]
var m = map[string, int]
if "a" in m {
  println(m["a"])
}
println(v[2])    # 3 (v[5] would panic)
```

Methods that might find nothing return an `Option` instead: `v:pop()` and `v:find(...)` give `Option::Some(x)` or `Option::None`, and `m:remove(k)` too.

<!-- check: ok -->
```pino
var v = [1]
match v:pop() {
  when Option::Some(x) { println(x) }   # 1
  when Option::None { println("empty") }
}
```

- **Why:** a wrong index is a bug, so it stops loudly; "might not be there" is something a function says in its type.

*Spec: VEC-E4, MAP-E1, MAP-7, VECLIB-8, VECLIB-9, VECLIB-16, MAPLIB-6, D-059, D-013.*

---

## Strings and runes

### `string + rune` works; `rune + rune` and `string + int` don't

- **You'd expect:** `+` joins anything with a string (JavaScript, Java), or fails with any type mix (Python, Go).
- **Pino does:** `string + rune` and `rune + string` give a string. Rune arithmetic stays rune arithmetic: `'A' + 1` is `'B'`, and `'c' - 'a'` is the `int` `2`. Two runes added together, or a string and a number, are errors. Use interpolation: `"$a$b"` or `"n=$n"`.

<!-- check: ok -->
```pino
val age = 24
val x = 10
val y = 20
println("age $age, sum $(x + y), cost \$5")   # age 24, sum 30, cost $5
println("ab" + 'c')   # abc
println('a' + "bc")   # abc
println('A' + 1)      # B
println('c' - 'a')    # 2
```

<!-- check: PN3203 -->
```pino
println('a' + 'b')   # PN3203: Cannot add two runes with '+'; use "$a$b" or str(a) + str(b) ...
```

<!-- check: PN3203 -->
```pino
println("n=" + 1)    # PN3203: Cannot use '+' with a 'string' and a non-string ...
```

- **Why:** each operator has one result type. `'a' + 'b'` could mean a number, a rune or a string, so it's not allowed. Interpolation (`$name`, `$(expr)`) is the way to build text from values.

*Spec: RUNE-2, RUNE-3, RUNE-E2, STR-2, STR-3, STR-11, STR-12, STR-E5, D-011, D-025.*

---

## Absence, errors and `match`

### There is no `null`, and no exceptions

- **You'd expect:** `null`, `None` or `nil` somewhere, and `try`/`catch`.
- **Pino does:** a value that might be missing is an `Option[T]` (`Option::Some(x)` or `Option::None`). An operation that might fail is a `Result[T, E]` (`Result::Success(x)` or `Result::Failure(e)`). A bug you can't recover from is a `panic`. Use `expr?` to pass a failure up to your caller, and `expr or { ... }` to recover on the spot.

*Spec: TYPE-12, ERR-1, ERR-3, ERR-7 to ERR-14, D-013.*

### `Option::None` can't be a `val`

- **You'd expect:** `val x = None` is a fine way to say "nothing yet".
- **Pino does:** it's an error. A `val` never changes, so it would be empty forever. Use a `var`.

<!-- check: PN3206 -->
```pino
val x Option[int] = Option::None   # PN3206: Cannot initialize immutable 'val' 'x' with 'Option::None'
```

<!-- check: ok -->
```pino
var x Option[int] = Option::None
x = Option::Some(3)
```

*Spec: VAR-E7, D-160.*

### A `Result` with no value is written `Result::Success`

- **You'd expect:** `Ok(())` (Rust), `return nil` (Go), or `Result::Success()`.
- **Pino does:** for a `Result[void, E]` (a function that can only fail), success is the bare `Result::Success`, with no parentheses, just like `Option::None`.

<!-- check: ok -->
```pino
fn check(n int) Result[void, string] {
  if n < 0 {
    return Result::Failure("negative")
  }
  return Result::Success
}

match check(1) {
  when Result::Success { println("ok") }
  when Result::Failure(e) { println(e) }
}
```

<!-- check: PN3313 -->
```pino
fn check(n int) Result[void, string] {
  return Result::Success()   # PN3313: ... write `Result::Success`.
}
```

*Spec: ERR-23, ERR-E8, TYPE-17, D-162.*

### In `match`, `else` is the only catch-all

- **You'd expect:** `_` (Rust, Python, Scala), `default` (C, Java, Go), or a bare name that binds the value.
- **Pino does:** the catch-all is `else`. A bare name in a `when` is an error: it neither compares with a variable nor binds one. A `match` over an enum or a union with every case covered needs no `else`. Over anything else, it does.

<!-- check: ok -->
```pino
fn describe(n int) string {
  match n {
    when 1 { return "one" }
    when 2, 3 { return "a few" }
    else { return "many" }
  }
}

println(describe(3))   # a few
```

<!-- check: PN3314 -->
```pino
fn describe(n int) {
  match n {
    when 1 { println("one") }
    when other { println("many") }   # PN3314: Cannot use bare identifier 'other' as a 'when' pattern; use 'else' ...
    else { println("none") }
  }
}
```

- **Why:** `when other` looks like it binds `other`, and that's a trap. One word for "everything else" removes the guesswork.

*Spec: MATCH-4, MATCH-5, MATCH-11, MATCH-E5, MATCH-E8, D-031, D-034.*

---

See also: [Values, not objects](values-and-mutation.md) for copies, `val`, `val mut`, `var` and `mut`.
