# RFC 007: Labeled Tuples in Pino

* **Status**: Draft
* **Authors**: Antigravity & OGShawnLee
* **Date**: 2026-07-03

---

## 1. Summary

This RFC proposes adding **labeled tuples** to Pino as a first-class, anonymous compound type that allows functions to return multiple named values without requiring a full struct declaration. The design draws inspiration from Swift's labeled tuple system, balancing explicitness and ergonomics.

---

## 2. Motivation

Currently, when a Pino function needs to return multiple related values, the only options are:

1. **Declare a struct**: Verbose and requires a name for a one-off data shape.
2. **Return a vector**: `[]string` loses semantic meaning (which index is which?).
3. **Inline the logic**: Forces the caller to hold all values in local variables, breaking abstraction.

This becomes a real pain point when writing internal helper functions, as encountered when implementing boolean short-circuit TACKY emission in the C compiler:

```pino
# Current: must repeat string interpolation or inline everything
val false_label = "and_false_label_$(counter[0])"
val end_label = "and_end_label_$(counter[0])"
counter[0] += 1
```

With labeled tuples, this helper can be cleanly extracted:

```pino
fn make_and_labels() (false_label string, end_label string) {
    return (
        false_label: "and_false_label_$(counter[0])"
        end_label: "and_end_label_$(counter[0])"
    )
}

val labels = make_and_labels()
instructions:push(Instruction::JumpIfZero(left_val, labels:false_label))
instructions:push(Instruction::Label(labels:end_label))
```

---

## 3. Syntax Variants

### 3.1 Labeled Tuple Type in Function Return Signature

Labeled tuples appear as the return type of a function, using parentheses with `name type` pairs:

```pino
fn divide(a int, b int) (quotient int, remainder int) {
    return (quotient: a / b, remainder: a % b)
}
```

### 3.2 Labeled Tuple Literal

The return expression uses parentheses with `key: value` pairs, consistent with struct initialization syntax:

```pino
return (
    false_label: "and_false_label_$(n)"
    end_label: "and_end_label_$(n)"
)
```

Inline form is also valid for short tuples:

```pino
return (quotient: a / b, remainder: a % b)
```

### 3.3 Field Access by Name

Fields are accessed using the same `:` member-access operator as structs:

```pino
val labels = make_and_labels()
println(labels:false_label)
println(labels:end_label)
```

### 3.4 Positional Destructuring

Labeled tuples support positional destructuring, matching Swift's behavior:

```pino
val (fl, el) = make_and_labels()
```

### 3.5 Named Destructuring (Optional Extension)

Named destructuring may be supported in a future iteration:

```pino
val (false_label, end_label) = make_and_labels()
# false_label and end_label bound by matching label name from the tuple
```

---

## 4. Detailed Design

### 4.1 AST Modifications

Introduce two new AST nodes:

* **`TupleTypeExpression`**: Represents a labeled tuple type in a function return annotation.
  ```csharp
  public record TupleTypeExpression(List<VariableDeclaration> Fields) : Expression;
  ```

* **`TupleLiteralExpression`**: Represents a labeled tuple literal value.
  ```csharp
  public record TupleLiteralExpression(List<(string Label, Expression Value)> Fields) : Expression;
  ```

* **`FunctionDeclaration`** gets an optional `TupleReturnType` field:
  ```csharp
  public List<VariableDeclaration>? TupleReturnType { get; set; }
  ```

---

### 4.2 Parser Changes

#### Return Type Parsing

In `ParseFunctionDeclaration` ([Parser.Statement.cs](../pino-csharp/Parser/Parser.Statement.cs)):

* After parsing parameters, if the next token is `(` instead of a plain identifier return type, parse a labeled tuple return type.
* Parse comma-separated `identifier type` pairs inside parentheses, producing a `List<VariableDeclaration>`.

#### Tuple Literal Parsing

In `ParsePrimaryExpression` ([Parser.Expression.cs](../pino-csharp/Parser/Parser.Expression.cs)):

* If `(` is encountered and the lookahead matches `identifier ':'`, parse as a `TupleLiteralExpression`.
* Otherwise, fall through to existing grouped expression parsing — standard parenthesized expressions remain unambiguous since they never start with `identifier ':'`.

---

### 4.3 Runtime Representation

At runtime, a labeled tuple value is represented as a `PinoTupleValue`: an ordered list of `(string Label, object? Value)` pairs, similar to a lightweight `PinoStructInstance` without a declared struct type.

```csharp
public class PinoTupleValue {
    public List<(string Label, object? Value)> Fields { get; }
    // Field access by name
    public object? Get(string name) => Fields.First(f => f.Label == name).Value;
    // Positional destructuring support
    public object? Get(int index) => Fields[index].Value;
}
```

Member access `labels:false_label` will check for `PinoTupleValue` before `PinoStructInstance` in the evaluator's member access logic.

---

### 4.4 Type Checker Validation

* **Inferred Return Type**: When a function declares a `TupleReturnType`, the checker validates that all `return` expressions in the function body produce a `TupleLiteralExpression` with matching label names and compatible value types.
* **Field Access**: Member access on a `TupleTypeExpression` is resolved by label name, producing the declared type of that field.
* **Destructuring**: Positional destructuring binds each variable to the corresponding field type in declaration order.
* **Monomorphization**: `SubstituteStatementTypes` in [Checker.Monomorphization.cs](../pino-csharp/Checker/Checker.Monomorphization.cs) must handle `TupleTypeExpression` by substituting generic type parameters inside each field type.

