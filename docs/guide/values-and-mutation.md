# Values, not objects

In Pino, a variable holds a **value**. When you assign it or pass it to a function, the other side gets its own value. Changing one never changes the other.

This one rule is why Pino stays simple (no pointers, no "who else is holding this?") and why it can be fast (the compiler knows that plain data can't form tangled webs). Pino is aimed at Go-like microservices and games, and this is how it gets there.

Short snippets on this page run as scripts. Longer ones use `fn main()`. Every example was run through the checker (`pino-re check`).

## 1. Assigning copies

A vector, a map and a struct all behave the same way:

<!-- check: ok -->
```pino
fn main() {
  var a = [1, 2, 3]
  var b = a
  b:push(4)
  println(a)   # [1, 2, 3]
  println(b)   # [1, 2, 3, 4]
}
```

<!-- check: ok -->
```pino
fn main() {
  var stock = map[string, int] { "apples" -> 3 }
  var backup = stock
  stock["apples"] = 0
  println(stock["apples"])    # 0
  println(backup["apples"])   # 3
}
```

<!-- check: ok -->
```pino
struct Point {
  mut x int
  mut y int
}

fn main() {
  var p = Point { x: 1, y: 2 }
  var q = p
  q:x = 10
  println(p:x)   # 1
  println(q:x)   # 10
}
```

You may worry that copying a big vector on every assignment is slow. It isn't, because Pino uses **copy-on-write**. `var b = a` is a cheap "both names share the data for now". The real copy happens only if one of the two is changed while the other still holds on. If nobody else is holding the data, a change happens in place with no copy at all.

Sets and unions work the same way.

*Spec: VEC-19, MAP-13, SET-7, STRUCT-16, UNION-24, D-177.*

## 2. Four ways to declare and pass data

| | How you write it | Reassign it (`x = ...`) | Change what's inside | Use it for |
| :--- | :--- | :---: | :---: | :--- |
| `val` | `val x = [1, 2]` | no | no | Things that never change. The default choice. |
| `val mut` | `val mut x = []int` | no | yes | A collection or struct you fill up but never replace. |
| `var` | `var x = 0` | yes | yes | Counters, accumulators, anything you reassign. |
| `mut` parameter | `fn f(mut x []int)`, called as `f(mut a)` | | yes, and the caller sees it | A function that changes its caller's data. |

Three things to know:

- **`val` is frozen all the way down.** A `val` vector can't be pushed to, and a `val` struct can't have its fields changed. Other languages' `const` and `final` only freeze the name. Pino's `val` freezes the contents too.
- **Plain parameters are frozen too.** A function can read what you pass it, but it can't change it. To let it change your data, the parameter says `mut`, and so does the call: `f(mut a)`. The `mut` at the call site means you can always see which calls may change your variables.
- **`mut` is never a fourth way to declare a variable.** It's a word that goes *after* `val`, or in front of a field, method or parameter.

Here are all four together:

<!-- check: ok -->
```pino
fn add_one(mut items []int) {
  items:push(1)
}

fn main() {
  val frozen = [1, 2, 3]
  val mut cache = []int
  var total = 0
  cache:push(7)
  total = total + 1
  add_one(mut cache)
  println(frozen)   # [1, 2, 3]
  println(cache)    # [7, 1]
  println(total)    # 1
}
```

And here is what the checker says when you cross a line:

<!-- check: PN3220 -->
```pino
fn main() {
  val frozen = [1, 2, 3]
  frozen:push(4)   # PN3220: Cannot mutate immutable variable 'frozen'
}
```

<!-- check: PN3229 -->
```pino
fn main() {
  val mut items = []int
  items:push(1)    # fine
  items = []int    # PN3229: Cannot reassign 'val mut' variable 'items'
}
```

<!-- check: PN3230 -->
```pino
fn main() {
  val mut count = 0   # PN3230: Type 'int' has nothing to mutate
  println(count)
}
```

`val mut` only makes sense for types that have something inside to change: vectors, maps, sets, structs with a `mut` field, and interfaces with a `mut` member. For a number, a string or a union such as `Option`, use `val` or `var`.

<!-- check: PN3223 -->
```pino
fn bump(mut items []int) {
  items:push(1)
}

fn main() {
  var v = [0]
  bump(v)   # PN3223: must be marked 'mut' at the call site
}
```

<!-- check: PN3220 -->
```pino
fn bump(items []int) {
  items:push(1)   # PN3220: Cannot mutate immutable parameter 'items'
}

fn main() {
  var v = [0]
  bump(v)
}
```

The word order matters. `val mut` is right. `mut val` and `var mut` are both errors (PN2022), and the message tells you the fix:

<!-- check: PN2022 -->
```pino
fn main() {
  mut val items = []int   # PN2022: 'mut val' is not valid; write 'val mut' instead.
}
```

<!-- check: PN2022 -->
```pino
fn main() {
  var mut items = []int   # PN2022: 'var mut' is not valid; 'var' bindings are already mutable.
}
```

A rule of thumb: start with `val`. Move to `val mut` when you need to fill something up. Move to `var` when you need to point the name at something new.

*Spec: VAR-1, VAR-2, VAR-12, VAR-13, VAR-E1, VAR-E10, VAR-E12, VAR-E13, FN-1, FN-E14, FN-E15, FN-E16, LEX-2, D-177, D-180, D-183.*

Two calls can't hand out the same variable twice as `mut`, since each would change it behind the other's back:

<!-- check: PN3226 -->
```pino
fn swap(mut a int, mut b int) {
  val t = a
  a = b
  b = t
}

fn main() {
  var x = 1
  swap(mut x, mut x)   # PN3226: Cannot use 'x' twice as 'mut' in one call to 'swap'.
}
```

*Spec: FN-E16, D-180.*

## 3. `mut` on struct fields, methods, interfaces and `pub`

Struct fields are read-only by default. To let code change a field, mark it `mut`. A method that changes the struct is a `mut fn`.

<!-- check: ok -->
```pino
struct Counter {
  mut count int
  step int

  mut fn tick() {
    count += step
  }

  fn doubled() int {
    return count * 2
  }
}

fn main() {
  var c = Counter { count: 0, step: 2 }
  c:tick()
  c:tick()
  println(c:doubled())   # 8
  println(c:count)       # 4
}
```

A few rules, each with the error it gives:

- A field without `mut` can't be changed, even on a `var` (PN3221).
- A plain `fn` method can't change `self` (PN3222). Write `mut fn` if it should.
- Calling a `mut fn` needs a `var` (or `val mut`) receiver, not a `val` (PN3220).
- Inside a `mut fn` you can change `mut` fields, but you can't reassign `self` itself.

<!-- check: PN3221 -->
```pino
struct Counter {
  count int
}

fn main() {
  var c = Counter { count: 0 }
  c:count = 5   # PN3221: Cannot mutate read-only field 'count'
}
```

<!-- check: PN3222 -->
```pino
struct Counter {
  mut count int

  fn tick() {
    count += 1   # PN3222: Cannot mutate 'self' inside non-mutating method
  }
}

fn main() {
  var c = Counter { count: 0 }
  c:tick()
}
```

**Interfaces** can ask for `mut` too: a `mut fn` method, or a `mut` property. A struct that has a `mut` field can fill a read-only property. A `mut` property needs a `mut` field. A `mut fn` in an interface accepts either a `mut fn` or a plain `fn`. A plain interface method won't accept a `mut fn`.

To pass an interface value as `mut`, keep it in an interface-typed variable:

<!-- check: ok -->
```pino
interface Resettable {
  mut hits int
  mut fn reset()
}

struct Counter {
  mut hits int

  mut fn reset() {
    hits = 0
  }
}

struct Noop {
  mut hits int

  fn reset() {
  }
}

fn clear_all(mut r Resettable) {
  r:reset()
}

fn main() {
  var a Resettable = Counter { hits: 3 }
  var b Resettable = Noop { hits: 1 }
  clear_all(mut a)
  clear_all(mut b)
  println(a:hits)   # 0
}
```

<!-- check: PN3108 -->
```pino
interface Reader {
  fn step()
}

struct Bad {
  mut fn step() {
  }
}

fn use_reader(r Reader) {
}

fn main() {
  use_reader(Bad {})   # PN3108: method 'step' must not be 'mut'
}
```

**`pub mut`.** Visibility and mutability are separate. `pub` lets other modules read a field. `pub mut` lets them read it and change it (when they hold a mutable value). `pub` always comes first. `mut pub` is an error (PN2022).

<!-- check: ok group=shapes file=modules/Shapes.pino -->
```pino
module Shapes

pub struct Sprite {
  pub mut x int
  pub name string
  mut hp int
}

pub fn make(name string) Sprite {
  return Sprite { x: 0, name: name, hp: 3 }
}
```

<!-- check: ok group=shapes file=main.pino -->
```pino
import Shapes

fn main() {
  var s = Shapes::make("hero")
  s:x += 1            # fine: pub mut
  println(s:x)        # 1
  # s:name = "villain"   would be PN3221: 'name' is pub but not mut
  # s:hp                 would be an error: 'hp' is private to Shapes
}
```

If you want a field that everyone can read but only the module can change, make it private and add a `pub fn` getter.

<!-- check: PN2022 -->
```pino
struct Sprite {
  mut pub x int   # PN2022: 'mut' must appear after 'pub' ('pub mut')
}

fn main() {
}
```

*Spec: STRUCT-2, STRUCT-4 to STRUCT-9, STRUCT-20, STRUCT-E15, STRUCT-E16, STRUCT-E17, STRUCT-E18, IFACE-1, IFACE-4 to IFACE-6, IFACE-E7, D-163, D-179.*

## 4. The linked-data caveat

Because storing something stores a **copy**, you can't keep a "pointer" to a struct by putting it inside another struct. This is the biggest thing to unlearn if you come from Python, Java or JavaScript.

<!-- check: ok -->
```pino
struct Enemy {
  mut hp int
}

struct Hero {
  mut target Enemy
}

fn main() {
  var goblin = Enemy { hp: 10 }
  var hero = Hero { target: goblin }
  hero:target:hp = 6
  println(goblin:hp)         # 10, the goblin is untouched
  println(hero:target:hp)    # 6, the hero holds its own copy
}
```

If you wanted "the hero is fighting *that* goblin", this isn't it. The hero holds a copy of the goblin as it was.

**The fix is to keep things in one place and point at them by number.** Put all the enemies in one vector, and let others remember an index (or an ID):

<!-- check: ok -->
```pino
struct Enemy {
  name string
  mut hp int
}

struct World {
  mut enemies []Enemy
  mut target int

  mut fn hit(damage int) {
    enemies[target]:hp -= damage
  }
}

fn main() {
  var world = World {
    enemies: [Enemy { name: "goblin", hp: 10 }, Enemy { name: "orc", hp: 30 }],
    target: 0
  }
  world:hit(4)
  println(world:enemies[0]:hp)   # 6
}
```

There is one goblin, in `world:enemies`, and everything else says "enemy number 0". This is the same pattern used in many fast game engines, and it works well with Pino's value model.

`Pool[T]` and `Handle[T]` are built in. A pool hands out a checked ID (a handle) for each value you add, so a stale ID is caught instead of pointing at the wrong enemy:

<!-- check: ok -->
```pino
struct Enemy {
  pub mut hp int
}

fn main() {
  var enemies = Pool[Enemy]
  val goblin = enemies:add(Enemy { hp: 10 })
  enemies[goblin]:hp -= 4
  println(enemies[goblin]:hp)   # 6
}
```

See `spec/features/pools.md` for the full rules: [pools](../../spec/features/pools.md). There is no other shared-reference type.

*Spec: D-177 ("Linked-data caveat"), STRUCT-16.*

## 5. Surprise copies, and how to avoid them

Copy-on-write is cheap until you change something that is still shared. Then Pino has to make the copy it put off.

<!-- check: ok -->
```pino
fn main() {
  var big = []int { len: 1000, init: it }
  val snapshot = big   # both names share the data for now
  big:push(1)          # big changes while snapshot still holds it: the copy happens here
  println(snapshot:len)   # 1000
  println(big:len)        # 1001
}
```

That is the right result, and sometimes it's what you want (a snapshot!). It's a surprise only when you didn't mean to keep a second name. Three habits avoid it:

- **Don't keep an extra name for something big that you're about to change.** If you only need to look at it, pass it to a function and let that function finish before you change it.
- **Reading is free.** Passing a collection to a plain parameter doesn't copy it.
- **Let a function change it for you.** A `mut` parameter changes your data in place, with no copy.

<!-- check: ok -->
```pino
fn total(items []int) int {
  var sum = 0
  for x in items {
    sum += x
  }
  return sum
}

fn main() {
  var big = []int { len: 1000, init: it }
  println(total(big))   # reading doesn't copy: 499500
  big:push(1)           # nobody else holds big, so this changes it in place
  println(big:len)      # 1001
}
```

*Spec: D-177 (copy-on-write, "copies are logical"), VEC-19.*

## 6. Module-level state

Sometimes you need a counter or a cache that lives for the whole program. Pino allows this in one place, and asks you to think twice.

**Allowed:** a *private* top-level `var` or `val mut` in an **imported module**.

<!-- check: ok group=ids file=modules/Ids.pino -->
```pino
module Ids

var next = 1
val mut seen = []int

pub fn new_id() int {
  val id = next
  next = next + 1
  seen:push(id)
  return id
}

pub fn count() int {
  return seen:len
}
```

<!-- check: ok group=ids file=main.pino -->
```pino
import Ids

fn main() {
  println(Ids::new_id())   # 1
  println(Ids::new_id())   # 2
  println(Ids::count())    # 2
}
```

**Not allowed:**

- `pub var` and `pub val mut`. Other modules reach module state only through your `pub fn`s. (`pub val` is fine: it's a frozen constant.)
- A top-level `var` or `val mut` in the **main file of a program** (a file with `fn main`). A single-file *script* without `fn main` may have them.

<!-- check: PN3007 -->
```pino
module Ids

pub var next = 1   # PN3007: Top-level 'var' cannot be marked 'pub'
```

<!-- check: PN3410 -->
```pino
var next = 1   # PN3410: Top-level variable 'next' must be declared with 'val' in program mode

fn main() {
  println(next)
}
```

**Prefer passing state.** Keep your state in `main`, or in a `Context` struct, and hand it to the functions that need it with `mut`:

<!-- check: ok -->
```pino
struct Context {
  mut next_id int
  mut log []string
}

fn new_id(mut ctx Context) int {
  val id = ctx:next_id
  ctx:next_id += 1
  ctx:log:push("made id $id")
  return id
}

fn main() {
  var ctx = Context { next_id: 1, log: []string }
  println(new_id(mut ctx))   # 1
  println(new_id(mut ctx))   # 2
  println(ctx:log:len)       # 2
}
```

Now every function that can change `ctx` says so at the call site (`mut ctx`).

**What module state costs you:**

- **It's hidden.** It's the one place where a call can change something and the call site doesn't show it. `Ids::new_id()` looks like it changes nothing, but it does.
- **There is only one of it.** A module is loaded once, so you can't have two independent counters.
- **Tests share it.** Tests in the same run see each other's changes unless you reset the state yourself.
- **Threads will need a rule.** Pino has no concurrency yet. When it arrives, module state will need an explicit rule about sharing.

Behind the scenes, the compiler works out which functions read or write module state, so future optimizations can use it. That needs no syntax from you and gives no warnings.

*Spec: VAR-7, VAR-12, MAIN-3, MAIN-E3, MOD-5, MOD-E9, D-182, D-183.*

## 7. `===` and `!==` are gone

Other languages have two kinds of equality: "equal contents" (`==`) and "the very same object" (`===`, `is`, `==` on references). Pino has only the first.

<!-- check: ok -->
```pino
val a = [1, 2]
val b = a
println(a == b)   # true: same contents
```

<!-- check: PN2023 -->
```pino
val a = [1, 2]
val b = a
println(a === b)   # PN2023: Identity operator '===' has been removed; use '=='.
```

*Why:* values don't have an identity. `val b = a` is a separate (logical) copy, so "is it the same object?" has no meaningful answer, and asking it would leak whether the compiler has done a real copy yet. `==` compares contents, all the way down, for vectors, maps, structs and unions.

*Spec: EQ-1 to EQ-9, EQ-13, EQ-E1, D-178.*

## 8. `push` returns nothing

`v:push(x)` changes `v` and returns nothing, like `set:add`. So you can't chain it, and you can't use it as a value:

<!-- check: ok -->
```pino
var v = []int
v:push(1)
v:push(2)
println(v)   # [1, 2]
```

<!-- check: PN3208 -->
```pino
var v = []int
v:push(1):push(2)   # PN3208: Type 'void' does not have method 'push'
```

*Why:* a chain would have to return a vector, and under value semantics that would be a copy, not your `v`. Methods that change their receiver (`push`, `add`, `pop`, `remove`) simply change it.

*Spec: VECLIB-5 to VECLIB-7, VECLIB-E1, D-181.*

## 9. Where Pino shines, and where it's weaker

**Pino is a good fit for:**

- network services and command-line tools
- compilers and interpreters (trees and tables, built from plain data)
- games with one central state (a `World` with vectors of things, updated each frame)
- data pipelines
- teaching: the model is small, and what you see is what you get

**Pino is weaker for:**

- **object webs**, where many objects point at each other
- **GUI trees with parent pointers**
- **hard real-time** work, where a surprise copy is not acceptable
- **shared-memory concurrent structures**, which need shared mutable state that Pino's value model deliberately doesn't have

You can still build these in Pino with IDs and flat containers (see section 4), but the language isn't going to make it feel natural.

*Spec: D-177 ("Where Pino shines vs. where it is weaker").*

## Coming from...

> - **Python:** `b = a` shares one list there. Here `var b = a` gives you your own, so `b:push(4)` leaves `a` alone. There's no `copy.deepcopy` and no `:clone()`, because you never need one.
> - **JavaScript:** `const` only stops you reassigning; the array or object inside can still change. `val` freezes everything inside as well, and `val mut` is the "can fill, can't reassign" form. Objects aren't shared references, so two names never see each other's changes.
> - **Go:** Structs copy like in Go, but so do slices and maps (no shared backing array). There are no pointers. A `mut` parameter does the job of `*T`, and the call site says `f(mut x)` where Go says `f(&x)`.
> - **Rust:** `val` is `let`, `var` is `let mut`, and `mut` parameters are `&mut` with `mut` at the call site. There are no lifetimes and no borrow annotations. The price is that you can't hold a reference inside a struct. Use an index.

Next: [Things that surprise newcomers](surprises.md).
