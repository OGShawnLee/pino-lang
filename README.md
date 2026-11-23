# Pino 🌲

> AI writes more of our code every day, but people still have to read it. Pino is a small, fast language built so that both can work in it comfortably: no hidden sharing, no `null`, no garbage collector, and error messages written as plain sentences.

Pino is a statically typed programming language that I've been building since 2024. It takes the parts I like from Go, V, Kotlin, Ruby, Rust and Zig and tries to make them fit together: `val`/`var`, one `for` keyword for every loop, `$name` string interpolation, tagged unions, and `Result`-based error handling with `?` and `or`.

The goal is the best balance I can find between simplicity and performance: a language that's pleasant for people to write and read, easy for AI to write correctly, fast enough for games and services, and light enough to feel at home on a modest laptop. Today Pino is good for scripts, command-line tools, terminal games and compilers. A big part of the Pino code that exists today is a compiler for Pino, written in Pino. Games and services are next, and a clean, fast alternative to Python for data science is the long-term dream.

```pino
fn divide(a int, b int) Result[int, string] {
  if b == 0 { return Result::Failure("division by zero") }
  return Result::Success(a // b)
}

val result = divide(10, 0) or {
  println("error: $err")
  yield -1
}

val numbers = []int { len: 6, init: it + 1 }
println(numbers:filter(fn (it int) -> it % 2 == 0)) # [2, 4, 6]
```

- [Guide](./docs/guide/): [values and mutation](./docs/guide/values-and-mutation.md) and [things that may surprise you](./docs/guide/surprises.md).
- [Specification](./spec/SPEC.md): every rule of the language, numbered, each one backed by tests in [`conformance/`](./conformance/).
- [Language Reference](./LANGUAGE_REFERENCE.md): the older long-form reference. Where it disagrees with the spec, the spec wins.
- [Architecture](./ARCHITECTURE.md): how the compiler is put together.
- [Proposals](./proposals/): design notes and RFCs.

## What makes Pino different

- **Your data never changes behind your back.** In Python, JavaScript, Java or C#, giving a list to another variable or function hands over the *same* list, so a change on one side shows up on the other, often by surprise. In Python, `b = a` followed by `b.append(4)` changes `a` too. In Pino, each side gets its own copy: after `var b = a` and `b:push(4)`, `a` is exactly as it was. That makes code easy to follow: to know what a function can do to your data, you only need to read that function. It stays fast, too, because Pino only really copies when one side changes something ([copy-on-write](#memory-without-a-garbage-collector)).
- **Change is always visible.** `val` freezes a value all the way down. A function can change your data only if its parameter says `mut`, and the call says `mut` too: `heal(mut hero)`.
- **No garbage collector.** Memory is freed the moment it's no longer used, so there are no pauses and memory use stays low. (Being built in the new compiler; see [Memory](#memory-without-a-garbage-collector).)
- **No `null`.** A value that may be missing is an `Option[T]`, and the checker makes you handle both cases.
- **Errors are values.** Functions that can fail return a `Result`; `?` passes the failure up and `or { ... }` handles it in place.
- **Errors that read like sentences.** The compiler tells you what's wrong and how to fix it, without compiler jargon.
- **One obvious way.** One loop keyword, one error mechanism, one way to share data between parts of a program. Fewer choices means less to decide.
- **Light on your computer.** Not everyone has a powerful machine. Pino should run well on a 4 GB laptop: no gigabytes of SDKs to install, quick builds, and small native programs (a hello world is about 25 KB).
- **Forgiving about small things, strict about real ones.** There are no semicolons to forget. Nobody should fail a class over a missing semicolon. The checker saves its strictness for real mistakes, like changing data you said wouldn't change.

## Made for people and for AI

When AI writes most of the code, people spend their time reviewing it. Pino is designed to make that easy:

- **No aliasing.** In most languages, any function might change an object that someone else also holds. In Pino it can't, so a reviewer (or a model) only has to read the function in front of it.
- **Safety without a borrow checker.** Rust gets memory safety through lifetimes, and models spend many attempts fighting them. Pino gets most of the same guarantees from values, with no lifetimes to write.
- **Errors that teach.** A model fixes its own mistakes faster when the error says exactly what's wrong and what to do:

  ```text
  error[PN3220]: Cannot mutate immutable variable 'party' of type '[]string'.
   --> main.pino:2:1
    |
  1 | val party = ["Ann", "Bob"]
    |     ----- 'party' is declared immutable here
  2 | party:push("Cy")
    | ^^^^^^^^^^^^^^^^
    = help: declare 'party' with 'val mut' to change its contents, or with 'var' to also reassign it
  ```

- **A small language with a complete spec.** The whole [specification](./spec/SPEC.md) fits in a model's context, and every rule has a test.

The honest weakness: models have seen millions of Go and Python files and almost no Pino. A new language starts behind. The plan is to make Pino cheap to learn: a short "Pino for LLMs" page, the spec as a single file, and errors that explain themselves. I also want to measure the claim: give the same tasks to a model in Pino, Go and Rust, and count tokens, compile-and-fix rounds and correct results.

## Memory without a garbage collector

Pino's memory model is three simple ideas on the surface, with the clever part hidden in the compiler.

**1. Values with copy-on-write.** Assigning a vector doesn't copy it. Both names share the data until one of them changes, and only then is it copied. If nobody else holds the data, a change happens in place.

```pino
fn add_bonus(mut scores []int) {
  scores:push(100)
}

var a = [1, 2, 3]
var b = a          # b is its own value; the data is shared until one side changes
b:push(4)
println(a)         # [1, 2, 3]
println(b)         # [1, 2, 3, 4]

add_bonus(mut b)   # the call says 'mut', so you can see what may change
println(b)         # [1, 2, 3, 4, 100]
```

**2. `Pool[T]` and `Handle[T]` for things that point at each other.** Since there are no references, game entities, graphs and trees that need to point at each other live in a pool and refer to each other by handle. A handle is a checked ID: using one after its value was removed stops the program with a clear message instead of silently reading the wrong thing.

```pino
struct Enemy {
  pub name string
  pub mut hp int
}

var enemies = Pool[Enemy]
val goblin = enemies:add(Enemy { name: "Goblin", hp: 10 })
val target = goblin            # a handle: a checked ID, not a pointer

enemies[target]:hp -= 3
println(enemies[goblin]:hp)    # 7

enemies:remove(goblin)
println(goblin in enemies)     # false
```

**3. Reference counting, mostly optimized away.** Under the hood, each piece of heap data carries a count of how many values use it, and it's freed when the count reaches zero. Classic reference counting has two weaknesses, and Pino's design removes both:

- **Loops of objects that never get freed.** Without references, plain data can't form loops at all. Graphs live in pools, which free their contents normally. The only possible loop is a closure that captures a `var`, and a small collector handles just that case.
- **The cost of counting.** Because Pino has values, the compiler can usually prove that a value has a single owner. Then it skips the counting entirely, or reuses the memory in place. This is the "Perceus" technique from the Koka language.

On top of that come arenas: for a batch of short-lived data, like one request in a server or one frame in a game, everything is allocated in one block and freed at once.

None of these ideas are new on their own, and I'm glad to borrow them:

| Idea | Where it comes from |
| :--- | :--- |
| Reference counting with reuse (Perceus) | Koka; Lean 4 does something close |
| Values with copy-on-write | Swift's arrays and strings, Hylo |
| No GC, values only, in-place updates | Roc, the closest relative |
| Generation-checked handles | Rust game engines, Vale |
| Arenas | Zig, Odin, C game code |

What's new is the combination: Go-like simplicity with no garbage collector, no `null`, no lifetimes, and memory bugs ruled out by design. As far as I know, no mainstream language offers that mix.

**Where this stands:** values, `mut` and `Pool` are fully specified and checked by the new compiler ([`pino-csharp-re`](./pino-csharp-re/)). Its VM and its reference-counted C backend are being built now. The `pino` you install today runs on the older engine, which uses a garbage collector and doesn't know all of these rules yet.

## What Pino is good for

