# Official Pino Lang Reference Manual 🌲

Pino is a modern, clean, and aesthetically premium programming language designed under the philosophy that writing code should be a pleasant and joyful experience. It fuses the best features of languages like **Vlang**, **Go**, **Kotlin**, and **Ruby**, providing static type inference, structural composition, and a set of high-performance ergonomic tools.

This document serves as the definitive reference guide for the syntax, type system, built-in functions, and instance methods available in Pino.

---

## Table of Contents
1. [Syntax Guidelines (Snake Case, Optional Commas)](#1-syntax-guidelines)
2. [Variables and Constants](#2-variables-and-constants)
3. [Type System and Data Types](#3-type-system-and-data-types)
   * [Primitives](#primitive-types)
   * [Ergonomic Rune Arithmetic](#ergonomic-rune-arithmetic)
   * [Strings and Interpolation](#strings-and-interpolation)
4. [Collections and Associative Structures](#4-collections-and-associative-structures)
   * [Vectors (Arrays)](#vectors-arrays)
   * [Maps (Dictionaries)](#maps-dictionaries)
5. [Control Flow Structures](#5-control-flow-structures)
   * [Conditionals (`if-else` as Statement and Expression)](#conditionals-if-else)
   * [The Universal Loop (`for`)](#the-universal-loop-for)
   * [Pattern Matching (`match-when`)](#pattern-matching-match-when)
6. [Modules and Imports](#6-modules-and-imports)
7. [Structured Programming and OOP](#7-structured-programming-and-oop)
   * [Structs (Fields, Methods, and Self-Referential Context)](#structs)
   * [Composition and Embedding](#composition-and-embedding)
   * [Structural Interfaces ("Duck Typing")](#structural-interfaces-duck-typing)
   * [Enums](#enums)
   * [Unions (Sum Types / Tagged Unions)](#unions-sum-types--tagged-unions)
8. [Error Handling (Panic, ? operator, and or blocks)](#8-error-handling)
9. [Global Built-in Functions](#9-global-built-in-functions)
10. [Properties and Instance Methods](#10-properties-and-instance-methods)
   * [String Methods](#string-methods)
   * [Regex Methods](#regex-methods)
   * [Vector Methods](#vector-methods)
   * [Map Methods](#map-methods)
11. [Execution Engines](#11-execution-engines)

---

## 1. Syntax Guidelines

Pino values clean, consistent formatting and provides ergonomic flexibility:

*   **Naming Convention**: Pino strictly adopts `snake_case` for all variable declarations, properties, functions, and method names. User-defined types such as structs, interfaces, and enums follow `PascalCase`.
*   **Optional Commas**: In almost all collection structures, lists, and initialization blocks, commas are completely optional when elements are separated by whitespace or newlines. This applies to:
    *   Vectors/Arrays (e.g., `[1 2 3]` or separated by newlines)
    *   Maps (e.g., `map[string, int] { "x": 10 "y": 20 }`)
    *   Struct initialization (e.g., `Player { name: hero_name hp: 80 }`)
    *   Imports (e.g., `from Utils import calculate_distance logger`)
    *   Match `when` branch cases (e.g., `when "start" "run" { ... }`)
    *   Function/method arguments and parameters.

---

## 2. Variables and Constants

Pino strictly distinguishes between mutable and immutable variables using the `var` and `val` keywords:

*   **`val` (Immutable Value)**: Declares a local constant whose value is evaluated once at runtime and cannot be reassigned. This is preferred by default to encourage safety and predictable code.
*   **`var` (Mutable Variable)**: Declares a memory cell that can be updated with new values of the same type during program execution.

```pino
val name = "Shawn Lee" # Immutable. Any subsequent reassignment will cause an error.
# name = "Vincent"      # Compiler/Checker ERROR

var attempts = 0       # Mutable.
attempts = attempts + 1 # Valid operation.
```

---

## 3. Type System and Data Types

Pino features a strong type system with automated type inference. The compiler and static analyzer (`Checker`) ensure type consistency before execution.

### Primitive Types

| Type | Description | Example / Representation |
| :--- | :--- | :--- |
| **`bool`** | Logical truth value | `true`, `false` |
| **`int`** | 64-bit signed integer | `42`, `-10`, `1_000_000` (supports underscores) |
| **`float`** | 64-bit double-precision floating-point number | `3.14159`, `-0.005` |
| **`rune`** | 32-bit Unicode code point | `'a'`, `'🌲'`, `'\n'` (delimited by single quotes) |
| **`string`** | Immutable UTF-8 encoded text sequence | `"Pino Lang"` (delimited by double quotes) |
| **`null`** | Represents the absence of value or null reference | `null` |

---

### Ergonomic Rune Arithmetic

Runes in Pino are not simply integers in disguise; they represent individual Unicode characters and have a special ergonomic behavior designed to make character and text manipulation easy:

1.  **Direct Concatenation**: If you add two runes, the result is automatically promoted to a `string`.
    ```pino
    val hello = 'H' + 'i'
    println(hello) # Prints: "Hi" (type string)
    ```
2.  **Numerical Shift**: Adding or subtracting an integer to/from a rune returns another rune shifted in the Unicode table.
    ```pino
    val next = 'A' + 1
    println(next) # Prints: 'B' (type rune)

    val prev = 'z' - 2
    println(prev) # Prints: 'x' (type rune)
    ```
3.  **Distance between Runes**: Subtracting one rune from another returns the numeric distance (`int`) between their code points.
    ```pino
    val dist = 'c' - 'a'
    println(dist) # Prints: 2 (type int)
    ```
4.  **Comparisons**: Runes can be directly compared using relational operators based on their numeric Unicode value (`==`, `!=`, `<`, `<=`, `>`, `>=`).

---

### Strings and Interpolation

Strings in Pino are immutable and support advanced interpolation of variables and execution expressions using the `$` symbol:

*   **Variable Interpolation**: Directly insert a variable's value by prefixing it with `$`.
*   **Expression Interpolation**: Insert the result of evaluating any complex expression by wrapping it in `$(...)`.

```pino
val age = 24
val msg = "Hello, my age is $age" # "Hello, my age is 24"

val x = 10
val y = 20
val result = "The sum is: $(x + y)" # "The sum is: 30"
```

---

## 4. Collections and Associative Structures

### Vectors (Arrays)

Vectors are dynamic, homogeneous arrays (all elements must be of the same type). They are initialized using square brackets `[...]` or by specifying a type for empty and dynamic collections.

#### Initialization Syntax:
1. **Literal initialization**: Declare items inside `[...]` with implicit type inference.
2. **Empty vector initialization**: Specify the type `[]type` without curly braces `{}`.
3. **Dynamic initialization constructor**: Initialize with a given length and a generator expression using the `[]type { len: <int>, init: <expression> }` syntax. The variable `it` is implicitly injected into the `init` expression, representing the current index.

```pino
# 1. Implicit type inference for []int
val numbers = [1, 2, 3, 4] 

# 2. Empty vector of strings (no curly braces needed, just type annotation)
var names = []string

# 3. Dynamic initialization of 5 integers, generating [0, 2, 4, 6, 8]
val evens = []int { len: 5, init: it * 2 }

# Index access and assignment (0-indexed)
val first = numbers[0]
names:push("Vincent")
```

---

### Maps (Dictionaries)

Maps are associative collections linking unique keys to values. Keys cannot be null. They are initialized using the `map[KeyType, ValueType] { ... }` syntax.

```pino
# Declaration and initialization of a score map
var scores = map[string, int] {
  "Shawn": 100,
  "Re-l": 95
}

# Index access
val shawn_score = scores["Shawn"] # 100
scores["Vincent"] = 80           # Inserts or updates a key
```

---

## 5. Control Flow Structures

### Conditionals (`if-else`)

Conditionals in Pino do not require parentheses around the condition, but curly braces `{}` are mandatory for code blocks.

#### As a Statement
```pino
val score = 85
if score >= 90 {
  println("Excellent")
} else if score >= 70 {
  println("Pass")
} else {
  println("Fail")
}
```

#### As an Expression
Pino allows using `if` as an evaluable expression to return conditional values in a single line (similar to a ternary operator), using the `if [condition] then [expr_a] else [expr_b]` syntax.
```pino
val level = 5
var difficulty = if level > 10 then "Hard" else "Easy"
println(difficulty) # Prints "Easy"
```

---

### The Universal Loop (`for`)

Pino consolidates all loop requirements under the `for` keyword, offering 4 clear syntactic variants:

#### 1. Infinite Loop
Runs until a `break` statement is encountered or the containing function returns.
```pino
var count = 0
for {
  count = count + 1
  if count >= 5 {
    break
  }
}
```

#### 2. Fixed Range Loop (N iterations)
Automatically iterates from `0` to `N - 1`. Pino injects the implicit variable `it` into the block as the current index.
```pino
for 3 {
  println("Implicit iteration: $it") # Prints 0, 1, 2
}
```

#### 3. Iterator Loop over Range or Collection
Iterates over a collection (list, numeric range, or character string), assigning the current element to a declared variable.
```pino
# Iterates from 0 to 4
for i in 5 {
  println(i)
}

# Iterates over elements of a vector
val items = ["coffee", "pino", "code"]
for item in items {
  println("Item: $item")
}
```
> [!NOTE]
> **String Iteration**: When iterating over a string, Pino deconstructs the text character by character, returning individual Unicode code points (`rune`).
> ```pino
> for char in "🌲Pino" {
>   println(type(char)) # Prints "rune" at each step
> }
> ```

#### 4. Index and Value Iteration
Allows extracting both the current iteration index and the collection element simultaneously.
```pino
val fruits = ["apple", "pear", "grape"]
for index, fruit in fruits {
  println("Fruit at index $index is $fruit")
}
```

---

### Pattern Matching (`match-when`)

A clean structure to evaluate multiple branches based on an expression's value. It supports multiple values per branch separated optionally by commas (commas are optional here too, e.g. `when "start" "run"`). The `else` branch is optional, and it is not mandatory to cover all cases exhaustively.

```pino
val command = "start"
match command {
  when "start", "run" {
    println("Running program...")
  }
  when "stop" {
    println("Stopping...")
  }
  else {
    println("Unknown command")
  }
}
```

---

### Program Entry Point (`main()`) & Program Mode

Pino programs can be run in two modes: **Script Mode** and **Program Mode**.

1. **Script Mode** (No `main` function): Top-level statements are evaluated sequentially. This is useful for short scripts, simple tasks, and interactive development.
2. **Program Mode** (A `fn main()` is defined): If the compiler detects a `main` function in the root file:
   * **Automatic Entry Point**: The `main()` function is automatically called at the end of compilation/loading. You do not need to call `main()` explicitly at the bottom of the file (and doing so is forbidden to avoid duplicate executions).
   * **Strict Top-level Rules**: To enforce structured architectures, top-level statements are restricted *only* to declarations (structs, interfaces, functions, enums, imports). Any top-level statements with side-effects (loops, conditionals, assignments, or loose expressions) will throw a **Type Check Error**.
   * **No Mutable Globals**: Declaring mutable variables with `var` at the top level is forbidden in Program Mode. Only immutable constants (`val`) are allowed as global definitions to avoid dangerous shared mutable state.

---

## 6. Modules and Imports

Pino supports a clean module system allowing you to import specific elements from other source files using the `from` keyword.

```pino
# Imports only the specified classes and functions into the current scope
from Entities import Player, Enemy
from Utils import calculate_distance, logger

val p = Player { name: "Shawn" }
logger("Player created")
```

To access namespaced constants or statics of imported scopes, use the scope resolution operator `::`:
```pino
val color = Color::Green
```

> [!IMPORTANT]
> **Module main Restriction**: Imported modules are checked strictly and are **not** allowed to define a `main()` function. Declaring a `main` function in an imported module causes a Type Check Error, keeping library scopes clean.

---

## 7. Structured Programming and OOP

Pino utilizes a data-oriented paradigm based on structures (`struct`), composition (embedding), and structural interfaces.

### Structs

A `struct` encapsulates state (fields) and behavior (methods).

*   **Instantiation**: Structs are initialized by explicitly naming their fields.
*   **Self-reference**: Instance methods can directly access declared properties of their struct in their body, or use the reserved identifiers `self` and `this` to explicitly refer to themselves.

```pino
struct Vector2 {
  x int
  y int

  # Instance method
  fn print_coords() {
    # Direct access to instance variables
    println("X: $x, Y: $y")
  }

  # Instance method with explicit self-reference
  fn distance_to(other Vector2) int {
    val dx = self:x - other:x
    val dy = this:y - other:y
    return dx * dx + dy * dy
  }
}

# Initialization
val pos = Vector2 { x: 10, y: 20 }
pos:print_coords()
```

---

### Composition and Embedding

Pino does not have traditional class inheritance. Instead, it promotes structural composition. A `struct` can anonymously embed another, automatically inheriting all its fields and methods.

```pino
struct Character {
  name string
  hp   int

  fn take_damage(dmg int) {
    self:hp -= dmg
    println("$name took $dmg damage.")
  }
}

struct Hero {
  Character # Structural embedding
  mana int
}

# Initializing the composite struct
val warrior = Hero {
  name: "Guts",
  hp: 150,
  mana: 20
}

# Invoking the inherited method
warrior:take_damage(30) # Prints: "Guts took 30 damage."
```

---

### Structural Interfaces ("Duck Typing")

Interfaces in Pino define behavior signatures that act as compile-time type constraints. Pino uses **implicit structural typing**: there is no `implements` keyword. Any `struct` implementing the functions specified in an interface automatically satisfies it.

```pino
interface SoundMaker {
  fn make_sound() string
}

struct Dog {
  fn make_sound() string => "Woof"
}

struct Cat {
  fn make_sound() string => "Meow"
}

# Function accepting any type that satisfies SoundMaker
fn output_sound(s SoundMaker) {
  println("Sound: " + s:make_sound())
}

output_sound(Dog{}) # Dog satisfies SoundMaker
output_sound(Cat{}) # Cat satisfies SoundMaker
```

---

### Enums

Enums declare a closed set of strongly-typed named constants.

```pino
enum Direction {
  North
  South
  East
  West
}

val heading = Direction::North
```

---

### Unions (Sum Types / Tagged Unions)

Unions in Pino are **tagged unions** (also known as *sum types* or *algebraic data types*). Unlike unsafe C-style unions, Pino unions are completely type-safe and represent a value that can be one of several different variants, optionally carrying payloads (associated data) of varying types.

They are defined using the `union` keyword and are consumed safely via **Pattern Matching** (`match-when`).

#### Syntax and Declaration:
```pino
pub union Result {
    Success([]Token)
    Error(string)
    Pending # Variant with no payload
}
```

#### Instantiation:
You instantiate a union variant using the scope resolution operator `::`:
```pino
val result_ok = Result::Success(token_list)
val result_err = Result::Error("Unexpected character")
val result_pending = Result::Pending
```

#### Safe Destruction with `match`:
To access the payload data within a union, you must use a `match` expression or statement. The compiler/checker helps guarantee safety by matching against specific variant tags. If you do not want to list every variant explicitly, you can use an `else` block as a wildcard/fallback case:
```pino
match result_ok {
    when Result::Success(tokens) {
        println("Successfully parsed $(tokens:len) tokens.")
    }
    else {
        println("Something went wrong or is still pending.")
    }
}
```

#### Generic Unions:
You can declare generic unions using either the `@generic` decorator prefix or inline brackets:
```pino
@generic[T, E]
union Result {
    Success(T)
    Failure(E)
}

# OR inline:
union Option[T] {
    Some(T)
    None
}
```

#### Monomorphization & Type Inference:
The static checker automatically monomorphizes generic unions at compile/type-check time based on actual payload arguments or context-driven return type signatures:
```pino
# Inferred as Option_int at compile time
val opt = Option::Some(42)

# Matching on generic variants
match opt {
    when Option::Some(val) {
        println("Got some value: $val")
    }
    when Option::None {
        println("Nothing")
    }
}
```

## 8. Error Handling

Pino provides a modern, type-safe, and highly robust error-handling model inspired by Rust and Zig. Instead of throwing arbitrary unchecked runtime exceptions, Pino encourages handling expected failures explicitly through monadic sum types (`Result` and `Option`), combined with clean compiler-enforced recovery structures.

### The `panic` Mechanism

For truly **unrecoverable errors** (e.g., severe logic bugs, missing crucial assets, or corrupt configurations), Pino offers the global `panic` built-in function:

*   **Behavior**: When invoked, `panic` immediately halts execution, prints a clean red stack backtrace containing all calling stack frames to `stderr`, and exits the process with a non-zero code (`101`).
*   **Signature**: `panic(message string) any` (returns `any` to satisfy any type context where it is called).

```pino
fn load_config(path string) {
    if path == "" {
        panic("Database configuration path cannot be empty!")
    }
}
```

---

### The Bubble Operator (`?`)

The suffix operator `?` provides a clean way to bubble up errors through call stacks without manual nesting:

*   **Applicability**: Can only be applied to expressions evaluating to a generic `Result[T, E]` or `Option[T]` union variant.
*   **Rules & Constraints (Enforced by the `Checker`)**:
    1.  **Function Scope**: `?` can only be used inside a function body. Using `?` at the global script level is a compile-time error.
    2.  **Return Type Annotation**: The enclosing function must explicitly declare a `Result` or `Option` return type.
    3.  **Error Compatibility**: The error type `E1` of the bubbled expression must be statically compatible with the declared error type `E2` of the enclosing function. Attempting to bubble a `string` error in a function returning `Result[int, int]` is a compile-time type error.
*   **Mechanics**:
    *   If the expression evaluates to `Success(value)` or `Some(value)`, `?` extracts and evaluates to the inner value `value` (type `T`).
    *   If it is a `Failure(err)` or `None`, it immediately returns the failure/none variant from the enclosing function.

```pino
fn divide(a int, b int) Result[int, string] {
    if b == 0 {
        return Result::Failure("Zero division")
    }
    return Result::Success(a / b)
}

# The enclosing function declares a compatible Result return type
fn compute(a int, b int) Result[int, string] {
    val num = divide(a, b)? # Extracts 'int' success value or bubbles Failure(string) early
    return Result::Success(num * 10)
}
```

---

### Suffix Recovery Blocks (`or`)

The `or` keyword evaluates an expression and, if it fails, redirects execution to a local recovery block:

*   **Injecting the Error**: The recovery block is executed in a child scope where a local variable named `err` is automatically declared. For a `Result[T, E]`, `err` holds the failure payload `E`. For an `Option[T]`, `err` is initialized to the string `"None"`.
*   **Yielding Fallbacks**: Inside the `or` block, you must either diverge (call `return` or `panic`) or yield a fallback value using the `yield` keyword. The type of the yielded expression must match the expected success type `T` of the outer expression.

```pino
val result = divide(10, 0) or {
    println("Captured error: $err") # err is "Zero division"
    yield -1 # Fallback value assigned to 'result'
}
```

---

### Managing Error Type Collisions (Tactics)

When calling functions that return different error types, you cannot bubble them all directly using `?` if they conflict with the enclosing function's declared error channel. 

#### Tactic 1: Manual Mapping with `or`
Use the `or` block to intercept the foreign error, map/format it into the enclosing function's error type, and return a manual `Failure`:

```pino
fn get_db_status() Result[string, int] {
    return Result::Failure(500) # Returns int error
}

fn initialize() Result[string, string] { # Expects string error
    # We cannot use get_db_status()? because 'int' is incompatible with 'string'
    val status = get_db_status() or {
        return Result::Failure("Database error occurred with status: " + err:to_string())
    }
    return Result::Success(status)
}
```

#### Tactic 2: Unified Module Error Unions
Define a unified module-level `union` that represents all possible failures, and use it as the error channel for your module's functions:

```pino
union AppError {
    DatabaseError(int)
    NetworkError(string)
}

fn query_db() Result[string, AppError] { ... }
fn fetch_network() Result[string, AppError] { ... }

# Since both return the same error channel, they can be bubbled together seamlessly!
fn execute() Result[string, AppError] {
    val db = query_db()?
    val net = fetch_network()?
    return Result::Success("Success: $db and $net")
}

fn handle_app_error(app_error AppError) {
  match app_error {
    case AppError::DatabaseError(code) {
      println("Database error: $code")
    }
    case AppError::NetworkError(msg) {
      println("Network error: $msg")
    }
  }
}

fn main {
  val res = execute()
  match res {
    case Result::Success(message) {
      println("Success: $message")
    }
    case Result::Failure(app_error) {
      handle_app_error(app_error)
    }
  }
}
```

---

### The `main()` Entry Point Failure Handler

In Program Mode, if `fn main()` is declared to return a `Result` or `Option`, any bubbled or explicitly returned failure variant is caught by the Pino runtime at the top level:

*   It prints the failure message directly to `stderr` in red.
*   It terminates execution immediately, returning exit code `1` to the operating system.

This enables you to write clean, linear CLI tools that bubble errors all the way up to `main` for clean terminations:

```pino
fn main() Result[int, string] {
    val tokens = lex(program)? # If lex fails, CLI prints error and exits with 1
    val parsed = parse(tokens)?
    return Result::Success(0)
}
```

---

## 9. Global Built-in Functions

These functions are globally available in any Pino source file without requiring any imports:

### `panic(message)`
Halts program execution immediately, displaying a clean stack backtrace of call frames, and exits the process.
*   **Arguments**: A `string` message describing the panic reason.
*   **Return**: `any` (special type that satisfies any surrounding type check constraints).
*   **Example**: `panic("Critical database failure!")`

### `println(args...)`
Prints one or more expressions to the standard console, separated by spaces and followed by a newline.
*   **Arguments**: Zero or more expressions of any type.
*   **Return**: `null`
*   **Example**: `println("The answer is:", 42)`

### `readline(prompt?)`
Reads a line of text from standard input. Optionally prints a prompt to the console before blocking execution waiting for input.
*   **Arguments**: An optional string (`prompt`).
*   **Return**: `string`
*   **Example**: `val name = readline("Enter your name: ")`

### `int(val)`
Converts a number or a string representation to a 64-bit integer (`int`).
*   **Arguments**: A value of type `string`, `float`, `int`, or `rune`.
*   **Return**: `int`
*   **Example**: `int("123")` returns `123`. `int(3.99)` returns `3`.

### `float(val)`
Converts an integer or a numeric string to a 64-bit decimal number (`float`).
*   **Arguments**: A value of type `string`, `int`, or `float`.
*   **Return**: `float`
*   **Example**: `float("3.14")` returns `3.14`. `float(42)` returns `42.0`.

### `rand(limit?)`
Generates pseudo-random numbers.
*   **Arguments**:
    *   If called without arguments: Returns a random `float` in the range `[0.0, 1.0)`.
    *   If a limit integer is provided (`limit`): Returns a random `int` in the range `[0, limit)`.
*   **Return**: `float` or `int`
*   **Example**: `val index = rand(10)` (Returns an integer from 0 to 9).

### `time()`
Returns the number of milliseconds elapsed since January 1, 1970, 00:00:00 UTC (Unix Epoch).
*   **Arguments**: None.
*   **Return**: `int`
*   **Example**: `val start = time()`

### `sleep(ms)`
Pauses execution of the current thread for a specified number of milliseconds.
*   **Arguments**: An integer `int` indicating the sleep duration.
*   **Return**: `null`
*   **Example**: `sleep(1000)` # Sleep for 1 second

### `type(val)`
Returns the name of the dynamic type of the provided value at runtime.
*   **Arguments**: Any expression.
*   **Return**: `string` (e.g., `"null"`, `"bool"`, `"rune"`, `"int"`, `"float"`, `"string"`, `"vector"`, `"map"`, `"struct"`, `"function"`, `"enum"`).
*   **Example**: `type('🌲')` returns `"rune"`.

### `rune(val)`
Converts or extracts an individual character (Unicode code point) from the given value.
*   **Arguments**:
    *   If it is an `int` or `float`, it interprets the value as a code point.
    *   If it is a `string`, it extracts the first character (supporting full surrogate pairs).
*   **Return**: `rune`
*   **Example**: `rune("🌲Pino")` returns `'🌲'`.

### `str(val)`
Returns the formatted textual representation of any object.
*   **Arguments**: Any expression.
*   **Return**: `string`
*   **Example**: `str([1, 2, 3])` returns `"[1, 2, 3]"`.

### `clear()`
Clears the standard terminal or console.
*   **Arguments**: None.
*   **Return**: `null`
*   **Example**: `clear()`

### `regex(pattern)`
Compiles a regular expression pattern for matching operations.
*   **Arguments**: A `string` containing the regex pattern.
*   **Return**: `regex`
*   **Example**: `val r = regex("[0-9]+")`

---

## 10. Properties and Instance Methods

Instance methods and properties are invoked using the two-dot member call operator (`:`).

### String Methods

Strings have the following useful methods and properties:

#### `len` / `length` (Property)
Returns the character length (count of `rune`) of the string, correctly handling extended Unicode characters.
*   **Return**: `int`
*   **Example**: `"🌲":len` returns `1`.

#### `lower()`
Returns a new string with all alphabetic characters converted to lowercase.
*   **Return**: `string`
*   **Example**: `"PINO":lower()` returns `"pino"`.

#### `upper()`
Returns a new string with all alphabetic characters converted to uppercase.
*   **Return**: `string`
*   **Example**: `"pino":upper()` returns `"PINO"`.

#### `trim()`
Returns a new string with all leading and trailing whitespace characters removed.
*   **Return**: `string`
*   **Example**: `"  hello  ":trim()` returns `"hello"`.

#### `contains(sub string)`
Checks if the specified substring is contained within the main string.
*   **Return**: `bool`
*   **Example**: `"Pino":contains("in")` returns `true`.

#### `split(sep string)`
Splits the string into a vector of strings using the provided separator.
*   **Return**: `[]string`
*   **Example**: `"a,b,c":split(",")` returns `["a", "b", "c"]`.

#### `replace(old string, new string)`
Replaces all occurrences of the `old` substring with the `new` string.
*   **Return**: `string`
*   **Example**: `"hello world":replace("world", "Pino")` returns `"hello Pino"`.

#### `substring(start int, len int)`
Extracts a substring starting at `start` index with the specified length `len`.
*   **Return**: `string`
*   **Example**: `"hello world":substring(6, 5)` returns `"world"`.

#### `starts_with(prefix string)`
Checks if the string begins with the specified `prefix` string.
*   **Return**: `bool`
*   **Example**: `"pino":starts_with("pi")` returns `true`.

#### `ends_with(suffix string)`
Checks if the string ends with the specified `suffix` string.
*   **Return**: `bool`
*   **Example**: `"pino":ends_with("no")` returns `true`.

#### `index_of(sub string)`
Returns the zero-based index of the first occurrence of the `sub` substring, or `-1` if not found.
*   **Return**: `int`
*   **Example**: `"pino":index_of("in")` returns `1`.

#### `trim_start()`
Returns a new string with all leading whitespace characters removed.
*   **Return**: `string`
*   **Example**: `"  pino":trim_start()` returns `"pino"`.

#### `trim_end()`
Returns a new string with all trailing whitespace characters removed.
*   **Return**: `string`
*   **Example**: `"pino  ":trim_end()` returns `"pino"`.

---

### Regex Methods

Regular expressions of type `regex` have the following methods and properties:

#### `pattern` (Property)
Returns the original compiled regex pattern string.
*   **Return**: `string`
*   **Example**: `regex("[0-9]+"):pattern` returns `"[0-9]+"`.

#### `has_match(text string)`
Checks if the regular expression pattern matches anywhere in the specified `text`.
*   **Return**: `bool`
*   **Example**: `regex("[0-9]+"):has_match("abc123def")` returns `true`.

#### `match_prefix(text string)`
Attempts to match the pattern anchored specifically to the beginning of the `text` string (equivalent to a `^` anchor).
*   **Return**: `string` (the matched substring, or `""` if not found).
*   **Example**: `regex("[0-9]+"):match_prefix("123abc456")` returns `"123"`.

#### `find(text string)`
Finds the first occurrence of the regular expression pattern anywhere in the specified `text`.
*   **Return**: `string` (the matched substring, or `""` if not found).
*   **Example**: `regex("[0-9]+"):find("abc456def")` returns `"456"`.

#### `find_all(text string)`
Finds all non-overlapping matches of the pattern in the specified `text` string.
*   **Return**: `[]string`
*   **Example**: `regex("[0-9]+"):find_all("12abc34def56")` returns `["12", "34", "56"]`.

#### `replace(text string, repl string)`
Replaces all matches of the regex pattern inside the `text` string with the replacement string `repl`.
*   **Return**: `string`
*   **Example**: `regex("[0-9]+"):replace("a12b34c", "X")` returns `"aXbXc"`.

---

### Vector Methods

Mutable vectors support chained calls and higher-order operations:

#### `len` / `length` (Property)
Returns the current number of elements stored in the vector.
*   **Return**: `int`
*   **Example**: `[10, 20]:len` returns `2`.

#### `push(item)` or `add(item)`
Appends an element to the end of the vector, modifying the original instance. Returns the modified vector to allow chaining.
*   **Return**: `vector` (mutated)
*   **Example**: `var v = []int {}; v:push(5):push(10)` (Leaves vector as `[5, 10]`).

#### `pop()`
Removes and returns the last element of the vector. If the vector is empty, returns `null`.
*   **Return**: `any`
*   **Example**: `val x = [1, 2]:pop()` (Leaves vector as `[1]` and `x` is `2`).

#### `each(fn)`
Executes the provided callback function on each element of the vector sequentially.
*   **Callback Signature**: Can be `fn(item)` or `fn(item, index)`.
*   **Return**: `null`
*   **Example**:
    ```pino
    [10, 20]:each(fn(val int, idx int) {
      println("Index $idx: $val")
    })
    ```

#### `map(fn)`
Transforms the vector by projecting each element to a new value through the specified function.
*   **Callback Signature**: Can be `fn(item) TargetType` or `fn(item, index) TargetType`.
*   **Return**: A new `vector` containing the transformed elements.
*   **Example**:
    ```pino
    val doubled = [1, 2, 3]:map(fn(v int) => v * 2) # [2, 4, 6]
    ```

#### `filter(fn)`
Returns a new vector containing only those elements that satisfy the truth condition (predicate) of the function.
*   **Callback Signature**: Can be `fn(item) bool` or `fn(item, index) bool`.
*   **Return**: A new `vector`.
*   **Example**:
    ```pino
    val evens = [1, 2, 3, 4]:filter(fn(x int) => x % 2 == 0) # [2, 4]
    ```

#### `find(fn)`
Finds the first element in the vector that satisfies the predicate and returns it. If none meet the condition, returns `null`.
*   **Callback Signature**: Can be `fn(item) bool` or `fn(item, index) bool`.
*   **Return**: `any` or `null`
*   **Example**:
    ```pino
    val num = [1, 3, 4, 5]:find(fn(x int) => x % 2 == 0) # 4
    ```

#### `find_index(fn)`
Finds the index of the first element in the vector that satisfies the predicate. If none is found, returns `-1`.
*   **Callback Signature**: Can be `fn(item) bool` or `fn(item, index) bool`.
*   **Return**: `int`
*   **Example**:
    ```pino
    val idx = [1, 3, 4, 5]:find_index(fn(x int) => x % 2 == 0) # 2
    ```

#### `any(fn)`
Checks if *at least one* element in the vector satisfies the predicate condition.
*   **Callback Signature**: `fn(item) bool` or `fn(item, index) bool`.
*   **Return**: `bool`
*   **Example**:
    ```pino
    val ok = [1, 3, 5]:any(fn(x int) => x > 2) # true
    ```

#### `all(fn)`
Checks if *all* elements in the vector satisfy the predicate condition.
*   **Callback Signature**: `fn(item) bool` or `fn(item, index) bool`.
*   **Return**: `bool`
*   **Example**:
    ```pino
    val ok = [2, 4, 6]:all(fn(x int) => x % 2 == 0) # true
    ```

---

### Map Methods

Maps have practical methods for querying and mutation:

#### `len` / `length` (Property)
Returns the total number of key-value pairs contained in the map.
*   **Return**: `int`
*   **Example**: `scores:len`

#### `keys()`
Returns a new vector containing all keys currently registered in the map.
*   **Return**: `[]KeyType`
*   **Example**: `scores:keys()` returns a vector of strings (e.g., `["Shawn", "Re-l"]`).

#### `values()`
Returns a new vector with all values stored in the map, in the order of their associated keys.
*   **Return**: `[]ValueType`
*   **Example**: `scores:values()` returns a vector of integers (e.g., `[100, 95]`).

#### `remove(key)`
Removes the specified key from the map and returns the value it had associated. If the key is not found, returns `null`.
*   **Return**: `any` or `null`
*   **Example**: `val val_removed = scores:remove("Re-l")` (Removes the key and returns `95`).

---

## 11. Execution Engines

Pino provides two execution engines that operate in parallel to offer advanced compatibility and performance engineering:

1.  **AST Interpreter (Tree-Walk)**:
    This is the default main execution engine. It evaluates nodes of the Abstract Syntax Tree (AST) directly. It guarantees 100% compliance with the high-level features of the language, including advanced structuring, import modules, and late binding of interfaces.
    ```bash
    pino run main.pino
    ```

2.  **Compiler and Virtual Machine (Bytecode VM - Experimental)**:
    An optimized engine designed for speed. It compiles the AST to a sequence of linear bytecode instructions (`OperationCode`) and executes them in a highly optimized stack-based virtual machine. Currently, it supports complex mathematical operations, function calls, and control loops optimized directly at the `Checker` level.
    ```bash
    pino run main.pino --vm
    ```
