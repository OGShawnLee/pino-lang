# RFC 008a: LLVM Native Backend, Boehm GC, and Bootstrapping Roadmap

* **Status**: Draft / Proposal
* **Author**: Antigravity & OGShawnLee
* **Created**: July 5, 2026

---

## 1. Introduction & Vision

This RFC proposes **Path A** for the native compilation of Pino: **Compiling Pino directly to machine code using LLVM**. 

This path positions Pino as a classic, high-performance systems programming language. By generating LLVM Intermediate Representation (LLVM IR) directly, Pino gains access to the industry's most advanced static compilation optimizations, native multi-platform target generation, and zero startup latency.

---

## 2. LLVM Native Backend Architecture

Instead of compiling to VM bytecode, the compiler compiles the Pino AST into text-based **LLVM Intermediate Representation (LLVM IR)**.

```
┌──────────┐      ┌──────────┐      ┌─────────┐      ┌─────────┐      ┌────────────┐
│ Pino AST │ ───> │ Pino IR  │ ───> │ LLVM IR │ ───> │ Assembly│ ───> │ Native App │
│          │      │ (Tacky)  │      │  (.ll)  │      │  (.s)   │      │   (.exe)   │
└──────────┘      └──────────┘      └─────────┘      └─────────┘      └────────────┘
```

### SSA Form & The Stack Allocation (`alloca`) Strategy
LLVM IR requires Static Single Assignment (SSA) form, meaning virtual registers (e.g. `%1`) can only be assigned once. To avoid complex SSA calculation algorithms (like $\phi$-nodes) for mutable variables (`var`), the compiler will use the stack memory allocation pattern:
1. **Declare local variables** in stack space via `alloca`.
2. **Access and modify** variables via `load` and `store`.
3. **Optimize**: Rely on LLVM's `mem2reg` optimization pass to automatically lift these stack structures into CPU registers at compile time, achieving native speed with minimal compiler complexity.

---

## 3. Memory Management: Conservative GC (Boehm GC)

To avoid the development complexity of Automatic Reference Counting (ARC) or static lifetime analysis (borrow checking) in Phase 1, Pino will use the **Boehm-Demers-Weiser conservative Garbage Collector**.

### Why Boehm GC?
* **Zero Compiler Complexity**: No need to generate metadata, stack maps, or reference-counting increments/decrements (`retain`/`release`) in the compiler backend.
* **Safety in 64-bit Systems**: The vast virtual address space of 64-bit platforms makes random integer-to-pointer collisions (false pointers) negligible, preventing memory leaks.
* **Low Overhead**: Fast allocation via thread-local allocation buffers, with a typical CPU overhead of just $2\% - 5\%$ for CLI and data processing scripts.

---

## 4. Runtime & Standard Library Management

Without the .NET CLR, Pino will build a hybrid Runtime and Standard Library using a two-layer approach:

```
┌────────────────────────────────────────────────────────┐
│             Pino Prelude (prelude.pino)                │ <- High-level functional utilities (map, trim, filter)
├────────────────────────────────────────────────────────┤
│           Runtime C Library (runtime.c -> .o)          │ <- Bridging functions (malloc, prints, file I/O)
├────────────────────────────────────────────────────────┤
│              Operating System C Library (libc)         │ <- Low-level system bindings (printf, malloc)
└────────────────────────────────────────────────────────┘
```

### 1. The C Runtime Layer (`runtime.c`)
A lightweight helper library written in C. It exposes direct wrappers to native OS calls and `libc` functions:
* **Memory Allocation**: `pino_malloc(size)` wrapping `GC_MALLOC(size)`.
* **Basic Console I/O**: `pino_print_str(s)` mapping to `printf("%s\n")` and `pino_readline()` wrapping `fgets`.
* **Utilities**: System time (`time`), thread sleep (`sleep`), and random number generators.

This file is compiled once to `runtime.o` (or `runtime.lib` / `.a`) and linked statically into every compiled Pino executable.

### 2. The Pino Prelude (`prelude.pino`)
High-level structures and algorithms written in **Pino itself**. They are compiled inline with the user's program:
* Vector operations (`map`, `filter`, `each`, `push`, `pop`).
* String manipulation (`lower`, `upper`, `trim`, `split`).
* Built-in collections (e.g., hash maps).

---

## 5. Self-Hosting Bootstrapping Roadmap

To compile Pino using Pino, the bootstrapping process will proceed in three distinct phases:

### Phase 1: The Python Compiler (C0)
* **Goal**: Rapidly write a basic parser, checker, and LLVM IR generator in Python.
* **Scope**: Compiles a minimal subset of Pino ("Pino-Core": functions, structs, arrays, loops, basic types).
* **Tools**: Use `llvmlite` bindings in Python to build and emit LLVM IR.

### Phase 2: The Self-Hosted Compiler (C1)
* **Goal**: Write the complete Pino compiler *in Pino itself*, utilizing the syntax and types we want to support.
* **Compilation**: Use the Python-based Compiler (C0) to compile the Pino compiler source code.
* **Output**: A native, highly-optimized executable of the Pino compiler (`pino` binary, version 1).

### Phase 3: Pure Self-Hosting (C2)
* **Goal**: Discard the Python compiler.
* **Execution**: Use the native Pino compiler (C1) to compile subsequent versions of itself (C2, C3, etc.).
* **Result**: Complete bootstrapping achieved. The compiler is now 100% native and self-supported in Pino.
