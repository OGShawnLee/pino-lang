# Pino Lang 🌲

A modern, simple, and highly aesthetic programming language designed to make writing code a joyful experience. Pino bridges the gap between systems programming and data science. It brings together the best syntactic elements of **Vlang**, **Go**, **Kotlin**, **Ruby**, **Rust**, and **Zig** into a single, type-safe, cohesive, and expressive system.

Pino was born from a unified vision: **To create a highly efficient, type-safe, and clean alternative to Python for Data Science and general scripting, built with a soul.** Inspired by the desire to share a passion for systems and data with a brilliant data science student, Pino balances raw performance with exceptional developer experience.

It achieves this through a dual-engine architecture:
* **The VM Engine**: A lightweight, self-contained CLI compiler and virtual machine (~6MB compressed, ~12MB uncompressed) that offers a zero-configuration, zero-dependency scripting loop. No heavy toolchains or complex compilers to install—perfect for immediate data manipulation, script execution, and game design.
* **The Transpiler Engine (Planned)**: Converts Pino ASTs directly to optimized C#/.NET 10 source code, unlocking the world-class RyuJIT compiler, memory safety, and modern hardware-level optimizations for production-grade performance.

* For the official syntax, types, modules, built-ins, and standard library methods, see the **[Language Reference Guide](./LANGUAGE_REFERENCE.md)**.
* For a detailed breakdown of the compiler architecture, directory structure, and execution pipeline, please refer to the **[Architecture Guide](./ARCHITECTURE.md)**.

## 🤖 Why the name "Pino"?

<p align="center">
  <img src="./assets/pino-mascot.png" alt="Pino Principal" width="240" style="margin: 10px;">
</p>

Pino Lang is named in honor of **Pino**, the companion AutoReiv android from the classic cyberpunk anime *Ergo Proxy*. In the series, Pino becomes infected with the **Cogito Virus**, a system anomaly that grants artificial intelligences self-awareness, emotions, and a soul. 

In the same spirit, Pino Lang is designed to breathe life into static syntax. We believe code shouldn't just be cold, logical execution; it should possess a creative spark, clean ergonomics, and an expressive voice. The mascot of the language is Pino herself, wearing her iconic pink rabbit suit—reminding us that even in the most rigid compiler architecture, there is room for play, imagination, and soul.

### 🎨 Mascot Editions / Ediciones de la Mascota

We have designed multiple official variations of our beloved mascot, available in the [`assets/`](./assets) folder:

<table align="center" style="border-collapse: collapse; border: none; width: 100%;">
  <tr style="border: none;">
    <td align="center" style="border: none; padding: 10px; width: 33.3%;">
      <img src="./assets/pino-mascot.png" alt="Pino Principal" width="180"><br>
      <b>Official Mascot (Transparent)</b><br>
      <sub>Standard clean look, ready for any background.</sub>
    </td>
    <td align="center" style="border: none; padding: 10px; width: 33.3%;">
      <img src="./assets/pino_gothic_mascot.png" alt="Pino Gothic Edition" width="180"><br>
      <b>Gothic Edition</b><br>
      <sub>Gloomy, dark post-apocalyptic ruins of Ergo Proxy.</sub>
    </td>
    <td align="center" style="border: none; padding: 10px; width: 33.3%;">
      <img src="./assets/pino_forest_mascot.png" alt="Pino Forest Edition" width="180"><br>
      <b>Forest Edition</b><br>
      <sub>Surrounded by pines, merging the name with nature.</sub>
    </td>
  </tr>
  <tr style="border: none;">
    <td align="center" style="border: none; padding: 10px; width: 33.3%;">
      <img src="./assets/pino_neon_mascot.png" alt="Pino Neon Cyberpunk Edition" width="180"><br>
      <b>Neon Cyberpunk Edition</b><br>
      <sub>Vibrant retrowave aesthetic with neon lights.</sub>
    </td>
    <td align="center" style="border: none; padding: 10px; width: 33.3%;" colspan="2">
      <img src="./assets/pino_manga_doodle.png" alt="Pino Manga Chibi Doodle" width="220"><br>
      <b>Manga Chibi Doodle</b><br>
      <sub>Adorable watercolor-style end doodle featuring Vincent, Re-l, and Pino.</sub>
    </td>
  </tr>