---

### 4.5 Evaluator Runtime Execution

* When executing a `return` containing a `TupleLiteralExpression`, evaluate each field's value expression and wrap them into a `PinoTupleValue`, then throw `PinoReturnException` with it.
* Member access (`BinaryExpression` with `OperatorType.MemberAccess`) checks if the left-hand side evaluates to `PinoTupleValue` and retrieves the field by name.
* Positional destructuring uses the `DestructuringDeclaration` path and binds values by index.

---

### 4.6 Virtual Machine & Compiler (PinoVM)

* A new `OpCode.MakeTuple(n)` instruction pops `n` values off the stack and creates a `PinoTupleValue`.
* Field access compiles to a new `OpCode.GetTupleField(label)` instruction.
* Positional destructuring compiles to `OpCode.GetTupleField(index)`.

---

## 5. Comparison with Alternatives

| Approach | Explicitness | Ergonomics | Reusability |
|---|---|---|---|
| Named struct | High | Low (boilerplate) | High |
| `[]any` vector | Low | Medium | Low |
| Labeled tuple (this RFC) | High | High | Medium |
| Positional tuple | Low | High | Low |

Labeled tuples occupy the sweet spot: **zero ceremony for one-off multi-value returns, with full semantic clarity at the call site**.

---

## 6. Open Questions (Path A)

1. **Tuple equality**: Should `(x: 1, y: 2) == (x: 1, y: 2)` be `true` by structural equality? Do labels and field order both matter?
2. **Nesting**: Should labeled tuples support nested tuple field types? e.g., `(inner: (a int, b int), name string)`.
3. **Spread operator**: Could a labeled tuple be spread into function arguments if the labels match parameter names?
   ```pino
   fn add(x int, y int) int { return x + y }
   val coords = (x: 3, y: 4)
   add(...coords) # => 7
   ```
4. **Pattern matching**: Should labeled tuples be matchable with `when`?
   ```pino
   match result {
       when (quotient: q, remainder: 0) => println("Divides evenly, quotient is $q")
       else => println("Has remainder")
   }
   ```

---

## 7. Path B: Return-Exclusive Labeled Tuples (Simpler Design)

An alternative, significantly simpler approach inspired by Go and V Lang: **labeled tuples exist only as function return types and their corresponding destructuring**. They are never stored as values, passed as arguments, nested, compared, or pattern-matched. The open questions in section 6 simply do not arise.

### 7.1 Core Principle

> A labeled tuple is a **syntax contract** between a function signature and its call site — not a runtime value.

The `()` delimiter is used consistently for everything tuple-related, making the visual grammar unambiguous:
- `{}` → struct world (initialization, and future struct destructuring)
- `()` → tuple world (return types, return literals, destructuring)

### 7.2 Syntax

**Function declaration:**
```pino
fn divide(a int, b int) (quotient int, remainder int) {
    return (quotient: a / b, remainder: a % b)
}
```

**Named destructuring — pick only what you need, ignore the rest implicitly:**
```pino
val (quotient) = divide(10, 3)                  # take only "quotient"
val (quotient, remainder) = divide(10, 3)        # take both by name
val (quotient: q) = divide(10, 3)               # rename to "q"
val (quotient: q, remainder: r) = divide(10, 3) # rename both
```

No `_` placeholder is ever needed — omitting a field name from the destructuring pattern implicitly ignores it.

**Comparison with future struct destructuring (reserved for `{}`):**
```pino
struct Point { x int, y int }
val p = Point { x: 3, y: 4 }
val { x } = p   # future struct destructuring — visually distinct from tuples
```

### 7.3 Implementation Simplicity

Because tuples never exist as runtime values:

* **No `PinoTupleValue` class needed** — the evaluator returns a `List<(string, object?)>` internally and immediately destructures it.
* **No new OpCodes for the VM** — `MakeTuple` and `GetTupleField` are not required; the compiler can unpack the return list directly onto the stack in declaration order.
* **No equality, nesting, spread, or pattern matching** to implement.
* **Parser disambiguation is trivial**: `val (...)` after `=` is always a named destructuring pattern, never ambiguous with a block.

### 7.4 Path A vs Path B Comparison

| Feature | Path A (First-class) | Path B (Return-exclusive) |
|---|---|---|
| Store tuple in variable | ✅ | ❌ |
| Pass tuple as argument | ✅ | ❌ |
| Tuple equality | ✅ (complex) | ❌ (not applicable) |
| Pattern matching | ✅ (complex) | ❌ (not applicable) |
| Named destructuring | ✅ | ✅ |
| Rename on destructure | ✅ | ✅ |
| Ignore fields implicitly | ✅ | ✅ |
| New runtime type needed | ✅ (`PinoTupleValue`) | ❌ |
| Implementation effort | High | Low |
| Scope of open questions | 4 hard questions | 0 |

### 7.5 Recommendation

Path B is the pragmatic starting point. It solves the immediate ergonomic problem (multi-value returns without struct ceremony) at a fraction of the complexity. Path A can be revisited in a future RFC if the community identifies concrete use cases that genuinely require tuples as first-class values.