| Kind of program | Fit | What's still missing |
| :--- | :--- | :--- |
| Scripts and command-line tools | Good today | A REPL on the new compiler, a wider standard library |
| Compilers and language tools | Good today; the self-hosted compiler proves it | — |
| Games | A strong fit: no GC pauses, `Pool` for entities | Calling C libraries, graphics bindings |
| Servers and microservices | Good, once concurrency lands | Concurrency design, an HTTP library |
| Data science | The memory model suits it (copy-on-write tables, cheap large arrays) | Libraries, notebooks, calling C math libraries |

## Strengths and weaknesses

**Strengths**

- Small, readable syntax, with one obvious way to do most things.
- Data can't change behind your back, which makes code easy to reason about and review.
- No `null`; failures are values the checker makes you handle.
- Error messages written for people.
- Planned performance close to C: native code through C compilers, no GC, no pauses, low memory use.
- A complete written spec with a conformance test for every rule.

**Weaknesses**

- Young, and a one-person project.
- Small standard library and no package ecosystem yet.
- No concurrency yet.
- Structures that point at each other need `Pool` and handles instead of pointers, which takes some getting used to.
- Two compilers are mid-transition: the one you install and the new one.
- AI models know very little Pino yet.

## How fast will it be?

Nothing is measured yet, so these are targets, not claims.

- **Where Pino should beat Go:**
  - The new C backend hands its output to GCC or Clang, which optimize harder than Go's compiler.
  - Generics compile to a separate copy for each type, with no hidden lookups.
  - There are no GC pauses.
  - Memory is freed right away, where Go lets the heap grow to about twice the live data.
  - Structs are stored inline, which is friendly to the CPU cache.
- **Where Go may still win:**
  - Programs that create huge numbers of short-lived shared objects, unless the compiler removes the counting.
  - Highly concurrent servers: Go's scheduler is 15 years mature, and Pino's concurrency design is still open. The plan is to move values between tasks instead of sharing them, so counts never need expensive atomic updates.
  - Build speed and tooling.

Realistic target: faster than Go on single-threaded work like computation, compilers and games, with less memory and no pauses, and on par with Go for servers. Benchmarks against Go and C will come once the new backend runs.

## Why "Pino"?

Pino is named after the AutoReiv android from *Ergo Proxy*. In the show she catches the Cogito virus, which gives machines self-awareness and feelings, and she spends most of it wearing a pink rabbit suit. I liked the idea of a language with a bit of that in it: something precise that still has some play and personality. She's the mascot.

<p align="center">
  <img src="./assets/pino_manga_doodle.png" alt="Vincent, Re-l and Pino" width="380"><br>
  <sub>More artwork in <a href="./assets"><code>assets/</code></a>.</sub>
</p>

## Status

Pino is a one-person project and parts of it are still moving. There are two compilers right now: the original one, which is what `pino` installs, and a rewrite that implements the current language rules and will replace it.

| Part | State |
| :--- | :--- |
| Tree-walk interpreter | The installed default. Runs the whole language as it was before the value rules. |
| C backend (`--c`) | Experimental. Transpiles to C and compiles with a bundled TCC and Boehm GC. Most programs work; some closure patterns still fail to compile. |
| Bytecode VM (`--vm`) | Partial. Handles arithmetic, control flow and functions, but not structs or unions yet. |
| New compiler ([`pino-csharp-re`](./pino-csharp-re/)) | Where new work happens. Lexer, parser and type checker are done, with plain-sentence errors and error recovery for an editor. A new intermediate representation is in progress; it will drive both a VM and a C backend that uses reference counting instead of a GC. |
| Self-hosted compiler ([`projects/pino-compiler`](./projects/pino-compiler/)) | In progress, written by hand in Pino. |
| Editor support | Syntax highlighting for [VS Code](./vscode-pino/) and [Zed](./zed-pino/). |

Recently decided:
- Structs and collections are values, copied on write, with `mut` parameters to change the caller's value.
- `Option[T]` replaces `null`.
- Type annotations go after the name: `val n int = 12`.

The details are in [`spec/decisions/`](./spec/decisions/).