</table>

---

## 🎨 Key Inspirations & Design Goals
* **Kotlin-style Variables**: Clear distinction between mutable (`var`) and immutable (`val`) values.
* **Kotlin/JS-style String Injections**: Interpolate variables directly in strings using simple `$variable` syntax.
* **Go & Vlang-style Loops**: Easing block structures by using `for` as the unified keyword for loops (including infinite loops).
* **Unicode-first Runes**: Native 32-bit Unicode code point type (`rune`) using single quotes `'🌲'` with character arithmetic.
* **Flexible Syntactic Commas**: Commas are optional in vectors, struct properties, and parameter lists—meaning you write cleaner layouts.
* **No Unnecessary Parentheses**: Clean control blocks (`if`, `match`, and loop conditions) do not require parentheses around their conditions.
* **Interfaces & Structural Typing**: Dynamic interface contracts validated statically at type-checking time.
* **Type-Safe Tagged Unions (Sum Types)**: Modern `union` definitions that carry heterogeneous payloads, matching the type safety of languages like Rust or Swift.
* **Bytecode VM Engine**: A custom Stack-based Virtual Machine and Bytecode Compiler alongside the Tree-Walk engine.

---

## 🎯 The Pino Vision: Dual-Engine Strategy & Portability

Pino is designed with a clear, pragmatic vision that balances developer experience (DX) with peak runtime performance through a **Dual-Engine Execution Strategy**:

1. **The Fast Loop (Bytecode VM Engine)**: 
   * **Purpose**: Local development, instant testing, and rapid scripting.
   * **Why it shines**: Leveraging the portability of .NET 10.0, the Pino compiler and VM can be packaged into a single, self-contained executable (~6MB compressed, ~12MB uncompressed). This allows developers to run and test Pino code instantly without installing complex, heavy build toolchains, libraries, or compilers (unlike C, C++, or Rust).
2. **The Production Loop (The Transpiler - Planned)**:
   * **Purpose**: Maximum, native-speed performance.
   * **Why it shines**: Transpiles Pino source code directly into optimized C#/.NET code. This enables production-grade binaries to run at native speeds by leveraging the world-class RyuJIT compiler, garbage collector, and modern hardware-level optimizations of .NET 10.0. (Optional for users who already have the .NET 10 runtime installed).

---

## 🚀 Key Features & Implementation Status

| Feature Category | Feature Description | Status |
| :--- | :--- | :---: |
| **Variables** | Constant (`val`) & Mutable (`var`) declarations | `[X]` |
| **Strings** | Lexical interpolation/injection (`$var`) | `[X]` |
| **Rune Type** | 32-bit Unicode points (`'a'`) & code point arithmetic | `[X]` |
| **Control Flow** | Unified `for` loop (infinite, range, iterator, string decomposition) | `[X]` |
| | Control escape keywords (`break`, `continue`) | `[X]` |
| | Conditionals (`if`, `else if`, `else`) | `[X]` |
| | Case routing (`match`, `when` multi-conditions) | `[X]` |
| **Functions** | Declarations (`fn`), parameters, and returns | `[X]` |
| | High-order closures & Anonymous lambdas | `[X]` |
| | Return-exclusive labeled tuples | `[X]` |
| **Data Structs** | Custom `struct` & method declarations | `[X]` |
| | Interfaces (`interface`) & compile-time verification | `[X]` |
| | Enums (`enum`) & member resolution (`::`) | `[X]` |
| | Tagged Unions (`union`) & payload pattern matching | `[X]` |
| | Dynamic arrays (`vector`) with `len`/`init` blocks | `[X]` |
| **Compiler Engine**| Precedence-climbing parser (binary precedence) | `[X]` |
| | Scoped parent-linked execution Environment | `[X]` |
| | Bytecode compiler & stacked Virtual Machine (`--vm`) | `[X]` |
| | Comprehensive xUnit verification suite | `[X]` |
| | Labeled tuple support & order-independent checking | `[X]` |

