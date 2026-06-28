# RFC 006: Match Expressions in Pino

* **Status**: Implemented
* **Authors**: Antigravity & OGShawnLee
* **Date**: 2026-06-28

---

## 1. Summary
This RFC proposes making `match` statements double as expressions in Pino. This enables `match` to return values from its branches, similar to `if-else` ternary expressions, supporting both lightweight arrow-syntax (`=>`) for single expressions and block-syntax (`{ ... }`) with `yield` for multi-line computations.

---

## 2. Motivation
Currently, `match` in Pino is strictly a statement. While highly readable, this forces programmers to declare mutable intermediate variables (`var`) to assign values conditionally across match branches:

```pino
# Current statement-only boilerplate:
var gravity = 0.0
match planet {
  when Planet::Earth { gravity = 9.81 }
  when Planet::Mars { gravity = 3.71 }
}
```

Allowing `match` to act as a first-class expression simplifies this pattern, promotes immutability (`val`), and integrates seamlessly with Pino's existing `or { yield ... }` block recovery syntax:

```pino
# Proposed expression syntax:
val gravity = match planet {
  when Planet::Earth => 9.81
  when Planet::Mars => 3.71
}
```

---

## 3. Syntax Variants

To cover both simple and complex branch evaluation, the expression form supports two syntactical variants:

### 3.1 Single-Expression Branches (Arrow Syntax)
Uses `=>` followed by a single expression. This is lightweight and ideal for mapping enums or simple destructuring:

```pino
val result = match planet {
  when Planet::Mercury => 3.7
  when Planet::Venus => 8.87
  when Planet::Earth => 9.81
  when Planet::Mars => 3.71
}
```

### 3.2 Block-Expression Branches (Yield Block Syntax)
Uses a block `{ ... }` when multi-line operations are required before returning. It uses the `yield` keyword to return the value, maintaining semantic alignment with `or` recovery blocks:

```pino
val gravity = match planet {
  when Planet::Earth {
    val factor = 1.0
    yield 9.81 * factor
  }
  when Planet::Mars {
    yield 3.71
  }
}
```

---

## 4. Detailed Design

### 4.1 AST Modifications

The AST nodes will be updated to allow `match` to function as an expression:

* Rename `MatchStatement` to `MatchExpression` and make it inherit from `Expression`:
  ```csharp
  public record MatchExpression(Expression Condition, List<WhenExpression> Branches, ElseExpression? Alternate) : Expression;
  ```
* Make `WhenExpression` represent a match branch, supporting either an expression or a block statement:
  ```csharp
  public record WhenExpression(List<Pattern> Conditions, Statement Body) : ASTNode;
  ```
  *(Since `Expression` is a subclass of `Statement`, `Body` can store a `BlockStatement` for block syntax or any `Expression` for arrow syntax.)*

* Make `ElseExpression` represent the wildcard match block/expression:
  ```csharp
  public record ElseExpression(Statement Body) : ASTNode;
  ```

---

### 4.2 Parser Changes

* Update `IsExpression` in [Parser.Expression.cs](../pino-csharp/Parser/Parser.Expression.cs) to recognize `match` as an expression starter.
* Add `ParseMatchExpression` to [Parser.Expression.cs](../pino-csharp/Parser/Parser.Expression.cs), parsing the condition, opening brace, branch list, optional `else` block, and closing brace.
* In `ParseWhenExpression`:
  * If the parser encounters `=>` after patterns, parse a single expression and store it in `Body`.
  * If the parser encounters `{` after patterns, parse a `BlockStatement` and store it in `Body`.

---

### 4.3 Type Checker Validation

* **Type Unification**: The type checker must infer the type of the `MatchExpression` by validating that all branches (including the `else` alternate branch, if present) evaluate to compatible types.
* **Exhaustiveness Rules**: If a `MatchExpression` is used as an expression (e.g. assigned to a variable or passed as an argument), it must be verified as **exhaustive** (either covering all enum/union variants or providing an `else` branch).
* **Yield Binding**: While typechecking block branches (`{ ... }`), the checker will bind the expected return type to `_currentYieldType`. Any `yield` statement inside the block is checked against this type.

---

### 4.4 Evaluator Runtime Execution

* Evaluate the `Condition` expression.
* Traverse the branches in order:
  * For the matching branch:
    * If the branch uses arrow-syntax (body is a pure expression), evaluate the expression and return the value.
    * If the branch uses block-syntax (body is a block), execute the block. Catch the `PinoYieldException` to extract and return the yielded value.
* If no branch matches:
  * If an `else` branch is defined, execute/evaluate its body and return the result.
  * If no branch matches and there is no `else` (which shouldn't happen for exhaustive checks), throw a runtime panic.

---

### 4.5 Virtual Machine & Compiler (PinoVM)

* Emit compilation code for `MatchExpression` by generating conditional branch jumps.
* For arrow branches, compile the expression directly onto the stack.
* For yield blocks, compile the block statement. The compiler will compile the `yield` expression and then jump to the end of the match block, leaving the yielded value on top of the stack.
* Ensure all branch execution paths terminate by jumping to the same instruction offset representing the end of the match expression, keeping stack top clean and correctly populated.

---

## 5. Implementation Details

All components of Match Expressions have been fully designed, implemented, and verified in the Pino codebase:

* **AST Integration**:
  * Instead of a complete rename to avoid code churn, `MatchStatement` was changed to inherit from `Expression` (which itself inherits from `Statement`). This allows the node to act as a first-class expression while remaining fully backwards-compatible as a statement.

* **Parser & Syntax Support**:
  * `IsExpression` and `ParsePrimaryExpression` in [Parser.Expression.cs](../pino-csharp/Parser/Parser.Expression.cs) were updated to recognize `match` as an expression.
  * `ParseWhenStatement` and `ParseElseStatement` in [Parser.Statement.cs](../pino-csharp/Parser/Parser.Statement.cs) were updated to consume the arrow `=>` operator followed by a single expression, or a brace `{` followed by a block.

* **Static Type Checking & Validation**:
  * Cleanly decoupled variable resolving/scope tracking from type inference:
    * `CheckExpression(MatchStatement)` in [Checker.Expression.cs](../pino-csharp/Checker/Checker.Expression.cs) checks the condition, patterns, and branch bodies under the correct scope hierarchy, ensuring correct runtime distance resolution of parameters/variables.
    * `InferTypeInternal(MatchStatement)` infers the unified return type of the match and checks pattern exhaustiveness for both unions and enums.
  * In block-yield branches, `_currentYieldType` is bound during verification to ensure type safety of the `yield` statement.
  * Monomorphization of match expressions was implemented in `SubstituteExpressionTypes` in [Checker.Monomorphization.cs](../pino-csharp/Checker/Checker.Monomorphization.cs).

* **Runtime Evaluator**:
  * Inside [Evaluator.Expression.cs](../pino-csharp/Evaluator/Evaluator.Expression.cs), `EvaluateMatch` handles evaluating the condition, matching variant patterns, binding pattern variables inside a nested environment, and evaluating branch bodies.
  * For block-yield branches, the block is executed, catching `PinoYieldException` to retrieve and return the yielded value.
  * Statement-level match execution in [Evaluator.Statement.cs](../pino-csharp/Evaluator/Evaluator.Statement.cs) is unified to delegate directly to `EvaluateMatch`.
