# RFC 008b: C Transpilation Backend, TCC Integration, and Bootstrapping Roadmap

* **Status**: Draft / Proposal
* **Author**: Antigravity & OGShawnLee
* **Created**: July 5, 2026

---

## 1. Introduction & Vision

This RFC proposes **Path B** for the native compilation of Pino: **Transpiling Pino to standard C code** and compiling it using **TCC (Tiny C Compiler)** for development and standard compilers (**GCC/Clang**) for production. 

This alternative path simplifies compiler backend development by 90% compared to direct LLVM IR emission, keeps installation footprints under 5MB (fully self-contained), and achieves near-instant compilation speeds while outputting high-performance native executables.

---

## 2. C Transpilation Architecture

Instead of compiling to bytecode or generating raw LLVM assembler blocks, the compiler translates the Pino AST directly into standard C code (`.c` files).

```
┌──────────┐      ┌──────────────┐      ┌────────────────┐      ┌────────────┐
│ Pino AST │ ───> │ C Source     │ ───> │ TCC (Local)    │ ───> │ Native App │
│          │      │ (.c file)    │      │ Clang/GCC (Prod)│      │   (.exe)   │
└──────────┘      └──────────────┘      └────────────────┘      └────────────┘
```

### Direct AST-to-C vs. TACKY Intermediate Lowering
By compiling to C, Pino can leverage the C compiler's native optimizations and control structures:
* **Native Control Flow & Short-Circuiting**: Operators like `&&` and `||`, as well as loops (`for`, `while`) and conditionals (`if/else`), do not need to be flattened into custom jumps and labels by the Pino compiler. They can be transpiled directly to C's native operators, delegating short-circuit execution to TCC/GCC.
* **TACKY Lowering Option**: The compiler can optionally lower the AST to a linear three-address representation (TACKY) first. This is highly useful for mapping complex abstractions (like closures capturing local scopes or fat pointers for interfaces) into explicit, step-by-step C declarations.

### Mapping Advanced Abstractions to C
1. **Tagged Unions (Sum Types)**: Map directly to a C `struct` containing an `enum` (for the discriminant tag) and a `union` (for variant payloads).
2. **Lambdas and Closures**: Map to a C `struct` holding a function pointer and a `void*` environment pointer capturing local variable data.
3. **Interfaces (Duck Typing)**: Map to a C `struct` (Fat Pointer) containing a `void*` data pointer and a `void**` VTable pointer mapping struct method offsets.
4. **Order-Independent Tuples**: Map to standard anonymous C structs, with destructuring mapped to local assignments.

---

## 3. Memory Management: C + Boehm GC

Pino will link the **Boehm-Demers-Weiser conservative Garbage Collector** into the generated C code.

### Advantages of C + Boehm GC
* **Automatic Garbage Collection**: Eliminates manual `free()` management. All dynamic allocations use `GC_MALLOC(size)`.
* **Zero Compiler Complexity**: No need to generate metadata, stack maps, or reference-counting increments/decrements (`retain`/`release`).
* **High Performance**: Thread-local allocation buffers ensure extremely fast heap allocations, with negligible pauses ($1 - 5\text{ms}$) for data pipelines and CLI scripts.

---

## 4. Runtime & Standard Library Management

A hybrid runtime model will be used:

1. **Runtime Library (`runtime.c`)**: Written in C, providing low-level wrappers to standard `libc` functions (`printf`, `fgets`, `fopen`) and Boehm GC allocation wrappers.
2. **Pino Prelude (`prelude.pino`)**: High-level modules written in Pino (string manipulation, vector functional methods like `map` and `filter`) that are compiled inline with the user's program.

---

## 5. Bootstrapping / Self-Hosting Roadmap

The self-hosting compilation pipeline will proceed in three phases:

### Phase 1: Python Compiler (C0)
* **Goal**: Write a simple frontend (lexer, parser, type checker) and a C code generator in Python.
* **Scope**: Compiles a minimal subset of Pino ("Pino-Core": functions, structs, arrays, loops, basic types).
* **Execution**: Translates Pino-Core files into C code and compiles them using a bundled TCC binary.

### Phase 2: Self-Hosted Compiler (C1)
* **Goal**: Write the complete Pino compiler *in Pino itself*.
* **Compilation**: Compile the Pino compiler source code using the Python-based Compiler (C0) to generate the C source code, then compile it with TCC.
* **Output**: A native, highly-optimized executable of the Pino compiler (`pino` binary, version 1).

### Phase 3: Pure Self-Hosting (C2)
* **Goal**: Discard the Python compiler.
* **Execution**: Use the native Pino compiler (C1) to translate subsequent versions of the compiler source code into C and compile them.
* **Result**: Complete bootstrapping achieved. The compiler is now 100% native and self-supported in Pino.
