# Pino Lang 🌲

A modern, simple, and highly aesthetic programming language designed to make writing code a joyful experience. Pino brings together the best syntactic elements of **Vlang**, **Go**, **Kotlin**, and **Ruby** into a single, cohesive, and expressive system.

Pino is built with a standalone tree-walk interpreter in **C# (.NET 10)** featuring a robust lexical analyzer, a precedence-climbing parser, and a fully scoped runtime environment.

For a detailed breakdown of the compiler architecture, directory structure, and execution pipeline, please refer to the [ARCHITECTURE.md](./ARCHITECTURE.md) guide.

---

## 🎨 Key Inspirations & Design Goals
* **Kotlin-style Variables**: Clear distinction between mutable (`var`) and immutable (`val`) values.
* **Kotlin/JS-style String Injections**: Interpolate variables directly in strings using simple `$variable` syntax.
* **Go & Vlang-style Loops**: Easing block structures by using `for` as the unified keyword for loops (including infinite loops).
* **Flexible Syntactic Commas**: Commas are optional in vectors, struct properties, and parameter lists—meaning you write cleaner layouts.
* **No Unnecessary Parentheses**: Clean control blocks (`if`, `match`, and loop conditions) do not require parentheses around their conditions.

---

## 🚀 Key Features & Implementation Status

| Feature Category | Feature Description | Status |
| :--- | :--- | :---: |
| **Variables** | Constant (`val`) & Mutable (`var`) declarations | `[X]` |
| **Strings** | Lexical interpolation/injection (`$var`) | `[X]` |
| **Control Flow** | Unified `for` loop (infinite, range, iterator) | `[X]` |
| | Control escape keywords (`break`, `continue`) | `[X]` |
| | Conditionals (`if`, `else if`, `else`) | `[X]` |
| | Case routing (`match`, `when` multi-conditions) | `[X]` |
| **Functions** | Declarations (`fn`), parameters, and returns | `[X]` |
| | High-order closures & Anonymous lambdas | `[X]` |
| **Data Structs** | Custom `struct` & method declarations | `[X]` |
| | Enums (`enum`) & member resolution (`::`) | `[X]` |
| | Dynamic arrays (`vector`) with `len`/`init` blocks | `[X]` |
| **Compiler Engine**| Precedence-climbing parser (binary precedence) | `[X]` |
| | Scoped parent-linked execution Environment | `[X]` |
| | Comprehensive xUnit verification suite | `[X]` |

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
match readline("Teleport planet: ") {
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
```bash
dotnet run run main.pino
```

#### 3. Interactive REPL
To start the interactive REPL shell:
```bash
dotnet run repl
```

#### 4. Run the Test Suite
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