### Roadmap

1. The new compiler's VM and C backend, so Pino programs run on it.
2. The reference-counting optimizations (Perceus) and arenas.
3. A REPL and a WebAssembly build on the new compiler.
4. Benchmarks against Go and C.
5. Concurrency, calling C libraries, and a "Pino for LLMs" page.

## Install

Windows (PowerShell):
```powershell
irm https://raw.githubusercontent.com/OGShawnLee/pino-lang/main/install.ps1 | iex
```

macOS and Linux:
```bash
curl -fsSL https://raw.githubusercontent.com/OGShawnLee/pino-lang/main/install.sh | bash
```

## Getting started

```bash
pino init my-app   # creates main.pino, modules/ and test/
cd my-app
pino run
pino test
```

A new project looks like this:

```text
my-app/
├── main.pino
├── modules/
│   └── utils.pino
├── test/
│   └── main_test.pino
└── .gitignore
```

### Commands

| Command | What it does |
| :--- | :--- |
| `pino init [name]` | Create a new project. |
| `pino run [file]` | Run a file with the interpreter (defaults to `main.pino`). |
| `pino run [file] --vm` | Run it on the bytecode VM. |
| `pino run [file] --c` | Transpile to C, compile with TCC, run it, then delete the binary. |
| `pino watch [file]` | Re-run the file every time you save it. |
| `pino test [file]` | Run every `test` block in the project or file. |
| `pino compile [file] [out.c]` | Transpile to C and write the result. |
| `pino repl` | Start the REPL. |
| `pino play` | Open the games menu (downloads the games if needed). |
| `pino version` / `pino update` | Show the version, or update from GitHub. |

## A tour of the language

The examples below follow the current rules, checked by the new compiler.

### Variables

`val` can't be reassigned, and its contents can't change. `var` can be reassigned. A type, when you want one, goes after the name.

```pino
val planet = "Earth"
var age = 24
age = 25

val year int = 2026
```

For the full story, including `val mut` and `mut` parameters, see [Values and mutation](./docs/guide/values-and-mutation.md).

### Strings and runes

`$name` inside a string interpolates. Strings are slices, so taking a substring doesn't copy anything ([RFC 013](./proposals/013-immutable-string-slices-zero-copy.md)). A `rune` is a 32-bit Unicode code point written in single quotes.

```pino
val name = "Augustus"
println("$name was the first emperor of Rome.")

val pine = '🌲'
println('A' + 32)  # 'a'
println('c' - 'a') # 2
println("Pino $pine")
```

### Functions

Functions are declared with `fn`. Parameters don't need commas, and `->` gives a function value a one-line body.

```pino
fn greet(name string, city string) {
  println("Hello $name from $city!")
}

val times = fn (multiplier int) -> fn (it int) -> it * multiplier
val double = times(2)
println(double(5)) # 10
```

### Labeled tuples

A function can return several named values with `@(...)`. When you destructure, order doesn't matter and you can rename. The `@` prefix is there so the parser never has to guess whether `(` starts a tuple or a grouped expression.

```pino
fn divide(a int, b int) @(quotient int, remainder int) {
  return @(quotient: a // b, remainder: a % b)
}

val @(remainder: r, quotient: q) = divide(10, 3)
```

### Numbers

`/` always gives a `float`, and `//` is integer (floor) division.

```pino
println(7 / 2)   # 3.5
println(7 // 2)  # 3
println(-7 // 2) # -4
```

### Structs and interfaces

Member access uses `:`. Methods can use the struct's fields by name. Fields are read-only unless marked `mut`, and a method that changes the struct is a `mut fn`. Interfaces are structural: if a struct has the methods, it satisfies the interface, and the type checker verifies it.

```pino
struct Vector2 {
  x int
  y int

  fn magnitude_sq() {
    return x * x + y * y
  }
}

val pos = Vector2 { x: 3, y: 4 }
println(pos:magnitude_sq()) # 25

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

print_content(Document { content: "hello" })
```

### Vectors