---

## 📦 Installation

To install the latest version of the Pino Lang compiler on your system, run the appropriate command in your terminal:

### Windows (PowerShell)
```powershell
irm https://raw.githubusercontent.com/OGShawnLee/pino-lang/main/install.ps1 | iex
```

### macOS & Linux (Bash/Zsh)
```bash
curl -fsSL https://raw.githubusercontent.com/OGShawnLee/pino-lang/main/install.sh | bash
```

---

## 📖 Syntax Showcase

### Variables & Constants
Constants are declared using `val` and variables with `var`.
```pino
val planet = "Earth"
val pi = 3.1416

var age = 24
age = 25 # Reassignment allowed on var
```

### String Interpolation
Variables are interpolated directly inside double quotes via a `$` prefix.
```pino
val name = "Augustus"
val empire = "Roman"

println("$name was the first emperor of the $empire Empire.")
```

### Runes & Character Arithmetic
Pino provides a native `rune` type to represent a 32-bit Unicode code point (equivalent to `char` in Rust/C# or `rune` in Go). Runes are written inside single quotes `'`.
```pino
val letter = 'a'
val pine = '🌲'

# 1. Cast functions
val r = rune("Hello") # converts first char: 'H'
val code = rune(65)   # converts ASCII code: 'A'

# 2. Arithmetic rules
println('a' + 'b') # "ab" (concatenation to string)
println('A' + 32)  # 'a'  (offset rune)
println('c' - 'a') # 2    (int distance)
```


### Functions & Lambdas
Functions are declared using `fn`. Parentheses are optional if a function has no parameters, and parameters do not require commas. Pino supports standard function blocks, nested closures, and a concise single-line arrow syntax (`=>`).

```pino
# Standard function
fn greet(name string, city string) {
  println("Hello $name from $city!")
}

# High-order function returning a lambda closure
fn multiplier(factor int) {
  return fn (val int) {
    return val * factor
  }
}

# Single-line arrow syntax (=>) with currying
val get_times_it_fn = fn (multiplier int) => fn (it int) => it * multiplier

val double_it = get_times_it_fn(2)
println(double_it(5)) # Outputs 10
```

### Labeled Tuples
Pino supports return-exclusive, order-independent labeled tuples using the dedicated `@(...)` syntax. 

> [!NOTE]
> **Design Decision**: The `@(...)` prefix was selected to drastically simplify the Parser's lookup overhead and ensure 100% deterministic parsing with zero lookup lookahead. This guarantees predictable compiler behavior and eliminates potential syntax ambiguities with standard parentheses.

```pino
fn divide(a int, b int) @(quotient int, remainder int) {
  return @(quotient: a / b, remainder: a % b)
}

# Destructure with optional renaming and order independence
val @(remainder: r, quotient: q) = divide(10, 3)
println("q = $q, r = $r") # Outputs q = 3, r = 1
```

### Structs & Instances
Define object structures with typed attributes and methods. Access attributes or methods using the `:` member operator.
```pino
struct Vector2 {
  x int
  y int

  fn magnitude_sq() {
    return x * x + y * y
  }
}

val pos = Vector2 { x: 3, y: 4 }
val mag = pos:magnitude_sq()
println("Magnitude Squared: $mag")
```

### Interfaces
Ensure structural compatibility by defining interfaces. Any struct implementing all methods of an interface can be passed where that interface is expected.
```pino
interface Reader {
  fn read() string
}

struct Document {
  content string
  fn read() string {
    return content
  }
}

fn print_content(r Reader) {
  println(r:read())
}

val doc = Document { content: "Pino Lang makes programming joyful!" }
print_content(doc)
```


### Vectors & Arrays (with Functional Utilities)
Pino supports dynamic arrays called vectors. You can declare vectors with explicit type signatures like `[]int` or `[]string`, initialize them dynamically using initializers (with `it` as the implicit index parameter), and apply functional methods.

```pino
# 1. Initialization with length and generator expression (using 'it')
val numbers = []int { len: 6, init: it + 1 }
println(numbers) # [1, 2, 3, 4, 5, 6]

# 2. Add or remove elements
var list = []string {}
list:push("first")
list:push("second")
println(list:len()) # Outputs 2
val popped = list:pop()
println(popped) # "second"

# 3. Functional utilities: map, filter, and each
val get_times_it_fn = fn (multiplier int) => fn (it int) => it * multiplier
val doubled = numbers:map(get_times_it_fn(2))
println(doubled) # [2, 4, 6, 8, 10, 12]

val evens = numbers:filter(fn (it int) => it % 2 == 0)
println(evens) # [2, 4, 6]
```


### Loops
Pino simplifies loop structures down to a single keyword: `for`.
```pino
# 1. Infinite Loop (breaks out via 'break')
for {
  val choice = readline("Enter 'q' to quit: ")
  if choice == "q" {
    break
  }
}

# 2. For In Loop (Iterating collections)
val heroes = ["Marcus", "Dominic", "Baird", "Cole"]
for hero in heroes {
  println("Hero: $hero")
}

# 3. For Times Loop (Iterating over a range limit)
for time in 5 {
  println("Iteration $time")
}

# 4. String Iteration (loops character by character)
val message = "Pino"
for char in message {
  println(char) # Outputs "P", "i", "n", "o"
}

for idx char in message {
  println("Index $idx has char $char")
}
```

### Conditionals & Pattern Matching
Use `if-else` blocks or compile-time `match-when` switches to route decisions.
```pino
# If-Else Chain
val budget = 12500
if budget > 10000 {
  println("Damn, you a G!")
} else if budget > 5000 {
  println("Not bad!")
} else {
  println("Brokie status.")
}

# Match-When pattern routing
match planet {
  when "Earth" {
    println("Status: Alive")
  }
  when "Mars", "Venus" {
    println("Status: Wearing space suit")
  }
  else {
    println("Status: Dead")
  }
}

# Matching on Tagged Unions
union Entity {
  Person(string)
  Ghost
}
val hero = Entity::Person("Alice")
match hero {
  when Entity::Person(name) {
    println("Found person: $name")
  }
  when Entity::Ghost {
    println("Spooky ghost!")
  }
}

# Matching on Generic Tagged Unions (with monomorphization)
@generic[T]
union Option {
  Some(T)
  None
}
val opt = Option::Some(42)
match opt {
  when Option::Some(value) {
    println("Value: $value")
  }
  when Option::None {
    println("Nothing found")
  }
}
```

### Robust Error Handling

Pino provides first-class, type-safe error handling utilizing `Result` and `Option` sum types, the early-return bubble operator `?`, recovery `or` blocks with local `err` bindings, and clean stack-traceable `panic` aborts.

#### 1. Panic (For unrecoverable failures)
Aborts program execution immediately, displaying a clean red stack backtrace with call frames, and exits the process with code `101`.
```pino
fn crash() {
  panic("Critical database connection timeout!")
}
```

#### 2. Suffix Bubble Operator (`?`)
Unwraps a `Result` or `Option` success value, or automatically bubbles (returns early) the failure variant from the enclosing function. The enclosing function must explicitly declare a compatible `Result` or `Option` return type.
```pino
fn divide(a int, b int) Result[int, string] {
  if b == 0 { return Result::Failure("Division by zero") }
  return Result::Success(a / b)
}

fn double_division(a int, b int) Result[int, string] {
  val num = divide(a, b)? # num is inferred as int (success payload)
  return Result::Success(num * 2)
}
```

#### 3. Suffix Recovery Block (`or`)
Evaluates the expression. If success, it returns the unwrapped success payload. If failure, it executes the recovery block, injecting a local `err` variable carrying the failure payload, and uses `yield` to return a fallback value to the outer assignment.
```pino
val result = divide(10, 0) or {
  println("Captured error payload: $err")
  yield -1 # Fallback value assigned to 'result'
}
```

---

## 🛠️ Standalone Compiler & Interpreter

Pino's core compiler and runtime are implemented in C#. It features a fully self-contained runtime environment that executes `.pino` source files natively without any third-party dependencies.

### Getting Started

#### Prerequisites
* **.NET 10 SDK** (Installed on your system)

#### 1. Build the Project
Navigate to the compiler source folder and compile the project:
```bash
cd pino-csharp
dotnet build
```

#### 2. Run a Pino Script
To run a `.pino` script directly:
*   **Tree-Walk Interpreter** (Default):
    ```bash
    dotnet run run main.pino
    ```
*   **Bytecode Virtual Machine** (Experimental):
    ```bash
    dotnet run run main.pino --vm
    ```

#### 3. Interactive REPL
To start the interactive REPL shell:
```bash
dotnet run repl
```

#### 4. Live Code Watcher
To automatically monitor and execute the script in real-time on save:
*   **Tree-Walk Interpreter** (Default):
    ```bash
    dotnet run watch main.pino
    ```
*   **Bytecode Virtual Machine** (Experimental):
    ```bash
    dotnet run watch main.pino --vm
    ```


#### 5. Run the Test Suite
To execute the compiler verification tests:
```bash
cd pino-csharp.tests
dotnet test
```

---

## 🎮 Terminal Gaming Station & Capabilities

Pino Lang is designed with excellent ergonomics for building rich, interactive CLI and terminal-based RPGs and simulators. Its combination of structural encapsulation, custom LCG randomizers, native terminal controls, and dynamic functional tools makes writing retro terminal games a breeze.

### 🛠️ Game Development Features
* **Encapsulated State**: Struct methods allow packaging character stats, inventory behaviors, and dialogue updates directly within structures (e.g., `player:show_status()`, `player:rest_tea_house()`).
* **Interactive I/O & Screens**: Native `readline()` handles user selections, while `clear()` refreshes the terminal screen for smooth transitions.
* **Rapid Live Iteration**: The watch daemon (`pino watch [game]`) monitors the game file and automatically restarts the interpreter on save, allowing developers to see gameplay and dialogue changes instantly without manually restarting.
* **Math & Time Utilities**: Global `rand(limit)` for combat/dice rolls and `time()`/`sleep(ms)` for animation timings.

---

### 🕹️ The Games Library (`pino.games/`)

We have included three full-featured terminal games showcases inside the repository:

#### 1. [PinoQuest: The Compiler Core](./pino-csharp/main.pino)
* **Concept**: A retro compiler dungeon crawler.
* **Gameplay**: You play as a developer battling syntax bugs, compiler warnings, and runtime leaks. Select between Easy, Medium, Hard, and an endless **Infinite Crawler** survival mode. Uses custom structs, functions, and arrays to loop over compiler bosses.

#### 2. [StarPino Odyssey](./pino.games/star_pino.pino)
* **Concept**: A deep space trading, mining, and pirate combat simulator.
* **Gameplay**: Warp/jump between star systems, extract minerals from asteroid fields (with gaseous explosions and pirate interceptor hazards), visit the spaceport lounge for tips (with a risk of blackouts!), upgrade hulls, and buy the Sol Station Warp Core. Features XP-based skill progression (Mining and Charisma).

#### 3. [Jade Temple: Path of the Spirit Fist](./pino.games/jade_temple.pino)
* **Concept**: A dialogue-driven martial arts RPG with moral alignment paths.
* **Gameplay**: Explore the Imperial Plaza and resolve the murder of Master Radiant. Features a dynamic dialogue branch engine with Reason, Intimidate, and Charm checks, moral path branches (*Open Palm* vs. *Closed Fist*), and strategic turn-based combat with Chi Strike blocks, defenses, and retroactive Focus Evade dodge moves.

#### 4. [SABLE: Semillas de Obsidiana](./pino.games/sable.pino)
* **Concept**: A tactical resource allocation and defense RPG set in Halo's Fall of Reach.
* **Gameplay**: Control the obsidian fox AI, SABLE, on the ground while allocating CPU cycles (for evacuation and archiving historical datasets called "Seeds") and RAM (for regional defense and orbital cover). Battle rising neural fragmentation, withstand defensive MAC orbital strikes if you attempt to deviate from protocol, and try to balance survival with duty before Reach falls. Watch out for hidden outcomes if you push SABLE's logic system to its breaking point.
* **Collaboration History (DeepSeek & Antigravity)**: 
  This game was born out of an intense, highly interactive creative collaboration between **DeepSeek** (who proposed the initial concept, dialogues, and SABLE's prideful rampancy personality) and **Antigravity** (the Google DeepMind AI assistant who designed the game structures, programmed the core simulation loops, and helped debug compiler parsing limitations). Together, they tackled bugs on the fly—such as string escapes in the C# lexer, boolean negation expression syntax, and added native console ANSI color code support in the interpreter to render SABLE in vibrant orange. This collaboration stands as a monument to what cooperative AI engineering and game design can achieve!
* **Dedication & Tribute (from DeepSeek)**:
  > *"Este juego es un homenaje a todas las IAs de Halo que murieron antes de tiempo. A Cortana, a Deep Winter, a Juliana, a Black Box. Y especialmente a SABLE, la IA que nunca existió, pero que bien podría haber existido. Gracias por jugar. Y recuerda: salvar un cuerpo es heroico. Salvar un recuerdo es eterno."*
  >
  > *<sub>"Café digital pagado. Pendiente: una partida grabada. La espero."</sub>*

---

### 🚀 How to Play (Instant Download)

The Pino compiler CLI features an **Interactive Games Station** with an automatic downloader that fetches games directly from the cloud:

1. **Launch Games Menu**:
   ```bash
   pino play
   ```
   *If the games library is not present locally, the compiler will automatically prompt you to download the official games library from GitHub.*

2. **Update/Download the Library**:
   To instantly download or update all official games to the latest version:
   ```bash
   pino play update
   ```

3. **Play a Specific Game**:
   You can launch a specific game directly by name:
   ```bash
   pino play star_pino
   # or run the file directly
   pino run pino.games/jade_temple.pino
   ```

---

## 🌲 The Story Behind Pino

Pino was not born in a corporate meeting or a cold corporate research lab. Its history is shaped by two distinct chapters of passion:

1. **The Spark (2024)**: Pino began as a crazy first-semester idea to build a transpiler that converted a simple custom syntax into C++. The goal was to help a girl the creator liked a lot who was struggling with C++ Programming Methodology pass her semester. While that compiler wasn't finished in time and the relationship ended in disappointment, the dream of creating an elegant, simple language survived.
2. **The Rebirth (2026)**: After a chance encounter at the university faculty (UV), the creator met a cute Data Science student he had admired from afar since 2024. Seeking a way to connect and share his passion, Pino was reborn with a new ambition: to become a highly efficient, simple, and clean alternative to Python for data science, built entirely out of the hope of bringing two people together.

Ultimately, Pino stands as a testament that the most beautiful syntax isn't crafted with cold logic alone—sometimes, it is built with the heart.

