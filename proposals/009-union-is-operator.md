# RFC 009: Union Variant Checking with `is` Operator in Pino

* **Status**: Proposed
* **Authors**: Antigravity & OGShawnLee
* **Date**: 2026-07-08

---

## 1. Summary
This RFC proposes the introduction of the `is` and `is not` operators in Pino to check if a tagged union (or enum) value matches a specific variant, with support for optional inline pattern variable binding (destructuring) and explicit generic type parameters.

---

## 2. Motivation
Currently, in order to check if a union matches a specific variant, programmers are forced to write a complete `match` block:

```pino
# Current verbose match-block boilerplate:
var is_var = false
match left {
  when Expression::Var => { is_var = true }
  else => { is_var = false }
}
if is_var { ... }
```

Or they must construct dummy variants with default values for comparison, which is slow and often impossible if payloads lack defaults:

```pino
# Fails or is extremely verbose:
if left == Expression::Var("") { ... }
```

In language compiler codebases (such as the Pino C compiler in `modules/parser.pino`), variant-checking is one of the most frequent operations. A lightweight, first-class operator to test variants and bind their payloads directly inside control flow conditions would significantly reduce boilerplate, enhance readability, and align Pino with modern programming languages.

---

## 3. Proposed Syntax

### 3.1 Simple Variant Check
Checks if a union belongs to a specific variant, ignoring its payload:

```pino
val optional_name = Option::Some("Marcus")

# Syntax: <expr> is <UnionName>::<VariantName>
if optional_name is Option::Some {
  println("optional_name has a value")
}

# Syntax: <expr> is not <UnionName>::<VariantName>
if optional_name is not Option::Some {
  println("optional_name is None")
}
```

### 3.2 Generic / Specialized Unions
For generic unions, both explicit specialization and implicit type inference are supported. Consider this generic union representing remote resource states:

```pino
@generic[T]
union RemoteData {
  Loading
  Success(T)
  Failure(string)
}
```

#### A. Explicit Specialization
The programmer explicitly specifies the generic parameters to match:

```pino
val state = RemoteData::Success(42)

# Syntax: <expr> is <UnionName>[<TypeArguments>]::<VariantName>
if state is RemoteData[int]::Success {
  println("Request succeeded with an integer payload!")
}
```

#### B. Implicit Type Inference (Omission of Type Parameters)
Since the static type checker already knows the concrete generic type of the checked variable, you can omit the generic type parameters `[...]` entirely. 

This is extremely useful when dealing with complex generic types. For example, if you have a variable:
```pino
val result Result[Vector[int], map[string, float]] = ...
```

Without implicit inference, you would be forced to write the full specialized generic signature every time you check a variant:
```pino
# Verbose and redundant:
if result is Result[Vector[int], map[string, float]]::Success {
  println("Success")
}
```

With implicit type inference, you can write:
```pino
# Clean and concise:
if result is Result::Success {
  println("Success")
}
```
The compiler automatically infers that `Result::Success` refers to the monomorphized `Result[Vector[int], map[string, float]]::Success` variant because `result` is already defined with that type.

### 3.3 Variant Check with Pattern Bindings
If the variant matches, its payload is destructured and bound to local variables available within the conditional statement's scope (e.g. the `if` body):

```pino
if optional_name is Option::Some(name) {
  println(name) # `name` is bound here
} else {
  println("No name available")
}

if state is RemoteData[int]::Success(number) {
  println("Payload value is: $number")
}
```

### 3.4 Relationship with Union Equality (`==` / `!=`)
There is a semantic overlap between value equality (`==`) and pattern checking (`is`) for variants that do not contain payload values (e.g. `Foo::A` or `Option::None`):

```pino
# Both statements are valid, evaluate to the same boolean, and compile to identical tag comparisons:
if opt == Option::None { ... }
if opt is Option::None { ... }
```

However, they serve distinct purposes:
* **Value Equality (`==`)** compares the **entire structure and payload** of two concrete instances recursively. It requires both sides to be fully instantiated expressions.
* **Variant Check (`is`)** matches an instance against a **pattern**. It checks only the variant tag (ignoring payloads unless specified) and allows destructuring bindings.

---

## 4. Design & Comparison with Other Languages

Modern systems and application languages solve this issue in various ways:

| Language | Syntax for Variant Check | Syntax for Binding |
| :--- | :--- | :--- |
| **Rust** | `matches!(x, Option::Some(_))` | `if let Option::Some(name) = x { ... }` |
| **Swift** | `if case .some = x` | `if case let .some(name) = x { ... }` |
| **Kotlin** | `if (x is Option.Some)` | `if (x is Option.Some) { x.value ... }` (Smart casting) |
| **Pino (Proposed)**| `if x is Option::Some` | `if x is Option::Some(name) { ... }` |

Pino's proposed `is` operator offers a highly readable, natural language style that avoids the reversed assignment syntax of Rust's `if let` while supporting clean destructuring.

---

## 5. Detailed Implementation Plan

### 5.1 AST Node Addition
We introduce `IsExpression` inheriting from `Expression`:

```csharp
namespace Pino;

public record IsExpression(
    Expression Value, 
    Pattern Pattern, 
    bool IsNot
) : Expression;
```

### 5.2 Lexer and Parser Changes
1. **Operator Precedence**: The `is` operator acts as a comparison operator, carrying the same precedence as `==` and `!=`.
2. **Parsing Rules**:
   Inside `Parser.Expression.cs`, during expression precedence climbing:
   - If the parser encounters the keyword `is`:
     - Check if the next token is `not` to set `IsNot = true`.
     - Parse the right-hand side as a `Pattern` (reusing the existing modular pattern parsing logic).
     - Construct and return the `IsExpression`.
3. **Generic Pattern Parsing**:
   To support generic parameters (like `Data[int]::Integer`), `ParsePattern` peeks ahead to identify if an identifier is followed by brackets (`[...]`) before a static member access separator (`::`). If matching, it consumes the generic arguments as part of the `UnionName` (e.g. `Data[int]`).

### 5.3 Type Checker Integration
1. The static type checker validates that the left-hand side of `is` evaluates to a `union` (or `enum`) type.
2. It verifies that the specified variant exists on that union.
3. **Monomorphization Propagation**: During type checking or type substitution, the monomorphizer uses `SubstituteType` to map `Data[int]` to its concrete C-style monomorphized struct name `Data_int`.
4. The inferred return type of the `IsExpression` is always `"bool"`.
5. **Variable Scoping**: When type checking an `IfStatement` whose condition contains an `IsExpression` with pattern bindings:
   - The variables declared in the subpatterns (e.g., `name` in `Option::Some(name)`) are added to the type scope of the `Consequent` (the `if` body block).

### 5.4 Code Generation (C Backend)
The C compiler target transpiles the `IsExpression` as follows:

1. **Tag Comparison**:
   - `x is Option::Some` -> `(x->tag == OptionTag_Some)`
   - `x is not Option::Some` -> `(x->tag != OptionTag_Some)`
2. **Payload Extraction**:
   If the condition contains variables to bind (e.g., `x is Option::Some(name)`), the transpiler injects local variable declarations at the start of the `if` body to extract payload fields:
   ```c
   if (optional_name->tag == OptionTag_Some) {
       const char* name = optional_name->value.Some._0;
       // user code using name...
   }
   ```