```pino
val tokens = []string { cap: 100 }               # empty, with room for 100
val numbers = []int { len: 6, init: it + 1 }     # [1, 2, 3, 4, 5, 6]

var list = []string
list:push("first")
list:push("second")
println(list:pop()) # Option::Some("second")

println(numbers:map(fn (it int) -> it * 2))      # [2, 4, 6, 8, 10, 12]
```

Commas are optional in vectors, maps, struct literals, argument lists and `when` cases.

### Loops

There's one loop keyword: `for`.

```pino
for {
  val answer = readline("Enter 'q' to quit: ") or { yield "q" }
  if answer == "q" { break }
}

for hero in ["Marcus", "Dominic", "Baird", "Cole"] {
  println(hero)
}

for i in 5 {
  println(i) # 0 to 4
}

for idx, char in "Pino" {
  println("$idx: $char")
}
```

### if and match

```pino
val budget = 12500
if budget > 10000 {
  println("Damn, you a G!")
} else if budget > 5000 {
  println("Not bad!")
} else {
  println("Brokie status.")
}

fn visit(planet string) {
  match planet {
    when "Earth" { println("Alive") }
    when "Mars", "Venus" { println("Wear a space suit") }
    else { println("Dead") }
  }
}

visit("Mars")
```

### Tagged unions and Option

Variants can carry data, and `match` unpacks it. Matches on unions and enums have to cover every case. `Option[T]` is a built-in union for a value that may be missing; Pino has no `null`.

```pino
union Entity {
  Person(string)
  Ghost
}

match Entity::Person("Alice") {
  when Entity::Person(name) { println("Found $name") }
  when Entity::Ghost { println("Spooky") }
}

fn find_hero(name string) Option[string] {
  if name == "Marcus" { return Option::Some("Marcus Fenix") }
  return Option::None
}

match find_hero("Marcus") {
  when Option::Some(full) { println(full) }
  when Option::None { println("No hero by that name") }
}
```

### Errors

`panic` stops the program, prints a message and exits with code 101. For errors you expect, return a `Result` or an `Option`:

- `?` unwraps the success value or returns the failure from the current function.
- `or { ... }` handles the failure in place. The error is bound to `err`, and `yield` provides a fallback value.

```pino
fn divide(a int, b int) Result[int, string] {
  if b == 0 { return Result::Failure("division by zero") }
  return Result::Success(a // b)
}

fn double_division(a int, b int) Result[int, string] {
  val num = divide(a, b)?
  return Result::Success(num * 2)
}

val result = divide(10, 0) or {
  println("error: $err")
  yield -1
}
```

## How it runs

Both compilers are written in C# (.NET 10). The installed `pino` ships as a single self-contained executable, so you don't need .NET installed to use it.

**Today's compiler** has two ways to run code:

