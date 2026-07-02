using System;
using System.Collections.Generic;
using System.Linq;

namespace Pino;

public class Compiler {
  private class Local {
    public string Name { get; }
    public int Depth { get; }
    public Local(string name, int depth) {
      Name = name;
      Depth = depth;
    }
  }

  private class CompilerState {
    public CompilerState? Parent;
    public List<Local> Locals = new();
    public int ScopeDepth = 0;
    public Chunk Chunk = new();
    public FunctionDeclaration? Function;

    public CompilerState(CompilerState? parent, FunctionDeclaration? function) {
      Parent = parent;
      Function = function;
      // Slot 0 is reserved for local function self-reference/closures
      Locals.Add(new Local(function?.Identifier ?? "", 0));
    }
  }

  private CompilerState _state = new(null, null);

  public PinoVMFunction Compile(ProgramStatement program) {
    foreach (var stmt in program.Statements) {
      CompileStatement(stmt);
    }
    EmitByte((byte)OperationCode.OP_NIL);
    EmitByte((byte)OperationCode.OP_RETURN);
    return new PinoVMFunction("script", 0, _state.Chunk);
  }

  private void CompileStatement(Statement stmt) {
    switch (stmt) {
      case VariableDeclaration varDecl:
        CompileVariableDeclaration(varDecl);
        break;

      case FunctionDeclaration fnDecl:
        CompileFunctionDeclaration(fnDecl);
        break;

      case ReturnStatement ret:
        if (ret.Argument != null) {
          CompileExpression(ret.Argument);
        } else {
          EmitByte((byte)OperationCode.OP_NIL);
        }
        EmitByte((byte)OperationCode.OP_RETURN);
        break;

      case YieldStatement yield:
        throw new Exception("PinoVM compiler: 'yield' is not supported in bytecode compilation yet.");

      case IfStatement ifs:
        CompileIfStatement(ifs);
        break;

      case ElseStatement elseStmt:
        CompileStatement(elseStmt.Body);
        break;

      case LoopStatement loop:
        CompileLoopStatement(loop);
        break;

      case BlockStatement block:
        BeginScope();
        foreach (var s in block.Statements) {
          CompileStatement(s);
        }
        EndScope();
        break;

      case Expression expr:
        CompileExpression(expr);
        EmitByte((byte)OperationCode.OP_POP); // Clean expression result from stack
        break;

      default:
        // Ignore or treat as nop for VM fallback
        break;
    }
  }

  private void CompileVariableDeclaration(VariableDeclaration varDecl) {
    if (varDecl.Value != null) {
      CompileExpression(varDecl.Value);
    } else {
      EmitByte((byte)OperationCode.OP_NIL);
    }

    if (_state.ScopeDepth > 0) {
      // Local variable
      _state.Locals.Add(new Local(varDecl.Identifier, _state.ScopeDepth));
    } else {
      // Global variable
      ushort nameIdx = (ushort)AddConstant(varDecl.Identifier);
      EmitByte((byte)OperationCode.OP_DEFINE_GLOBAL);
      EmitShort(nameIdx);
    }
  }

  private void CompileFunctionDeclaration(FunctionDeclaration fnDecl) {
    // 1. Compile the function body in a nested compiler state
    var priorState = _state;
    _state = new CompilerState(priorState, fnDecl);
    
    // Add parameters as locals
    foreach (var param in fnDecl.Parameters) {
      _state.Locals.Add(new Local(param.Identifier, 1));
    }
    _state.ScopeDepth = 1;

    if (fnDecl.Body != null) {
      CompileStatement(fnDecl.Body);
    }

    // Implicit return nil if not returned
    EmitByte((byte)OperationCode.OP_NIL);
    EmitByte((byte)OperationCode.OP_RETURN);

    var compiledChunk = _state.Chunk;
    var fn = new PinoVMFunction(fnDecl.Identifier, fnDecl.Parameters.Count, compiledChunk);
    
    _state = priorState;

    // 2. Define function in scope
    if (_state.ScopeDepth > 0) {
      ushort constIdx = (ushort)AddConstant(fn);
      EmitByte((byte)OperationCode.OP_CONSTANT);
      EmitShort(constIdx);
      _state.Locals.Add(new Local(fnDecl.Identifier, _state.ScopeDepth));
    } else {
      ushort constIdx = (ushort)AddConstant(fn);
      EmitByte((byte)OperationCode.OP_CONSTANT);
      EmitShort(constIdx);
      ushort nameIdx = (ushort)AddConstant(fnDecl.Identifier);
      EmitByte((byte)OperationCode.OP_DEFINE_GLOBAL);
      EmitShort(nameIdx);
    }
  }

