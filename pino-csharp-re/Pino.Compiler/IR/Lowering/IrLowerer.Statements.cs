using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  private void LowerBlockItems(FunctionBuilder fb, ImmutableArray<SyntaxNode> items) {
    foreach (SyntaxNode item in items) {
      if (fb.IsTerminated) {
        break;
      }

      LowerStatement(fb, item);
    }
  }

  private void LowerStatement(FunctionBuilder fb, SyntaxNode item) {
    if (fb.IsTerminated) {
      return;
    }

    switch (item) {
      case VariableDeclarationSyntax varDecl:
        LowerVariableDeclaration(fb, varDecl);
        break;

      case TupleDestructuringStatementSyntax destruct:
        LowerTupleDestructuring(fb, destruct);
        break;

      case ExpressionStatementSyntax exprStmt:
        _ = LowerExpression(fb, exprStmt.Expression);
        break;

      case BlockStatementSyntax block:
        LowerBlockItems(fb, block.Items);
        break;

      case IfStatementSyntax ifStmt:
        LowerIfStatement(fb, ifStmt);
        break;

      case ForStatementSyntax forStmt:
        LowerForStatement(fb, forStmt);
        break;

      case MatchStatementSyntax matchStmt:
        LowerMatchStatement(fb, matchStmt);
        break;

      case ReturnStatementSyntax retStmt:
        LowerReturn(fb, retStmt);
        break;

      case YieldStatementSyntax yieldStmt:
        LowerYield(fb, yieldStmt);
        break;

      case BreakStatementSyntax breakStmt:
        LowerBreak(fb, breakStmt);
        break;

      case ContinueStatementSyntax contStmt:
        LowerContinue(fb, contStmt);
        break;

      case AssertStatementSyntax assertStmt:
        LowerAssert(fb, assertStmt);
        break;

      case FunctionDeclarationSyntax:
      case StructDeclarationSyntax:
      case InterfaceDeclarationSyntax:
      case EnumDeclarationSyntax:
      case UnionDeclarationSyntax:
      case ModuleDeclarationSyntax:
      case ImportStatementSyntax:
      case FromImportStatementSyntax:
      case TestDeclarationSyntax:
        // Hoisted declarations are lowered separately; imports/module headers emit no runtime code.
        break;

      default:
        throw new InvalidOperationException($"Unsupported statement node '{item.GetType().Name}'.");
    }
  }

  private void LowerVariableDeclaration(FunctionBuilder fb, VariableDeclarationSyntax decl) {
    // VAR-5: evaluate initializer before binding enters scope.
    IrOperand? initOpt = LowerExpression(fb, decl.Initializer);
    if (fb.IsTerminated || initOpt is not { } initVal) {
      return;
    }

    if (fb.Model.GetDeclaredSymbol(decl) is not LocalSymbol localSym) {
      return;
    }

    BindLocalSymbol(fb, localSym, initVal, decl.Span);
  }

  private void LowerTupleDestructuring(FunctionBuilder fb, TupleDestructuringStatementSyntax destruct) {
    IrOperand? initOpt = LowerExpression(fb, destruct.Initializer);
    if (fb.IsTerminated || initOpt is not { } tupleVal) {
      return;
    }

    var irTuple = (IrTupleType) _types.GetType(fb.GetOperandType(tupleVal));
    foreach (DestructureFieldSyntax field in destruct.Fields) {
      if (fb.Model.GetDeclaredSymbol(field) is not LocalSymbol localSym) {
        continue;
      }

      string label = NameOf(fb.Model, field.LabelToken);
      int fieldIdx = FindTupleFieldIndex(irTuple, label);
      IrTempId extracted = fb.AddTemp(irTuple.Fields[fieldIdx].Type, localSym.IsMutable ? null : localSym.Name);
      fb.Emit(new TupleField(extracted, tupleVal, fieldIdx, field.Span));
      BindLocalSymbol(fb, localSym, extracted, field.Span);
    }
  }

  private void BindLocalSymbol(FunctionBuilder fb, LocalSymbol localSym, IrOperand initVal, SourceSpan span) {
    if (_globalIds.TryGetValue(localSym, out IrGlobalId globalId)) {
      IrTypeId gType = _module.Globals[globalId.Index].Type;
      IrTempId ptrTmp = fb.AddTemp(_types.GetPtr(gType));
      fb.Emit(new AddrGlobal(ptrTmp, globalId, span));
      fb.Emit(new StorePtr(ptrTmp, initVal, span));
      return;
    }

    if (localSym.IsMutable) {
      IrTypeId irType = InternType(localSym.Type);
      IrSlotId slot = fb.AddSlot(localSym.Name, irType, isMutSlot: true);
      fb.LocalSlots[localSym] = slot;
      fb.Emit(new StoreSlot(slot, initVal, span));
      return;
    }

    fb.LocalTemps[localSym] = initVal;
  }

  private void LowerReturn(FunctionBuilder fb, ReturnStatementSyntax retStmt) {
    if (retStmt.Expression is null) {
      fb.Terminate(new RetVoid(retStmt.Span));
      return;
    }

    IrOperand? valOpt = LowerExpression(fb, retStmt.Expression);
    if (fb.IsTerminated) {
      return;
    }

    if (valOpt is { } val) {
      fb.Terminate(new Ret(val, retStmt.Span));
    } else {
      fb.Terminate(new RetVoid(retStmt.Span));
    }
  }

  private void LowerYield(FunctionBuilder fb, YieldStatementSyntax yieldStmt) {
    if (fb.YieldStack.Count == 0) {
      throw new InvalidOperationException("'yield' encountered outside a recovery block or match arm.");
    }

    YieldTarget target = fb.YieldStack.Peek();
    IrOperand? valOpt = LowerExpression(fb, yieldStmt.Expression);
    if (fb.IsTerminated) {
      return;
    }

    if (target.ResultSlot is { } slot && valOpt is { } val) {
      fb.Emit(new StoreSlot(slot, val, yieldStmt.Span));
    }

    fb.Terminate(new Br(target.MergeBlock, yieldStmt.Span));
  }

  private void LowerBreak(FunctionBuilder fb, BreakStatementSyntax breakStmt) {
    if (fb.LoopStack.Count == 0) {
      throw new InvalidOperationException("'break' encountered outside a loop.");
    }

    fb.Terminate(new Br(fb.LoopStack.Peek().BreakBlock, breakStmt.Span));
  }

  private void LowerContinue(FunctionBuilder fb, ContinueStatementSyntax contStmt) {
    if (fb.LoopStack.Count == 0) {
      throw new InvalidOperationException("'continue' encountered outside a loop.");
    }

    fb.Terminate(new Br(fb.LoopStack.Peek().ContinueBlock, contStmt.Span));
  }

  private void LowerAssert(FunctionBuilder fb, AssertStatementSyntax assertStmt) {
    IrOperand? condOpt = LowerExpression(fb, assertStmt.Condition);
    if (fb.IsTerminated || condOpt is not { } cond) {
      return;
    }

    IrBasicBlock bbOk = fb.NewBlock();
    IrBasicBlock bbFail = fb.NewBlock();
    fb.Terminate(new BrIf(cond, bbOk.Id, bbFail.Id, assertStmt.Span));

    fb.SwitchTo(bbFail);
    IrOperand msg;
    if (assertStmt.Message is { } msgExpr) {
      IrOperand? msgOpt = LowerExpression(fb, msgExpr);
      if (fb.IsTerminated || msgOpt is not { } m) {
        fb.SwitchTo(bbOk);
        return;
      }

      msg = m;
    } else {
      msg = fb.EmitConstString("Assertion failed.", assertStmt.Span);
    }

    fb.Terminate(new Panic(msg, assertStmt.Span));
    fb.SwitchTo(bbOk);
  }

  private IrOperand? LowerAssignment(FunctionBuilder fb, AssignmentExpressionSyntax assign) {
    TokenKind op = assign.OperatorToken.Kind;
    ExpressionSyntax target = UnwrapParens(assign.Target);

    // Direct local `var` fast path: avoids redundant addr_local/store_ptr for clean TACKY IR.
    if (target is IdentifierExpressionSyntax id
        && fb.Model.GetReferencedSymbol(id) is LocalSymbol localSym
        && !IsSelfLocal(localSym)
        && !_globalIds.ContainsKey(localSym)
        && fb.LocalSlots.TryGetValue(localSym, out IrSlotId localSlot)) {
      if (op == TokenKind.Assign) {
        IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
        if (fb.IsTerminated || rhsOpt is not { } rhs) {
          return null;
        }

        fb.Emit(new StoreSlot(localSlot, rhs, assign.Span));
        return rhs;
      } else {
        IrTempId oldVal = fb.AddTemp(fb.Function.Slots[localSlot.Index].Type);
        fb.Emit(new LoadSlot(oldVal, localSlot, assign.Span));
        IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
        if (fb.IsTerminated || rhsOpt is not { } rhs) {
          return null;
        }

        IrOperand updated = EmitArithmeticOp(fb, op, oldVal, rhs, assign.Span);
        fb.Emit(new StoreSlot(localSlot, updated, assign.Span));
        return updated;
      }
    }

    // Map direct index assignment `m[k] = v` (MAP-5: inserts if key is absent, unlike MapElemAddr!).
    if (op == TokenKind.Assign
        && target is IndexExpressionSyntax mapIndex
        && fb.Model.GetType(mapIndex.Target) is MapType) {
      IrOperand? mapPtrOpt = ResolvePlaceAddr(fb, mapIndex.Target, ensureUniqueLeaf: true);
      if (fb.IsTerminated || mapPtrOpt is not { } mapPtr) {
        return null;
      }

      IrOperand? keyOpt = LowerExpression(fb, mapIndex.Index);
      if (fb.IsTerminated || keyOpt is not { } keyVal) {
        return null;
      }

      IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
      if (fb.IsTerminated || rhsOpt is not { } rhsVal) {
        return null;
      }

      fb.Emit(new MapSet(mapPtr, keyVal, rhsVal, assign.Span));
      return rhsVal;
    }

    // General place path (VAR-4, D-182): evaluate path subexpressions ONCE left-to-right,
    // run top-down EnsureUnique, then store or read-modify-write through the resolved ptr<T>.
    IrOperand? placePtrOpt = ResolvePlaceAddr(fb, target, ensureUniqueLeaf: false);
    if (fb.IsTerminated || placePtrOpt is not { } placePtr) {
      return null;
    }

    if (op == TokenKind.Assign) {
      IrOperand? rhsOpt = LowerExpression(fb, assign.Value);
      if (fb.IsTerminated || rhsOpt is not { } rhs) {
        return null;
      }

      fb.Emit(new StorePtr(placePtr, rhs, assign.Span));
      return rhs;
    }

    var ptrType = (IrPtrType) _types.GetType(fb.GetOperandType(placePtr));
    IrTempId curVal = fb.AddTemp(ptrType.TargetType);
    fb.Emit(new LoadPtr(curVal, placePtr, assign.Span));
    IrOperand? compRhsOpt = LowerExpression(fb, assign.Value);
    if (fb.IsTerminated || compRhsOpt is not { } compRhs) {
      return null;
    }

    IrOperand newVal = EmitArithmeticOp(fb, op, curVal, compRhs, assign.Span);
    fb.Emit(new StorePtr(placePtr, newVal, assign.Span));
    return newVal;
  }
}