- **Interpreter (and the experimental VM)** for development: no build step, so you can just run the file or `pino watch` it.
- **C backend** for native binaries: Pino becomes plain C, which the bundled [TCC](https://bellard.org/tcc/) compiles and links with [Boehm GC](https://www.hboehm.info/gc/). A hello world comes out at roughly 25 KB, and the program needs nothing else to run. The reasoning is in [RFC 008b](./proposals/008b-tcc-c-backend.md).

**The new compiler** turns a checked program into one intermediate representation with two consumers:

- **A VM** for fast edit-and-run, the REPL and the browser (WebAssembly).
- **A C backend** with its own small runtime ([`runtime-re/`](./runtime-re/)) that uses reference counting instead of a GC. It can be compiled with TCC for quick builds, or with GCC or Clang for optimized ones.

Both are checked against the same conformance tests, so they must print exactly the same output.

The long-term plan is to replace the C# compiler entirely with the self-hosted one in [`projects/pino-compiler`](./projects/pino-compiler/), bootstrapped through C. Its design follows Nora Sandler's *Writing a C Compiler*.

### Building from source

You'll need the .NET 10 SDK.

```bash
cd pino-csharp
dotnet build
dotnet run run main.pino        # interpreter
dotnet run run main.pino --vm   # VM
dotnet run repl

cd ../pino-csharp.tests
dotnet test
```

The new compiler:

```bash
dotnet build pino-csharp-re/Pino.sln
dotnet run --project pino-csharp-re/Pino.Cli -- check main.pino   # type-check a file
dotnet test pino-csharp-re/Pino.sln
```

## Games

Writing games is how I find out what the language is missing. There are three in [`pino.games/`](./pino.games/). You can play them with `pino play`, `pino play star_pino`, or `pino run pino.games/jade_temple.pino`.

- **[StarPino Odyssey](./pino.games/star_pino.pino)**: space trading and mining. You jump between systems, mine asteroids, fight pirates, upgrade your hull and save up for the Sol Station warp core.
- **[Jade Temple: Path of the Spirit Fist](./pino.games/jade_temple.pino)**: a martial arts RPG. You investigate Master Radiant's murder using Reason, Intimidate and Charm checks, choose Open Palm or Closed Fist, and fight turn-based battles.
- **[SABLE: Semillas de Obsidiana](./pino.games/sable.pino)**: a resource management game set during Halo's Fall of Reach. You play the AI SABLE and split CPU and RAM between evacuation, archiving and defense while your own logic slowly breaks down. DeepSeek came up with the concept and the writing; Antigravity helped program it, and working on it uncovered several interpreter bugs that got fixed along the way.

  > *"Este juego es un homenaje a todas las IAs de Halo que murieron antes de tiempo. A Cortana, a Deep Winter, a Juliana, a Black Box. Y especialmente a SABLE, la IA que nunca existió, pero que bien podría haber existido. Gracias por jugar. Y recuerda: salvar un cuerpo es heroico. Salvar un recuerdo es eterno."*

There's also **[PinoQuest: The Compiler Core](./pino-csharp/main.pino)**, a small dungeon crawler where the enemies are syntax errors and memory leaks.

## The story

Pino didn't start as a serious project. It started with a girl, a humble laptop and a C++ course.

In 2024, my first semester, there was a girl I liked a lot. She was struggling with C++ on a modest laptop that worked hard just to run its tools, and a single missing semicolon could make a whole program fail.

My first plan was naive. I'd write a small, friendly language and a transpiler that turned it into C++, so she could write her homework the easy way and hand in the C++. It didn't take me long to see that it made no sense. What I wanted to give her wasn't a shortcut through one course. It was a language of her own: simple, forgiving about the small things, and light enough for a laptop like hers.

I didn't finish it in time, and it didn't end the way I hoped. But the language kept growing, and that semester became its rules: no semicolons, errors that help instead of scold, and a compiler that runs well on a modest computer. A language can be imperfect and still be kind, and I think that's what makes it beautiful.

In 2026 I ran into a Data Science student at the faculty whom I'd admired from a distance since 2024. I wanted a way to connect and to share what I love, and Pino came back with a new goal: to be a clean, fast alternative to Python for people like her.

So Pino was built with the heart as much as with logic.

> *"Love is in the things we build for those we cherish, even if they may never be seen. And when they are, we have found home."*

## How Pino is made

I want to be open about how this language is built, because AI is part of it.

- **2024: by hand.** The first versions of Pino were written by hand in C++.
- **The move to C#.** The compiler was later migrated to C#, with Gemini Flash doing most of the migration.
- **Today: AI with a human in charge.** The new compiler ([`pino-csharp-re`](./pino-csharp-re/)), the [specification](./spec/SPEC.md) and the conformance tests are written and heavily improved by Claude and Gemini. They work from small tickets, and every design decision is mine, recorded in [`spec/decisions/`](./spec/decisions/). Nothing is merged until the full test suite passes. Commits written with an AI name the model in a `Co-Authored-By` line, so the history shows who wrote what.
- **The self-hosted compiler: by hand.** [`projects/pino-compiler`](./projects/pino-compiler/) is written by hand, one piece at a time, while I read the literature, starting with Nora Sandler's *Writing a C Compiler*. This is where I learn how compilers really work.
- **The games.** SABLE's concept and writing came from DeepSeek, and Antigravity helped program it, as noted [above](#games).

The tools have changed over the years. The reason hasn't.