  private void CompileIfStatement(IfStatement ifs) {
    CompileExpression(ifs.Condition);

    int jumpOffset = EmitJump((byte)OperationCode.OP_JUMP_IF_FALSE);
    EmitByte((byte)OperationCode.OP_POP); // Pop condition

    CompileStatement(ifs.Consequent);

    int elseJumpOffset = EmitJump((byte)OperationCode.OP_JUMP);

    PatchJump(jumpOffset);
    EmitByte((byte)OperationCode.OP_POP); // Pop condition

    if (ifs.Alternate != null) {
      CompileStatement(ifs.Alternate);
    }

    PatchJump(elseJumpOffset);
  }

  private void CompileLoopStatement(LoopStatement loop) {
    if (loop.Kind == LoopKind.ForIn) {
      string colType = loop.End?.InferredType ?? "any";

      // 1. Evaluate collection/range limit
      CompileExpression(loop.End!);

      // Save collection as hidden local variable
      int collectionSlot = _state.Locals.Count;
      _state.Locals.Add(new Local("<collection>", _state.ScopeDepth));

      // 2. Get collection length and save as limit
      int limitSlot = _state.Locals.Count;
      if (colType == "string") {
        EmitByte((byte)OperationCode.OP_GET_LOCAL);
        EmitByte((byte)collectionSlot);
        EmitByte((byte)OperationCode.OP_STRING_LEN);
      } else if (colType.StartsWith("[]")) {
        EmitByte((byte)OperationCode.OP_GET_LOCAL);
        EmitByte((byte)collectionSlot);
        EmitByte((byte)OperationCode.OP_LIST_LEN);
      } else {
        // Range loop: the collection itself is the limit (integer)
        EmitByte((byte)OperationCode.OP_GET_LOCAL);
        EmitByte((byte)collectionSlot);
      }
      _state.Locals.Add(new Local("<limit>", _state.ScopeDepth));

      // 3. Initialize counter to 0L
      int counterSlot = _state.Locals.Count;
      EmitByte((byte)OperationCode.OP_CONSTANT);
      EmitShort((ushort)AddConstant(0L));
      _state.Locals.Add(new Local("<counter>", _state.ScopeDepth));

      int startOffset = _state.Chunk.Code.Count;

      // Compare: counter < limit
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)counterSlot);
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)limitSlot);
      EmitByte((byte)OperationCode.OP_LESS);

      int exitJump = EmitJump((byte)OperationCode.OP_JUMP_IF_FALSE);
      EmitByte((byte)OperationCode.OP_POP); // Pop comparison result

      // Loop body scope
      BeginScope();

      // If double variable loop (e.g. for idx, v in collection)
      if (!string.IsNullOrEmpty(loop.KeyVar)) {
        // The index is the counter
        EmitByte((byte)OperationCode.OP_GET_LOCAL);
        EmitByte((byte)counterSlot);
        _state.Locals.Add(new Local(loop.KeyVar, _state.ScopeDepth));
      }

      // User loop variable (value at index)
      var id = loop.Begin as IdentifierExpression;
      if (id != null) {
        if (colType == "string") {
          EmitByte((byte)OperationCode.OP_GET_LOCAL);
          EmitByte((byte)collectionSlot);
          EmitByte((byte)OperationCode.OP_GET_LOCAL);
          EmitByte((byte)counterSlot);
          EmitByte((byte)OperationCode.OP_STRING_GET_INDEX);
        } else if (colType.StartsWith("[]")) {
          EmitByte((byte)OperationCode.OP_GET_LOCAL);
          EmitByte((byte)collectionSlot);
          EmitByte((byte)OperationCode.OP_GET_LOCAL);
          EmitByte((byte)counterSlot);
          EmitByte((byte)OperationCode.OP_LIST_GET_INDEX);
        } else {
          // Range loop: value is the counter itself
          EmitByte((byte)OperationCode.OP_GET_LOCAL);
          EmitByte((byte)counterSlot);
        }
        _state.Locals.Add(new Local(id.Name, _state.ScopeDepth));
      }

      CompileStatement(loop.Body);
      EndScope();

      // Increment counter: counter += 1
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)counterSlot);
      EmitByte((byte)OperationCode.OP_CONSTANT);
      EmitShort((ushort)AddConstant(1L));
      EmitByte((byte)OperationCode.OP_ADD);
      EmitByte((byte)OperationCode.OP_SET_LOCAL);
      EmitByte((byte)counterSlot);
      EmitByte((byte)OperationCode.OP_POP); // Pop set result

      // Loop back
      EmitLoop(startOffset);

      PatchJump(exitJump);
      EmitByte((byte)OperationCode.OP_POP); // Pop comparison result

      // Pop hidden collection, limit and counter from VM stack
      EmitByte((byte)OperationCode.OP_POP);
      EmitByte((byte)OperationCode.OP_POP);
      EmitByte((byte)OperationCode.OP_POP);
      _state.Locals.RemoveRange(collectionSlot, 3);
    } else if (loop.Kind == LoopKind.ForTimes) {
      // Loop N times
      CompileExpression(loop.Begin!);

      int limitSlot = _state.Locals.Count;
      _state.Locals.Add(new Local("<limit>", _state.ScopeDepth));
      
      int counterSlot = _state.Locals.Count;
      EmitByte((byte)OperationCode.OP_CONSTANT);
      EmitShort((ushort)AddConstant(0L));
      _state.Locals.Add(new Local("<counter>", _state.ScopeDepth));

      int startOffset = _state.Chunk.Code.Count;

      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)counterSlot);
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)limitSlot);
      EmitByte((byte)OperationCode.OP_LESS);

      int exitJump = EmitJump((byte)OperationCode.OP_JUMP_IF_FALSE);
      EmitByte((byte)OperationCode.OP_POP);

      BeginScope();
      _state.Locals.Add(new Local("it", _state.ScopeDepth));
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)counterSlot);

      CompileStatement(loop.Body);
      EndScope();

      // Increment
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)counterSlot);
      EmitByte((byte)OperationCode.OP_CONSTANT);
      EmitShort((ushort)AddConstant(1L));
      EmitByte((byte)OperationCode.OP_ADD);
      EmitByte((byte)OperationCode.OP_SET_LOCAL);
      EmitByte((byte)counterSlot);
      EmitByte((byte)OperationCode.OP_POP);

      EmitLoop(startOffset);

      PatchJump(exitJump);
      EmitByte((byte)OperationCode.OP_POP);
      EmitByte((byte)OperationCode.OP_POP);
      EmitByte((byte)OperationCode.OP_POP);
      _state.Locals.RemoveRange(limitSlot, 2);
    } else if (loop.Kind == LoopKind.While) {
      int startOffset = _state.Chunk.Code.Count;
      CompileExpression(loop.Begin!);
      int exitJump = EmitJump((byte)OperationCode.OP_JUMP_IF_FALSE);
      EmitByte((byte)OperationCode.OP_POP); // pop comparison result if true

      BeginScope();
      CompileStatement(loop.Body);
      EndScope();

      EmitLoop(startOffset);

      PatchJump(exitJump);
      EmitByte((byte)OperationCode.OP_POP); // pop comparison result if false
    } else if (loop.Kind == LoopKind.Infinite) {
      int startOffset = _state.Chunk.Code.Count;
      BeginScope();
      CompileStatement(loop.Body);
      EndScope();
      EmitLoop(startOffset);
    }
  }

  private void CompileExpression(Expression expr) {
    switch (expr) {
      case LiteralExpression lit:
        CompileLiteral(lit);
        break;

      case IdentifierExpression id:
        CompileIdentifier(id);
        break;

      case UnaryExpression un:
        if (un.Operator == OperatorType.Not) {
          CompileExpression(un.Right);
          EmitByte((byte)OperationCode.OP_NOT);
        } else if (un.Operator == OperatorType.Subtraction) {
          if (un.Right.InferredType == "int") {
            ushort constIdx = (ushort)AddConstant(0L);
            EmitByte((byte)OperationCode.OP_CONSTANT);
            EmitShort(constIdx);
            CompileExpression(un.Right);
            EmitByte((byte)OperationCode.OP_SUB_INT);
          } else {
            ushort constIdx = (ushort)AddConstant(0.0);
            EmitByte((byte)OperationCode.OP_CONSTANT);
            EmitShort(constIdx);
            CompileExpression(un.Right);
            EmitByte((byte)OperationCode.OP_SUB);
          }
        }
        break;

      case BinaryExpression bin:
        CompileBinaryExpression(bin);
        break;

      case FunctionCallExpression call:
        CompileFunctionCallExpression(call);
        break;

      case TernaryExpression tern:
        CompileExpression(tern.Condition);
        int jumpOffset = EmitJump((byte)OperationCode.OP_JUMP_IF_FALSE);
        EmitByte((byte)OperationCode.OP_POP);
        CompileExpression(tern.Consequent);
        int elseJump = EmitJump((byte)OperationCode.OP_JUMP);
        PatchJump(jumpOffset);
        EmitByte((byte)OperationCode.OP_POP);
        CompileExpression(tern.Alternate);
        PatchJump(elseJump);
        break;

      case BubbleExpression bub:
        throw new Exception("PinoVM compiler: '?' operator is not supported in bytecode compilation yet.");

      case RecoveryExpression rec:
        throw new Exception("PinoVM compiler: 'or' recovery block is not supported in bytecode compilation yet.");

      default:
        // Emit nil fallback
        EmitByte((byte)OperationCode.OP_NIL);
        break;
    }
  }

  private void CompileLiteral(LiteralExpression lit) {
    switch (lit.LiteralType) {
      case LiteralType.Boolean:
        if (bool.Parse(lit.Value)) {
          EmitByte((byte)OperationCode.OP_TRUE);
        } else {
          EmitByte((byte)OperationCode.OP_FALSE);
        }
        break;
      case LiteralType.Integer:
        long iVal = long.Parse(lit.Value.Replace("_", ""));
        EmitByte((byte)OperationCode.OP_CONSTANT);
        EmitShort((ushort)AddConstant(iVal));
        break;
      case LiteralType.Float:
        double fVal = double.Parse(lit.Value.Replace("_", ""), System.Globalization.CultureInfo.InvariantCulture);
        EmitByte((byte)OperationCode.OP_CONSTANT);
        EmitShort((ushort)AddConstant(fVal));
        break;
      case LiteralType.String:
        EmitByte((byte)OperationCode.OP_CONSTANT);
        EmitShort((ushort)AddConstant(lit.Value));
        break;
      case LiteralType.Rune:
        int cp = int.Parse(lit.Value);
        EmitByte((byte)OperationCode.OP_CONSTANT);
        EmitShort((ushort)AddConstant(new PinoRune(cp)));
        break;
      default:
        EmitByte((byte)OperationCode.OP_NIL);
        break;
    }
  }

  private void CompileIdentifier(IdentifierExpression id) {
    int localIdx = ResolveLocal(id.Name);
    if (localIdx != -1) {
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)localIdx);
    } else {
      ushort nameIdx = (ushort)AddConstant(id.Name);
      EmitByte((byte)OperationCode.OP_GET_GLOBAL);
      EmitShort(nameIdx);
    }
  }

  private void CompileBinaryExpression(BinaryExpression bin) {
    if (bin.Operator == OperatorType.Assignment) {
      var idExpr = bin.Left as IdentifierExpression;
      if (idExpr != null) {
        CompileExpression(bin.Right);
        int localIdx = ResolveLocal(idExpr.Name);
        if (localIdx != -1) {
          EmitByte((byte)OperationCode.OP_SET_LOCAL);
          EmitByte((byte)localIdx);
        } else {
          ushort nameIdx = (ushort)AddConstant(idExpr.Name);
          EmitByte((byte)OperationCode.OP_SET_GLOBAL);
          EmitShort(nameIdx);
        }
      }
      return;
    }

    if (bin.Operator == OperatorType.AdditionAssignment ||
        bin.Operator == OperatorType.SubtractionAssignment ||
        bin.Operator == OperatorType.MultiplicationAssignment ||
        bin.Operator == OperatorType.DivisionAssignment) {
      var idExpr = bin.Left as IdentifierExpression;
      if (idExpr != null) {
        bool isIntOp = idExpr.InferredType == "int" && bin.Right.InferredType == "int";
        // Load original value
        CompileIdentifier(idExpr);
        // Load delta
        CompileExpression(bin.Right);
        // Emit operation
        OperationCode op = bin.Operator switch {
          OperatorType.AdditionAssignment => isIntOp ? OperationCode.OP_ADD_INT : OperationCode.OP_ADD,
          OperatorType.SubtractionAssignment => isIntOp ? OperationCode.OP_SUB_INT : OperationCode.OP_SUB,
          OperatorType.MultiplicationAssignment => isIntOp ? OperationCode.OP_MUL_INT : OperationCode.OP_MUL,
          OperatorType.DivisionAssignment => isIntOp ? OperationCode.OP_DIV_INT : OperationCode.OP_DIV,
          _ => throw new NotImplementedException()
        };
        EmitByte((byte)op);

        // Store back
        int localIdx = ResolveLocal(idExpr.Name);
        if (localIdx != -1) {
          EmitByte((byte)OperationCode.OP_SET_LOCAL);
          EmitByte((byte)localIdx);
        } else {
          ushort nameIdx = (ushort)AddConstant(idExpr.Name);
          EmitByte((byte)OperationCode.OP_SET_GLOBAL);
          EmitShort(nameIdx);
        }
      }
      return;
    }
    
    if (bin.Operator == OperatorType.And) {
      CompileExpression(bin.Left);
      int endJump = EmitJump((byte)OperationCode.OP_JUMP_IF_FALSE);
      EmitByte((byte)OperationCode.OP_POP);
      CompileExpression(bin.Right);
      PatchJump(endJump);
      return;
    }

    if (bin.Operator == OperatorType.Or) {
      CompileExpression(bin.Left);
      int rightBranchJump = EmitJump((byte)OperationCode.OP_JUMP_IF_FALSE);
      int endJump = EmitJump((byte)OperationCode.OP_JUMP);
      PatchJump(rightBranchJump);
      EmitByte((byte)OperationCode.OP_POP);
      CompileExpression(bin.Right);
      PatchJump(endJump);
      return;
    }

    bool isInt = bin.Left.InferredType == "int" && bin.Right.InferredType == "int";

    CompileExpression(bin.Left);
    CompileExpression(bin.Right);

    switch (bin.Operator) {
      case OperatorType.Addition: EmitByte((byte)(isInt ? OperationCode.OP_ADD_INT : OperationCode.OP_ADD)); break;
      case OperatorType.Subtraction: EmitByte((byte)(isInt ? OperationCode.OP_SUB_INT : OperationCode.OP_SUB)); break;
      case OperatorType.Multiplication: EmitByte((byte)(isInt ? OperationCode.OP_MUL_INT : OperationCode.OP_MUL)); break;
      case OperatorType.Division: EmitByte((byte)(isInt ? OperationCode.OP_DIV_INT : OperationCode.OP_DIV)); break;
      case OperatorType.Modulus: EmitByte((byte)(isInt ? OperationCode.OP_MOD_INT : OperationCode.OP_MOD)); break;
      case OperatorType.Equal: EmitByte((byte)(isInt ? OperationCode.OP_EQUAL_INT : OperationCode.OP_EQUAL)); break;
      case OperatorType.NotEqual: EmitByte((byte)OperationCode.OP_NOT_EQUAL); break;
      case OperatorType.LessThan: EmitByte((byte)(isInt ? OperationCode.OP_LESS_INT : OperationCode.OP_LESS)); break;
      case OperatorType.LessThanEqual: EmitByte((byte)(isInt ? OperationCode.OP_LESS_EQUAL_INT : OperationCode.OP_LESS_EQUAL)); break;
      case OperatorType.GreaterThan: EmitByte((byte)(isInt ? OperationCode.OP_GREATER_INT : OperationCode.OP_GREATER)); break;
      case OperatorType.GreaterThanEqual: EmitByte((byte)(isInt ? OperationCode.OP_GREATER_EQUAL_INT : OperationCode.OP_GREATER_EQUAL)); break;
    }
  }

  private void CompileFunctionCallExpression(FunctionCallExpression call) {
    // 1. Look up callee (can be local or global)
    int localIdx = ResolveLocal(call.Callee);
    if (localIdx != -1) {
      EmitByte((byte)OperationCode.OP_GET_LOCAL);
      EmitByte((byte)localIdx);
    } else {
      ushort nameIdx = (ushort)AddConstant(call.Callee);
      EmitByte((byte)OperationCode.OP_GET_GLOBAL);
      EmitShort(nameIdx);
    }

    // 2. Compile arguments
    foreach (var arg in call.Arguments) {
      CompileExpression(arg);
    }

    // 3. Emit Call instruction
    EmitByte((byte)OperationCode.OP_CALL);
    EmitByte((byte)call.Arguments.Count);
  }

  private int ResolveLocal(string name) {
    for (int i = _state.Locals.Count - 1; i >= 0; i--) {
      if (_state.Locals[i].Name == name) {
        return i;
      }
    }
    return -1;
  }

  private void BeginScope() {
    _state.ScopeDepth++;
  }

  private void EndScope() {
    _state.ScopeDepth--;
    // Pop local variables belonging to this depth
    int count = 0;
    for (int i = _state.Locals.Count - 1; i >= 0; i--) {
      if (_state.Locals[i].Depth > _state.ScopeDepth) {
        count++;
      } else {
        break;
      }
    }
    if (count > 0) {
      for (int i = 0; i < count; i++) {
        EmitByte((byte)OperationCode.OP_POP);
      }
      _state.Locals.RemoveRange(_state.Locals.Count - count, count);
    }
  }

  private void EmitByte(byte b) {
    _state.Chunk.Write(b);
  }

  private void EmitShort(ushort val) {
    _state.Chunk.WriteShort(val);
  }

  private int AddConstant(object? val) {
    return _state.Chunk.AddConstant(val);
  }

  private int EmitJump(byte instruction) {
    EmitByte(instruction);
    EmitByte(0xFF);
    EmitByte(0xFF);
    return _state.Chunk.Code.Count - 2;
  }

  private void PatchJump(int offset) {
    int jumpLen = _state.Chunk.Code.Count - offset - 2;
    if (jumpLen > ushort.MaxValue) {
      throw new Exception("COMPILER ERROR: Jump limit exceeded.");
    }
    _state.Chunk.Code[offset] = (byte)((jumpLen >> 8) & 0xFF);
    _state.Chunk.Code[offset + 1] = (byte)(jumpLen & 0xFF);
  }

  private void EmitLoop(int loopStart) {
    EmitByte((byte)OperationCode.OP_JUMP);
    int offset = _state.Chunk.Code.Count + 2 - loopStart;
    if (offset > ushort.MaxValue) {
      throw new Exception("COMPILER ERROR: Loop limit exceeded.");
    }
    // We emit negative offset by subtracting it or just storing it.
    // For simplicity, we can store backward jump offset as absolute or relative.
    // Let's store relative offset (backwards). To do that:
    ushort backwardJump = (ushort)(_state.Chunk.Code.Count + 2 - loopStart);
    // Since we compile it backwards, we mark it as negative or split it:
    // Let's support backward jump in OP_JUMP by checking if offset is negative.
    // Actually, in the VM we can read it, and if it's OP_JUMP, we jump to:
    // ip = ip + offset - 3 (if offset is signed) or we can have a dedicated OP_LOOP instruction!
    // OP_LOOP takes a 16-bit offset and subtracts it from ip. That is extremely clean and avoids negative number bugs!
    // Let's define OP_LOOP or use OP_JUMP with signed. Actually, a dedicated OP_JUMP_BACK (or OP_LOOP) is the cleanest.
    // Wait, let's just make OP_JUMP support relative offset in VM. Or let's implement OP_JUMP with a signed 16-bit short!
    // Yes! If we use a signed 16-bit short for jump offsets, we can jump forward (positive) and backward (negative).
    // Let's check:
    // Offset for forward jump: chunk.Code.Count - offset - 2 (always positive)
    // Offset for backward jump: loopStart - (chunk.Code.Count + 2) (always negative)
    // This is perfect! A signed short (Int16) ranges from -32768 to 32767, which is plenty for any script!
    // Let's implement signed 16-bit offset patching!
    // Forward jump patch:
    // int jumpLen = Code.Count - offset - 2;
    // short sJumpLen = (short)jumpLen;
    // Backward jump:
    // int jumpLen = loopStart - (Code.Count + 2); // negative
    // short sJumpLen = (short)jumpLen;
    
    // Let's write the backward jump:
    int jumpLen = loopStart - (_state.Chunk.Code.Count + 2);
    short sJumpLen = (short)jumpLen;
    EmitShort((ushort)sJumpLen);
  }
}
