using System.Collections.Immutable;
using System.Text;

using Pino.Compiler.Checking;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  private IrOperand? LowerExpression(FunctionBuilder fb, ExpressionSyntax expr) {
    if (fb.IsTerminated) {
      return null;
    }

    // FN-16 / ConversionKind.ImplicitItLambda: do NOT eagerly evaluate `expr` in the caller;
    // desugar directly into a 1-arg closure (non-inlined call sites).
    if (fb.Model.GetConversion(expr) is { Kind: ConversionKind.ImplicitItLambda } itConv) {
      return LowerImplicitItAsClosure(fb, expr, itConv);
    }

    IrOperand? result = LowerExpressionCore(fb, expr);
    if (fb.IsTerminated || result is not { } val) {
      return null;
    }

    return ApplyConversion(fb, expr, val, expr.Span);
  }

  private IrOperand? ApplyConversion(FunctionBuilder fb, SyntaxNode node, IrOperand val, SourceSpan span) {
    ConversionInfo? conv = fb.Model.GetConversion(node);
    if (conv is null) {
      return val;
    }

    switch (conv.Kind) {
      case ConversionKind.None:
      case ConversionKind.StringInterpolationFormat:
      case ConversionKind.ImplicitItLambda:
        return val;

      case ConversionKind.IntToFloat:
        if (fb.GetOperandType(val) == _types.Int) {
          IrTempId widened = fb.AddTemp(_types.Float);
          fb.Emit(new CastI64ToF64(widened, val, span));
          return widened;
        }
        return val;

      case ConversionKind.NeverToTarget:
        return null;

      case ConversionKind.StructToInterface: {
          var targetIface = (InterfaceType) fb.ResolveType(conv.TargetInterface ?? conv.TargetType);
          TypeSymbol srcType = fb.Model.GetType(node) is { } rawSrc
              ? fb.ResolveType(rawSrc)
              : InferSourceTypeFromOperand(fb, val);
          return EmitStructToInterface(fb, val, srcType, targetIface, span);
        }

      default:
        return val;
    }
  }

  private TypeSymbol InferSourceTypeFromOperand(FunctionBuilder fb, IrOperand val) {
    IrType irType = _types.GetType(fb.GetOperandType(val));
    if (irType is IrStructType st && st.Definition is not null) {
      ImmutableArray<TypeSymbol> tArgs = ResolveStructTypeArgsFromIr(st);
      return _compilation.Types.GetStruct(st.Definition, tArgs);
    }

    if (irType is IrInterfaceType it && it.Definition is not null) {
      return _compilation.Types.GetInterface(it.Definition);
    }

    throw new InvalidOperationException($"Cannot infer concrete TypeSymbol from IR type '{irType.Name}'.");
  }

  private ImmutableArray<TypeSymbol> ResolveStructTypeArgsFromIr(IrStructType st) {
    if (st.TypeArguments.IsEmpty) {
      return [];
    }

    // Reconstruct TypeSymbols from IrTypeTable entries when shorthand nodes don't record GetType.
    var builder = ImmutableArray.CreateBuilder<TypeSymbol>(st.TypeArguments.Length);
    foreach (IrTypeId argId in st.TypeArguments) {
      builder.Add(ReconstructTypeSymbol(argId));
    }

    return builder.MoveToImmutable();
  }

  private TypeSymbol ReconstructTypeSymbol(IrTypeId id) => _types.GetType(id) switch {
    IrPrimitiveType { Kind: IrTypeKind.Bool } => _compilation.Types.Bool,
    IrPrimitiveType { Kind: IrTypeKind.Int } => _compilation.Types.Int,
    IrPrimitiveType { Kind: IrTypeKind.Float } => _compilation.Types.Float,
    IrPrimitiveType { Kind: IrTypeKind.Rune } => _compilation.Types.Rune,
    IrPrimitiveType { Kind: IrTypeKind.String } => _compilation.Types.String,
    IrPrimitiveType { Kind: IrTypeKind.Regex } => _compilation.Types.Regex,
    IrPrimitiveType { Kind: IrTypeKind.Void } => _compilation.Types.Void,
    IrPrimitiveType { Kind: IrTypeKind.Never } => _compilation.Types.Never,
    IrEnumType e when e.Definition is not null => _compilation.Types.GetEnum(e.Definition),
    IrVectorType v => _compilation.Types.GetVector(ReconstructTypeSymbol(v.ElementType)),
    IrMapType m => _compilation.Types.GetMap(ReconstructTypeSymbol(m.KeyType), ReconstructTypeSymbol(m.ValueType)),
    IrSetType s => _compilation.Types.GetSet(ReconstructTypeSymbol(s.ElementType)),
    IrPoolType p => _compilation.Types.GetPool(ReconstructTypeSymbol(p.ElementType)),
    IrHandleType h => _compilation.Types.GetHandle(ReconstructTypeSymbol(h.ElementType)),
    IrStructType s when s.Definition is not null => _compilation.Types.GetStruct(
        s.Definition,
        [.. s.TypeArguments.Select(ReconstructTypeSymbol)]),
    IrUnionType u when u.Definition is not null => _compilation.Types.GetUnion(
        u.Definition,
        [.. u.TypeArguments.Select(ReconstructTypeSymbol)]),
    IrInterfaceType i when i.Definition is not null => _compilation.Types.GetInterface(
        i.Definition,
        [.. i.TypeArguments.Select(ReconstructTypeSymbol)]),
    IrTupleType t => _compilation.Types.GetTuple(
        [.. t.Fields.Select(f => new Checking.TupleField(f.Label, ReconstructTypeSymbol(f.Type)))]),
    IrClosureType c => _compilation.Types.GetFunction(
        [.. c.Parameters.Select(p => new FunctionParameter(
            p.Ownership == IrParamOwnership.Mut
                ? ReconstructTypeSymbol(((IrPtrType) _types.GetType(p.Type)).TargetType)
                : ReconstructTypeSymbol(p.Type),
            p.Ownership == IrParamOwnership.Mut))],
        ReconstructTypeSymbol(c.ReturnType)),
    var other => throw new InvalidOperationException($"Cannot reconstruct TypeSymbol for '{other.Name}'."),
  };

  private IrOperand? LowerExpressionCore(FunctionBuilder fb, ExpressionSyntax expr) {
    switch (expr) {
      case ParenthesizedExpressionSyntax paren:
        return LowerExpression(fb, paren.Expression);

      case LiteralExpressionSyntax lit:
        return LowerLiteral(fb, lit);

      case InterpolatedStringExpressionSyntax interp:
        return LowerInterpolatedString(fb, interp);

      case IdentifierExpressionSyntax id:
        return LowerIdentifierRead(fb, id);

      case UnaryExpressionSyntax unary:
        return LowerUnary(fb, unary);

      case BinaryExpressionSyntax binary:
        return LowerBinary(fb, binary);

      case AssignmentExpressionSyntax assign:
        return LowerAssignment(fb, assign);

      case IndexExpressionSyntax index:
        return LowerIndexRead(fb, index);

      case MemberAccessExpressionSyntax member:
        return LowerMemberRead(fb, member);

      case StaticAccessExpressionSyntax staticAccess:
        return LowerStaticRead(fb, staticAccess);

      case CallExpressionSyntax call:
        return LowerCall(fb, call);

      case IfExpressionSyntax ifExpr:
        return LowerIfExpression(fb, ifExpr);

      case MatchExpressionSyntax matchExpr:
        return LowerMatchExpression(fb, matchExpr);

      case IsPatternExpressionSyntax isPat:
        return LowerIsExpression(fb, isPat);

      case BubbleExpressionSyntax bubble:
        return LowerBubble(fb, bubble);

      case RecoveryExpressionSyntax recovery:
        return LowerRecovery(fb, recovery);

      case StructLiteralExpressionSyntax structLit:
        return LowerStructLiteral(fb, structLit);

      case TupleLiteralExpressionSyntax tupleLit:
        return LowerTupleLiteral(fb, tupleLit);

      case VectorLiteralExpressionSyntax vecLit:
        return LowerVectorLiteral(fb, vecLit);

      case VectorInitializerExpressionSyntax vecInit:
        return LowerVectorInitializer(fb, vecInit);

      case MapLiteralExpressionSyntax mapLit:
        return LowerMapLiteral(fb, mapLit);

      case SetLiteralExpressionSyntax setLit:
        return LowerSetLiteral(fb, setLit);

      case PoolExpressionSyntax poolExpr:
        return LowerPoolExpression(fb, poolExpr);

      case LambdaExpressionSyntax lambda:
        return LowerLambdaExpression(fb, lambda);

      default:
        throw new InvalidOperationException($"Unsupported expression node '{expr.GetType().Name}'.");
    }
  }

  private IrOperand LowerLiteral(FunctionBuilder fb, LiteralExpressionSyntax lit) {
    string raw = NameOf(fb.Model, lit.Token);
    return lit.Token.Kind switch {
      TokenKind.IntLiteral => fb.EmitConstInt(ParseIntLiteral(raw), lit.Span),
      TokenKind.FloatLiteral => fb.EmitConstFloat(ParseFloatLiteral(raw), lit.Span),
      TokenKind.TrueLiteral => fb.EmitConstBool(true, lit.Span),
      TokenKind.FalseLiteral => fb.EmitConstBool(false, lit.Span),
      TokenKind.RuneLiteral => fb.EmitConstRune(DecodeRuneLiteral(raw), lit.Span),
      _ => throw new InvalidOperationException($"Unsupported literal token kind '{lit.Token.Kind}'."),
    };
  }

  private IrOperand? LowerInterpolatedString(FunctionBuilder fb, InterpolatedStringExpressionSyntax interp) {
    bool hasInterpolation = false;
    foreach (StringPartSyntax part in interp.Parts) {
      if (part is StringInterpolationPartSyntax) {
        hasInterpolation = true;
        break;
      }
    }

    if (!hasInterpolation) {
      var sb = new StringBuilder();
      foreach (StringPartSyntax part in interp.Parts) {
        if (part is StringTextPartSyntax textPart) {
          sb.Append(UnescapeStringText(NameOf(fb.Model, textPart.TextToken)));
        }
      }

      return fb.EmitConstString(sb.ToString(), interp.Span);
    }

    var parts = ImmutableArray.CreateBuilder<IrOperand>(interp.Parts.Length);
    foreach (StringPartSyntax part in interp.Parts) {
      if (part is StringTextPartSyntax textPart) {
        string text = UnescapeStringText(NameOf(fb.Model, textPart.TextToken));
        if (text.Length > 0) {
          parts.Add(fb.EmitConstString(text, textPart.Span));
        }
      } else if (part is StringInterpolationPartSyntax interpPart) {
        IrOperand? val = LowerExpression(fb, interpPart.Expression);
        if (fb.IsTerminated || val is not { } v) {
          return null;
        }

        if (fb.GetOperandType(v) == _types.String) {
          parts.Add(v);
        } else {
          IrTempId formatted = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(formatted, IrRuntimeOp.ToStr, [v], AllocRegion.Heap, interpPart.Span));
          parts.Add(formatted);
        }
      }
    }

    if (parts.Count == 0) {
      return fb.EmitConstString(string.Empty, interp.Span);
    }

    if (parts.Count == 1) {
      return parts[0];
    }

    IrTempId result = fb.AddTemp(_types.String);
    fb.Emit(new StrConcatN(result, parts.ToImmutable(), AllocRegion.Heap, interp.Span));
    return result;
  }

  private IrOperand LowerIdentifierRead(FunctionBuilder fb, IdentifierExpressionSyntax id) {
    Symbol sym = fb.Model.GetReferencedSymbol(id)
        ?? throw new InvalidOperationException($"Unresolved identifier '{NameOf(fb.Model, id.IdentifierToken)}'.");
    return EmitSymbolValueRead(fb, sym, id.Span);
  }

  private IrOperand EmitSymbolValueRead(FunctionBuilder fb, Symbol sym, SourceSpan span) {
    switch (sym) {
      case LocalSymbol local:
        if (IsSelfLocal(local)) {
          return EmitSelfValueRead(fb, span);
        }

        if (_globalIds.TryGetValue(local, out IrGlobalId globalId)) {
          IrTypeId gType = _module.Globals[globalId.Index].Type;
          IrTempId ptrTmp = fb.AddTemp(_types.GetPtr(gType));
          fb.Emit(new AddrGlobal(ptrTmp, globalId, span));
          IrTempId valTmp = fb.AddTemp(gType);
          fb.Emit(new LoadPtr(valTmp, ptrTmp, span));
          return valTmp;
        }

        if (IsSymbolBoxedCell(fb, local)) {
          return EmitCellRead(fb, local, span);
        }

        if (fb.LocalSlots.TryGetValue(local, out IrSlotId slotId)) {
          IrTypeId sType = fb.Function.Slots[slotId.Index].Type;
          IrTempId valTmp = fb.AddTemp(sType);
          fb.Emit(new LoadSlot(valTmp, slotId, span));
          return valTmp;
        }

        if (fb.LocalTemps.TryGetValue(local, out IrOperand tempOp)) {
          return tempOp;
        }

        throw new InvalidOperationException($"Local '{local.Name}' was not bound in '{fb.Function.Name}'.");

      case ParameterSymbol param: {
          if (fb.LocalTemps.TryGetValue(param, out IrOperand capturedOrInlined)) {
            return capturedOrInlined;
          }

          if (!fb.ParamIndices.TryGetValue(param, out int paramIdx)) {
            throw new InvalidOperationException($"Parameter '{param.Name}' was not bound in '{fb.Function.Name}'.");
          }

          IrOperand paramOp = IrOperand.FromParam(paramIdx);
          if (param.IsMutable) {
            var ptrType = (IrPtrType) _types.GetType(fb.Function.Parameters[paramIdx].Type);
            IrTempId loaded = fb.AddTemp(ptrType.TargetType);
            fb.Emit(new LoadPtr(loaded, paramOp, span));
            return loaded;
          }

          return paramOp;
        }

      case FieldSymbol field: {
          // Bare field read inside an instance method (STRUCT-3).
          StructSymbol owner = fb.EnclosingStruct
              ?? throw new InvalidOperationException($"Bare field '{field.Name}' read outside a struct method.");
          IrOperand selfVal = EmitSelfValueRead(fb, span);
          return EmitStructFieldGet(fb, selfVal, owner, field, span);
        }

      case FunctionSymbol fnSym:
        return LowerFunctionAsValue(fb, fnSym, span);

      case MethodSymbol methodSym: {
          // Bare non-mutating instance method used as a bound method value inside a struct method (STRUCT-14/19).
          StructSymbol owner = fb.EnclosingStruct
              ?? throw new InvalidOperationException($"Bare method '{methodSym.Name}' read outside a struct method.");
          IrOperand selfVal = EmitSelfValueRead(fb, span);
          ImmutableArray<TypeSymbol> ownerArgs = [.. owner.TypeParameters.Select(tp => fb.ResolveType(tp.Type))];
          StructType concreteOwner = _compilation.Types.GetStruct(owner, ownerArgs);
          var fnParams = ImmutableArray.CreateBuilder<FunctionParameter>(methodSym.Parameters.Length);
          foreach (ParameterSymbol p in methodSym.Parameters) {
            fnParams.Add(new FunctionParameter(fb.ResolveType(p.Type), p.IsMutable));
          }

          FunctionType concreteFnType = _compilation.Types.GetFunction(
              fnParams.MoveToImmutable(),
              fb.ResolveType(methodSym.ReturnType));
          return LowerBoundMethodValue(fb, selfVal, concreteOwner, methodSym, concreteFnType, span);
        }

      default:
        throw new InvalidOperationException($"Cannot read value of symbol '{sym.Name}' ({sym.GetType().Name}).");
    }
  }

  private IrOperand EmitSelfValueRead(FunctionBuilder fb, SourceSpan span) {
    if (fb.CapturedSelf is { } capturedSelf) {
      return capturedSelf;
    }

    IrOperand selfParam = IrOperand.FromParam(0);
    if (fb.IsMutatingMethod) {
      var ptrType = (IrPtrType) _types.GetType(fb.Function.Parameters[0].Type);
      IrTempId loaded = fb.AddTemp(ptrType.TargetType);
      fb.Emit(new LoadPtr(loaded, selfParam, span));
      return loaded;
    }

    return selfParam;
  }

  private IrOperand EmitStructFieldGet(
      FunctionBuilder fb,
      IrOperand structVal,
      StructSymbol structSym,
      FieldSymbol field,
      SourceSpan span) {
    IrOperand current = structVal;
    StructSymbol currentStruct = structSym;

    // Walk promoted embedded sub-struct chain first (STRUCT-8).
    foreach (FieldSymbol step in field.EmbeddedChain) {
      var targetStructType = (StructType) step.Type;
      int embIdx = FindEmbeddedStructFieldIndex(currentStruct, targetStructType.Definition);
      var irStruct = (IrStructType) _types.GetType(fb.GetOperandType(current));
      IrTempId subVal = fb.AddTemp(irStruct.Fields[embIdx].Type);
      fb.Emit(new FieldGet(subVal, current, embIdx, span));
      current = subVal;
      currentStruct = targetStructType.Definition;
    }

    int leafIdx = field.IsPromotedFromEmbedded
        ? FindDirectFieldIndexByName(currentStruct, field.Name)
        : FindStructFieldIndex(currentStruct, field);
    var leafStruct = (IrStructType) _types.GetType(fb.GetOperandType(current));
    IrTempId fieldVal = fb.AddTemp(leafStruct.Fields[leafIdx].Type);
    fb.Emit(new FieldGet(fieldVal, current, leafIdx, span));
    return fieldVal;
  }

  private IrOperand? LowerUnary(FunctionBuilder fb, UnaryExpressionSyntax unary) {
    // Handle `-9223372036854775808` (INT64_MIN literal) without overflowing Int64.Parse.
    if (unary.OperatorToken.Kind == TokenKind.Minus
        && UnwrapParens(unary.Operand) is LiteralExpressionSyntax { Token.Kind: TokenKind.IntLiteral } intLit) {
      string cleaned = NameOf(fb.Model, intLit.Token).Replace("_", string.Empty, StringComparison.Ordinal);
      if (string.Equals(cleaned, "9223372036854775808", StringComparison.Ordinal)) {
        return fb.EmitConstInt(long.MinValue, unary.Span);
      }
    }

    IrOperand? operand = LowerExpression(fb, unary.Operand);
    if (fb.IsTerminated || operand is not { } opVal) {
      return null;
    }

    if (unary.OperatorToken.Kind == TokenKind.Not) {
      IrTempId dest = fb.AddTemp(_types.Bool);
      fb.Emit(new Not(dest, opVal, unary.Span));
      return dest;
    }

    IrTempId negDest = fb.AddTemp(fb.GetOperandType(opVal));
    fb.Emit(new Neg(negDest, opVal, unary.Span));
    return negDest;
  }

  private IrOperand? LowerBinary(FunctionBuilder fb, BinaryExpressionSyntax binary) {
    TokenKind kind = binary.OperatorToken.Kind;

    // Short-circuit logical operators (TYPE-6) with `is`-binding propagation on `and`.
    if (kind is TokenKind.And or TokenKind.Or) {
      return LowerShortCircuitLogical(fb, binary);
    }

    IrOperand? leftOpt = LowerExpression(fb, binary.Left);
    if (fb.IsTerminated || leftOpt is not { } left) {
      return null;
    }

    IrOperand? rightOpt = LowerExpression(fb, binary.Right);
    if (fb.IsTerminated || rightOpt is not { } right) {
      return null;
    }

    switch (kind) {
      case TokenKind.Plus:
      case TokenKind.Minus:
      case TokenKind.Star:
      case TokenKind.Slash:
      case TokenKind.SlashSlash:
      case TokenKind.Percent:
        return EmitArithmeticOp(fb, kind, left, right, binary.Span);

      case TokenKind.Less:
      case TokenKind.LessEqual:
      case TokenKind.Greater:
      case TokenKind.GreaterEqual:
        return EmitOrderedComparison(fb, kind, left, right, binary.Span);

      case TokenKind.EqualEqual:
        return EmitEquality(fb, left, right, negated: false, binary.Span);

      case TokenKind.BangEqual:
        return EmitEquality(fb, left, right, negated: true, binary.Span);

      case TokenKind.InKeyword:
        return EmitMembership(fb, left, right, negated: false, binary.Span);

      case TokenKind.Not when binary.SecondaryOperatorToken is { Kind: TokenKind.InKeyword }:
        return EmitMembership(fb, left, right, negated: true, binary.Span);

      default:
        throw new InvalidOperationException($"Unsupported binary operator '{kind}'.");
    }
  }

  private IrOperand? LowerShortCircuitLogical(FunctionBuilder fb, BinaryExpressionSyntax binary) {
    IrSlotId resSlot = fb.AddSlot("logic_res", _types.Bool, isMutSlot: true);
    IrBasicBlock bbTrue = fb.NewBlock();
    IrBasicBlock bbFalse = fb.NewBlock();
    IrBasicBlock bbMerge = fb.NewBlock();

    LowerCondition(fb, binary, bbTrue.Id, bbFalse.Id);

    fb.SwitchTo(bbTrue);
    IrTempId cTrue = fb.EmitConstBool(true, binary.Span);
    fb.Emit(new StoreSlot(resSlot, cTrue, binary.Span));
    fb.Terminate(new Br(bbMerge.Id, binary.Span));

    fb.SwitchTo(bbFalse);
    IrTempId cFalse = fb.EmitConstBool(false, binary.Span);
    fb.Emit(new StoreSlot(resSlot, cFalse, binary.Span));
    fb.Terminate(new Br(bbMerge.Id, binary.Span));

    fb.SwitchTo(bbMerge);
    IrTempId loaded = fb.AddTemp(_types.Bool);
    fb.Emit(new LoadSlot(loaded, resSlot, binary.Span));
    return loaded;
  }

  private IrOperand EmitArithmeticOp(
      FunctionBuilder fb,
      TokenKind op,
      IrOperand left,
      IrOperand right,
      SourceSpan span) {
    TokenKind baseOp = op switch {
      TokenKind.PlusAssign => TokenKind.Plus,
      TokenKind.MinusAssign => TokenKind.Minus,
      TokenKind.StarAssign => TokenKind.Star,
      TokenKind.SlashAssign => TokenKind.Slash,
      TokenKind.SlashSlashAssign => TokenKind.SlashSlash,
      TokenKind.PercentAssign => TokenKind.Percent,
      _ => op,
    };

    IrTypeId leftType = fb.GetOperandType(left);
    IrTypeId rightType = fb.GetOperandType(right);

    switch (baseOp) {
      case TokenKind.Plus:
        if (leftType == _types.String && rightType == _types.String) {
          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new StrConcat(d, left, right, AllocRegion.Heap, span));
          return d;
        }

        if (leftType == _types.String && rightType == _types.Rune) {
          // STR-11
          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new StrConcatRune(d, left, right, AllocRegion.Heap, span));
          return d;
        }

        if (leftType == _types.Rune && rightType == _types.String) {
          // STR-12
          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new RuneConcatStr(d, left, right, AllocRegion.Heap, span));
          return d;
        }

        if (leftType == _types.Rune && rightType == _types.Int) {
          // RUNE-2
          IrTempId delta = fb.AddTemp(_types.Rune);
          fb.Emit(new CastI64ToRune(delta, right, span));
          IrTempId d = fb.AddTemp(_types.Rune);
          fb.Emit(new Add(d, left, delta, span));
          return d;
        }

        IrTempId addDest = fb.AddTemp(leftType);
        fb.Emit(new Add(addDest, left, right, span));
        return addDest;

      case TokenKind.Minus:
        if (leftType == _types.Rune && rightType == _types.Int) {
          // RUNE-2
          IrTempId delta = fb.AddTemp(_types.Rune);
          fb.Emit(new CastI64ToRune(delta, right, span));
          IrTempId d = fb.AddTemp(_types.Rune);
          fb.Emit(new Sub(d, left, delta, span));
          return d;
        }

        if (leftType == _types.Rune && rightType == _types.Rune) {
          // RUNE-3
          IrTempId li = fb.AddTemp(_types.Int);
          IrTempId ri = fb.AddTemp(_types.Int);
          fb.Emit(new CastRuneToI64(li, left, span));
          fb.Emit(new CastRuneToI64(ri, right, span));
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new Sub(d, li, ri, span));
          return d;
        }

        IrTempId subDest = fb.AddTemp(leftType);
        fb.Emit(new Sub(subDest, left, right, span));
        return subDest;

      case TokenKind.Star: {
          IrTempId d = fb.AddTemp(leftType);
          fb.Emit(new Mul(d, left, right, span));
          return d;
        }

      case TokenKind.Slash: {
          // TYPE-9, TYPE-E4: always float division with runtime zero check.
          EmitDivZeroGuard(fb, right, isFloat: true, span);
          IrTempId d = fb.AddTemp(_types.Float);
          fb.Emit(new DivF64(d, left, right, span));
          return d;
        }

      case TokenKind.SlashSlash: {
          // TYPE-14, TYPE-15, TYPE-E4, TYPE-E6, D-164, D-186.
          bool isFloat = leftType == _types.Float;
          EmitDivZeroGuard(fb, right, isFloat, span);
          if (!isFloat) {
            EmitIntMinDivOverflowGuard(fb, left, right, span);
          }

          IrTempId d = fb.AddTemp(leftType);
          fb.Emit(new FloorDiv(d, left, right, span));
          return d;
        }

      case TokenKind.Percent: {
          // TYPE-14, TYPE-18, TYPE-E4, TYPE-E6, D-164, D-171, D-186.
          bool isFloat = leftType == _types.Float;
          EmitDivZeroGuard(fb, right, isFloat, span);
          if (!isFloat) {
            EmitIntMinDivOverflowGuard(fb, left, right, span);
          }

          IrTempId d = fb.AddTemp(leftType);
          fb.Emit(new FloorMod(d, left, right, span));
          return d;
        }

      default:
        throw new InvalidOperationException($"Unsupported arithmetic operator '{op}'.");
    }
  }

  private void EmitDivZeroGuard(FunctionBuilder fb, IrOperand divisor, bool isFloat, SourceSpan span) {
    IrOperand zero = isFloat ? fb.EmitConstFloat(0.0, span) : fb.EmitConstInt(0, span);
    IrTempId isZero = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(isZero, IrCmpOp.Eq, divisor, zero, span));

    IrBasicBlock bbPanic = fb.NewBlock();
    IrBasicBlock bbOk = fb.NewBlock();
    fb.Terminate(new BrIf(isZero, bbPanic.Id, bbOk.Id, span));

    fb.SwitchTo(bbPanic);
    IrTempId msg = fb.EmitConstString(PanicDivByZero, span);
    fb.Terminate(new Panic(msg, span));

    fb.SwitchTo(bbOk);
  }

  private void EmitIntMinDivOverflowGuard(FunctionBuilder fb, IrOperand left, IrOperand right, SourceSpan span) {
    IrTempId minInt = fb.EmitConstInt(long.MinValue, span);
    IrTempId isMin = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(isMin, IrCmpOp.Eq, left, minInt, span));

    IrBasicBlock bbCheckNeg1 = fb.NewBlock();
    IrBasicBlock bbPanic = fb.NewBlock();
    IrBasicBlock bbOk = fb.NewBlock();
    fb.Terminate(new BrIf(isMin, bbCheckNeg1.Id, bbOk.Id, span));

    fb.SwitchTo(bbCheckNeg1);
    IrTempId neg1 = fb.EmitConstInt(-1, span);
    IrTempId isNeg1 = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(isNeg1, IrCmpOp.Eq, right, neg1, span));
    fb.Terminate(new BrIf(isNeg1, bbPanic.Id, bbOk.Id, span));

    fb.SwitchTo(bbPanic);
    IrTempId msg = fb.EmitConstString(PanicIntMinDivOverflow, span);
    fb.Terminate(new Panic(msg, span));

    fb.SwitchTo(bbOk);
  }

  private IrOperand EmitOrderedComparison(
      FunctionBuilder fb,
      TokenKind kind,
      IrOperand left,
      IrOperand right,
      SourceSpan span) {
    IrCmpOp cmpOp = kind switch {
      TokenKind.Less => IrCmpOp.Lt,
      TokenKind.LessEqual => IrCmpOp.Le,
      TokenKind.Greater => IrCmpOp.Gt,
      TokenKind.GreaterEqual => IrCmpOp.Ge,
      _ => throw new InvalidOperationException($"Not an ordered comparison '{kind}'."),
    };

    if (fb.GetOperandType(left) == _types.String) {
      // STR-10: lexicographical string comparison.
      IrTempId cmpVal = fb.AddTemp(_types.Int);
      fb.Emit(new StrCmp(cmpVal, left, right, span));
      IrTempId zero = fb.EmitConstInt(0, span);
      IrTempId res = fb.AddTemp(_types.Bool);
      fb.Emit(new Cmp(res, cmpOp, cmpVal, zero, span));
      return res;
    }

    IrTempId dest = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(dest, cmpOp, left, right, span));
    return dest;
  }

  private IrOperand EmitEquality(
      FunctionBuilder fb,
      IrOperand left,
      IrOperand right,
      bool negated,
      SourceSpan span) {
    IrTypeId leftTypeId = fb.GetOperandType(left);
    IrTypeId rightTypeId = fb.GetOperandType(right);
    IrType leftIrType = _types.GetType(leftTypeId);
    IrType rightIrType = _types.GetType(rightTypeId);

    // Struct == Interface or Interface == Struct: box concrete struct into the interface first.
    if (leftTypeId != rightTypeId) {
      if (leftIrType is IrStructType leftSt && rightIrType is IrInterfaceType rightIt) {
        var concreteSt = (StructType) ReconstructTypeSymbol(leftTypeId);
        var concreteIt = (InterfaceType) ReconstructTypeSymbol(rightTypeId);
        left = EmitStructToInterface(fb, left, concreteSt, concreteIt, span);
        leftTypeId = rightTypeId;
        leftIrType = rightIrType;
      } else if (rightIrType is IrStructType rightSt && leftIrType is IrInterfaceType leftIt) {
        var concreteSt = (StructType) ReconstructTypeSymbol(rightTypeId);
        var concreteIt = (InterfaceType) ReconstructTypeSymbol(leftTypeId);
        right = EmitStructToInterface(fb, right, concreteSt, concreteIt, span);
        rightTypeId = leftTypeId;
        rightIrType = leftIrType;
      }
    }

    IrTypeId typeId = leftTypeId;
    IrType irType = leftIrType;

    switch (irType.Kind) {
      case IrTypeKind.Bool:
      case IrTypeKind.Int:
      case IrTypeKind.Float:
      case IrTypeKind.Rune:
      case IrTypeKind.Enum:
      case IrTypeKind.Handle: {
          IrTempId dest = fb.AddTemp(_types.Bool);
          fb.Emit(new Cmp(dest, negated ? IrCmpOp.Ne : IrCmpOp.Eq, left, right, span));
          return dest;
        }

      case IrTypeKind.String: {
          IrTempId eq = fb.AddTemp(_types.Bool);
          fb.Emit(new StrEq(eq, left, right, span));
          if (!negated) {
            return eq;
          }

          IrTempId ne = fb.AddTemp(_types.Bool);
          fb.Emit(new Not(ne, eq, span));
          return ne;
        }

      case IrTypeKind.Regex: {
          // EQ-8: regex values compare equal when their pattern strings are equal.
          IrTempId p1 = fb.AddTemp(_types.String);
          IrTempId p2 = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(p1, IrRuntimeOp.RegexPattern, [left], AllocRegion.Stack, span));
          fb.Emit(new CallRuntime(p2, IrRuntimeOp.RegexPattern, [right], AllocRegion.Stack, span));
          IrTempId eq = fb.AddTemp(_types.Bool);
          fb.Emit(new StrEq(eq, p1, p2, span));
          if (!negated) {
            return eq;
          }

          IrTempId ne = fb.AddTemp(_types.Bool);
          fb.Emit(new Not(ne, eq, span));
          return ne;
        }

      case IrTypeKind.Struct:
      case IrTypeKind.Union:
      case IrTypeKind.Vector:
      case IrTypeKind.Map:
      case IrTypeKind.Set:
      case IrTypeKind.Pool: {
          IrFuncId eqFn = GetOrSynthesizeEqHelper(typeId);
          IrTempId eq = fb.AddTemp(_types.Bool);
          fb.Emit(new CallDirect(eq, eqFn, [left, right], span));
          if (!negated) {
            return eq;
          }

          IrTempId ne = fb.AddTemp(_types.Bool);
          fb.Emit(new Not(ne, eq, span));
          return ne;
        }

      case IrTypeKind.Interface:
      case IrTypeKind.Closure: {
          IrTempId eq = fb.AddTemp(_types.Bool);
          fb.Emit(new StructEq(eq, left, right, span));
          if (!negated) {
            return eq;
          }

          IrTempId ne = fb.AddTemp(_types.Bool);
          fb.Emit(new Not(ne, eq, span));
          return ne;
        }

      default:
        throw new InvalidOperationException($"Unsupported equality on type '{irType.Name}'.");
    }
  }

  private IrFuncId GetOrSynthesizeEqHelper(IrTypeId typeId) {
    if (_eqHelpers.TryGetValue(typeId, out IrFuncId existing)) {
      return existing;
    }

    IrType irType = _types.GetType(typeId);
    string helperName = ReserveFunctionName($"__pino_eq_{SanitizeTypeName(irType.Name)}");
    IrFunction eqFn = _module.AddFunction(
        helperName,
        [
          new IrParam("a", typeId, IrParamOwnership.Owned),
          new IrParam("b", typeId, IrParamOwnership.Owned),
        ],
        _types.Bool,
        ModuleStateEffects.Pure);
    _eqHelpers[typeId] = eqFn.Id;

    var fb = new FunctionBuilder(this, eqFn, _compilation.RootModel);
    IrOperand a = IrOperand.FromParam(0);
    IrOperand b = IrOperand.FromParam(1);

    switch (irType) {
      case IrStructType st:
        BuildStructEqBody(fb, st, a, b);
        break;

      case IrUnionType ut:
        BuildUnionEqBody(fb, ut, a, b);
        break;

      case IrVectorType vt:
        BuildVectorEqBody(fb, vt, a, b);
        break;

      case IrMapType mt:
        BuildMapEqBody(fb, mt, a, b);
        break;

      case IrSetType setT:
        BuildSetEqBody(fb, setT, a, b);
        break;

      case IrPoolType pt:
        BuildPoolEqBody(fb, pt, a, b);
        break;

      default:
        throw new InvalidOperationException($"Unexpected type for eq helper: '{irType.Name}'.");
    }

    FinalizeFunctionBody(fb, default);
    return eqFn.Id;
  }

  private void BuildStructEqBody(FunctionBuilder fb, IrStructType st, IrOperand a, IrOperand b) {
    for (int i = 0; i < st.Fields.Length; i++) {
      IrTypeId fieldType = st.Fields[i].Type;
      IrTempId fa = fb.AddTemp(fieldType);
      IrTempId fbVal = fb.AddTemp(fieldType);
      fb.Emit(new FieldGet(fa, a, i));
      fb.Emit(new FieldGet(fbVal, b, i));
      IrOperand eq = EmitEquality(fb, fa, fbVal, negated: false, default);

      IrBasicBlock bbNext = fb.NewBlock();
      IrBasicBlock bbFalse = fb.NewBlock();
      fb.Terminate(new BrIf(eq, bbNext.Id, bbFalse.Id));

      fb.SwitchTo(bbFalse);
      fb.Terminate(new Ret(fb.EmitConstBool(false)));

      fb.SwitchTo(bbNext);
    }

    fb.Terminate(new Ret(fb.EmitConstBool(true)));
  }

  private void BuildUnionEqBody(FunctionBuilder fb, IrUnionType ut, IrOperand a, IrOperand b) {
    IrTempId tagA = fb.AddTemp(_types.Int, "tag_a");
    IrTempId tagB = fb.AddTemp(_types.Int, "tag_b");
    fb.Emit(new UnionTag(tagA, a));
    fb.Emit(new UnionTag(tagB, b));
    IrTempId tagsEq = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(tagsEq, IrCmpOp.Eq, tagA, tagB));

    IrBasicBlock bbSwitch = fb.NewBlock();
    IrBasicBlock bbFalse = fb.NewBlock();
    fb.Terminate(new BrIf(tagsEq, bbSwitch.Id, bbFalse.Id));

    fb.SwitchTo(bbFalse);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbSwitch);
    var cases = ImmutableArray.CreateBuilder<(int, IrBlockId)>(ut.Variants.Length);
    var variantBlocks = new IrBasicBlock[ut.Variants.Length];
    for (int v = 0; v < ut.Variants.Length; v++) {
      variantBlocks[v] = fb.NewBlock();
      cases.Add((ut.Variants[v].TagOrdinal, variantBlocks[v].Id));
    }

    fb.Terminate(new Switch(tagA, cases.MoveToImmutable(), null));

    for (int v = 0; v < ut.Variants.Length; v++) {
      fb.SwitchTo(variantBlocks[v]);
      IrUnionVariant variant = ut.Variants[v];
      for (int f = 0; f < variant.Fields.Length; f++) {
        IrTypeId fType = variant.Fields[f].Type;
        IrTempId fa = fb.AddTemp(fType);
        IrTempId fbVal = fb.AddTemp(fType);
        fb.Emit(new UnionField(fa, a, v, f));
        fb.Emit(new UnionField(fbVal, b, v, f));
        IrOperand eq = EmitEquality(fb, fa, fbVal, negated: false, default);

        IrBasicBlock bbNext = fb.NewBlock();
        IrBasicBlock bbMismatch = fb.NewBlock();
        fb.Terminate(new BrIf(eq, bbNext.Id, bbMismatch.Id));

        fb.SwitchTo(bbMismatch);
        fb.Terminate(new Ret(fb.EmitConstBool(false)));

        fb.SwitchTo(bbNext);
      }

      fb.Terminate(new Ret(fb.EmitConstBool(true)));
    }
  }

  private void BuildVectorEqBody(FunctionBuilder fb, IrVectorType vt, IrOperand a, IrOperand b) {
    IrTempId lenA = fb.AddTemp(_types.Int, "len_a");
    IrTempId lenB = fb.AddTemp(_types.Int, "len_b");
    fb.Emit(new VecLen(lenA, a));
    fb.Emit(new VecLen(lenB, b));
    IrTempId lenEq = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(lenEq, IrCmpOp.Eq, lenA, lenB));

    IrBasicBlock bbInit = fb.NewBlock();
    IrBasicBlock bbFalse = fb.NewBlock();
    fb.Terminate(new BrIf(lenEq, bbInit.Id, bbFalse.Id));

    fb.SwitchTo(bbFalse);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbInit);
    IrSlotId idxSlot = fb.AddSlot("i", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0)));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbTrue = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, lenA));
    fb.Terminate(new BrIf(cond, bbBody.Id, bbTrue.Id));

    fb.SwitchTo(bbBody);
    IrTempId ea = fb.AddTemp(vt.ElementType);
    IrTempId eb = fb.AddTemp(vt.ElementType);
    fb.Emit(new VecGet(ea, a, curI, Unchecked: true));
    fb.Emit(new VecGet(eb, b, curI, Unchecked: true));
    IrOperand eq = EmitEquality(fb, ea, eb, negated: false, default);

    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbMismatch = fb.NewBlock();
    fb.Terminate(new BrIf(eq, bbLatch.Id, bbMismatch.Id));

    fb.SwitchTo(bbMismatch);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbLatch);
    IrTempId nextI = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextI, curI, fb.EmitConstInt(1)));
    fb.Emit(new StoreSlot(idxSlot, nextI));
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbTrue);
    fb.Terminate(new Ret(fb.EmitConstBool(true)));
  }

  private void BuildMapEqBody(FunctionBuilder fb, IrMapType mt, IrOperand a, IrOperand b) {
    IrTempId lenA = fb.AddTemp(_types.Int);
    IrTempId lenB = fb.AddTemp(_types.Int);
    fb.Emit(new MapLen(lenA, a));
    fb.Emit(new MapLen(lenB, b));
    IrTempId lenEq = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(lenEq, IrCmpOp.Eq, lenA, lenB));

    IrBasicBlock bbInit = fb.NewBlock();
    IrBasicBlock bbFalse = fb.NewBlock();
    fb.Terminate(new BrIf(lenEq, bbInit.Id, bbFalse.Id));

    fb.SwitchTo(bbFalse);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbInit);
    IrTempId slen = fb.AddTemp(_types.Int);
    fb.Emit(new MapSlotLen(slen, a));
    IrSlotId idxSlot = fb.AddSlot("i", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0)));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbCheck = fb.NewBlock();
    IrBasicBlock bbHasKey = fb.NewBlock();
    IrBasicBlock bbCmpVal = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbMismatch = fb.NewBlock();
    IrBasicBlock bbTrue = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, slen));
    fb.Terminate(new BrIf(cond, bbCheck.Id, bbTrue.Id));

    fb.SwitchTo(bbCheck);
    IrTempId live = fb.AddTemp(_types.Bool);
    fb.Emit(new MapSlotIsLive(live, a, curI));
    fb.Terminate(new BrIf(live, bbHasKey.Id, bbLatch.Id));

    fb.SwitchTo(bbHasKey);
    IrTempId key = fb.AddTemp(mt.KeyType);
    fb.Emit(new MapSlotKey(key, a, curI));
    IrTempId hasK = fb.AddTemp(_types.Bool);
    fb.Emit(new MapContains(hasK, b, key));
    fb.Terminate(new BrIf(hasK, bbCmpVal.Id, bbMismatch.Id));

    fb.SwitchTo(bbCmpVal);
    IrTempId valA = fb.AddTemp(mt.ValueType);
    IrTempId valB = fb.AddTemp(mt.ValueType);
    fb.Emit(new MapSlotValue(valA, a, curI));
    fb.Emit(new MapGet(valB, b, key));
    IrOperand valEq = EmitEquality(fb, valA, valB, negated: false, default);
    fb.Terminate(new BrIf(valEq, bbLatch.Id, bbMismatch.Id));

    fb.SwitchTo(bbMismatch);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbLatch);
    IrTempId nextI = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextI, curI, fb.EmitConstInt(1)));
    fb.Emit(new StoreSlot(idxSlot, nextI));
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbTrue);
    fb.Terminate(new Ret(fb.EmitConstBool(true)));
  }

  private void BuildSetEqBody(FunctionBuilder fb, IrSetType st, IrOperand a, IrOperand b) {
    IrTempId lenA = fb.AddTemp(_types.Int);
    IrTempId lenB = fb.AddTemp(_types.Int);
    fb.Emit(new SetLen(lenA, a));
    fb.Emit(new SetLen(lenB, b));
    IrTempId lenEq = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(lenEq, IrCmpOp.Eq, lenA, lenB));

    IrBasicBlock bbInit = fb.NewBlock();
    IrBasicBlock bbFalse = fb.NewBlock();
    fb.Terminate(new BrIf(lenEq, bbInit.Id, bbFalse.Id));

    fb.SwitchTo(bbFalse);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbInit);
    IrTempId slen = fb.AddTemp(_types.Int);
    fb.Emit(new SetSlotLen(slen, a));
    IrSlotId idxSlot = fb.AddSlot("i", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0)));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbCheck = fb.NewBlock();
    IrBasicBlock bbHasElem = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbMismatch = fb.NewBlock();
    IrBasicBlock bbTrue = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, slen));
    fb.Terminate(new BrIf(cond, bbCheck.Id, bbTrue.Id));

    fb.SwitchTo(bbCheck);
    IrTempId live = fb.AddTemp(_types.Bool);
    fb.Emit(new SetSlotIsLive(live, a, curI));
    fb.Terminate(new BrIf(live, bbHasElem.Id, bbLatch.Id));

    fb.SwitchTo(bbHasElem);
    IrTempId elem = fb.AddTemp(st.ElementType);
    fb.Emit(new SetSlotElem(elem, a, curI));
    IrTempId hasE = fb.AddTemp(_types.Bool);
    fb.Emit(new SetContains(hasE, b, elem));
    fb.Terminate(new BrIf(hasE, bbLatch.Id, bbMismatch.Id));

    fb.SwitchTo(bbMismatch);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbLatch);
    IrTempId nextI = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextI, curI, fb.EmitConstInt(1)));
    fb.Emit(new StoreSlot(idxSlot, nextI));
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbTrue);
    fb.Terminate(new Ret(fb.EmitConstBool(true)));
  }

  private void BuildPoolEqBody(FunctionBuilder fb, IrPoolType pt, IrOperand a, IrOperand b) {
    // EQ-14: equal live count, same live handles (index & gen), and equal values.
    IrTempId lenA = fb.AddTemp(_types.Int);
    IrTempId lenB = fb.AddTemp(_types.Int);
    fb.Emit(new PoolLen(lenA, a));
    fb.Emit(new PoolLen(lenB, b));
    IrTempId lenEq = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(lenEq, IrCmpOp.Eq, lenA, lenB));

    IrBasicBlock bbInit = fb.NewBlock();
    IrBasicBlock bbFalse = fb.NewBlock();
    fb.Terminate(new BrIf(lenEq, bbInit.Id, bbFalse.Id));

    fb.SwitchTo(bbFalse);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbInit);
    IrTempId slen = fb.AddTemp(_types.Int);
    fb.Emit(new PoolSlotLen(slen, a));
    IrSlotId idxSlot = fb.AddSlot("i", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0)));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbCheck = fb.NewBlock();
    IrBasicBlock bbHasHandle = fb.NewBlock();
    IrBasicBlock bbCmpVal = fb.NewBlock();
    IrBasicBlock bbLatch = fb.NewBlock();
    IrBasicBlock bbMismatch = fb.NewBlock();
    IrBasicBlock bbTrue = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbHead);
    IrTempId curI = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curI, idxSlot));
    IrTempId cond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(cond, IrCmpOp.Lt, curI, slen));
    fb.Terminate(new BrIf(cond, bbCheck.Id, bbTrue.Id));

    fb.SwitchTo(bbCheck);
    IrTempId live = fb.AddTemp(_types.Bool);
    fb.Emit(new PoolSlotIsLive(live, a, curI));
    fb.Terminate(new BrIf(live, bbHasHandle.Id, bbLatch.Id));

    fb.SwitchTo(bbHasHandle);
    IrTypeId handleType = _types.GetHandle(pt.ElementType);
    IrTempId handle = fb.AddTemp(handleType);
    fb.Emit(new PoolSlotHandle(handle, a, curI));
    IrTempId hasH = fb.AddTemp(_types.Bool);
    fb.Emit(new PoolContains(hasH, b, handle));
    fb.Terminate(new BrIf(hasH, bbCmpVal.Id, bbMismatch.Id));

    fb.SwitchTo(bbCmpVal);
    IrTempId valA = fb.AddTemp(pt.ElementType);
    IrTempId valB = fb.AddTemp(pt.ElementType);
    fb.Emit(new PoolSlotValue(valA, a, curI));
    fb.Emit(new PoolGetOrPanic(valB, b, handle, "pool"));
    IrOperand valEq = EmitEquality(fb, valA, valB, negated: false, default);
    fb.Terminate(new BrIf(valEq, bbLatch.Id, bbMismatch.Id));

    fb.SwitchTo(bbMismatch);
    fb.Terminate(new Ret(fb.EmitConstBool(false)));

    fb.SwitchTo(bbLatch);
    IrTempId nextI = fb.AddTemp(_types.Int);
    fb.Emit(new Add(nextI, curI, fb.EmitConstInt(1)));
    fb.Emit(new StoreSlot(idxSlot, nextI));
    fb.Terminate(new Br(bbHead.Id));

    fb.SwitchTo(bbTrue);
    fb.Terminate(new Ret(fb.EmitConstBool(true)));
  }

  private static string SanitizeTypeName(string name) {
    var sb = new StringBuilder(name.Length);
    foreach (char ch in name) {
      sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
    }

    return sb.ToString();
  }

  private IrOperand EmitMembership(
      FunctionBuilder fb,
      IrOperand left,
      IrOperand right,
      bool negated,
      SourceSpan span) {
    IrType rhsType = _types.GetType(fb.GetOperandType(right));
    IrTempId contains = fb.AddTemp(_types.Bool);

    switch (rhsType.Kind) {
      case IrTypeKind.Vector:
        fb.Emit(new VecContains(contains, right, left, span));
        break;

      case IrTypeKind.Map:
        fb.Emit(new MapContains(contains, right, left, span));
        break;

      case IrTypeKind.Set:
        fb.Emit(new SetContains(contains, right, left, span));
        break;

      case IrTypeKind.Pool:
        fb.Emit(new PoolContains(contains, right, left, span));
        break;

      case IrTypeKind.String:
        fb.Emit(new CallRuntime(contains, IrRuntimeOp.StrContains, [right, left], AllocRegion.Stack, span));
        break;

      default:
        throw new InvalidOperationException($"Unsupported 'in' target type '{rhsType.Name}'.");
    }

    if (!negated) {
      return contains;
    }

    IrTempId neg = fb.AddTemp(_types.Bool);
    fb.Emit(new Not(neg, contains, span));
    return neg;
  }

  private IrOperand? LowerIndexRead(FunctionBuilder fb, IndexExpressionSyntax index) {
    IrOperand? targetOpt = LowerExpression(fb, index.Target);
    if (fb.IsTerminated || targetOpt is not { } target) {
      return null;
    }

    IrOperand? keyOpt = LowerExpression(fb, index.Index);
    if (fb.IsTerminated || keyOpt is not { } key) {
      return null;
    }

    IrType targetType = _types.GetType(fb.GetOperandType(target));
    switch (targetType) {
      case IrVectorType vt: {
          IrTempId dest = fb.AddTemp(vt.ElementType);
          fb.Emit(new VecGet(dest, target, key, Unchecked: false, index.Span));
          return dest;
        }

      case IrPrimitiveType { Kind: IrTypeKind.String }: {
          IrTempId dest = fb.AddTemp(_types.Rune);
          fb.Emit(new StrGetRune(dest, target, key, index.Span));
          return dest;
        }

      case IrMapType mt: {
          IrTempId dest = fb.AddTemp(mt.ValueType);
          fb.Emit(new MapGet(dest, target, key, index.Span));
          return dest;
        }

      case IrPoolType pt: {
          // D-185 / POOL-E1: pass the pool's first identifier as written.
          string poolName = ExtractFirstIdentifier(fb.Model, index.Target);
          IrTempId dest = fb.AddTemp(pt.ElementType);
          fb.Emit(new PoolGetOrPanic(dest, target, key, poolName, index.Span));
          return dest;
        }

      default:
        throw new InvalidOperationException($"Unsupported index target type '{targetType.Name}'.");
    }
  }

  private IrOperand? LowerMemberRead(FunctionBuilder fb, MemberAccessExpressionSyntax member) {
    TypeSymbol targetSymType = fb.GetNodeType(member.Target);

    IrOperand? targetOpt = LowerExpression(fb, member.Target);
    if (fb.IsTerminated || targetOpt is not { } target) {
      return null;
    }

    if (targetSymType is InterfaceType ifaceType) {
      return LowerInterfaceMemberRead(fb, member, ifaceType, target);
    }

    string memberName = NameOf(fb.Model, member.MemberToken);

    if (targetSymType is StructType structType) {
      Symbol? memberSym = fb.Model.GetReferencedSymbol(member);
      if (memberSym is FieldSymbol field) {
        return EmitStructFieldGet(fb, target, structType.Definition, field, member.Span);
      }

      if (memberSym is MethodSymbol boundMethod) {
        // For @@Generic[T is Bound], memberSym may be the interface's MethodSymbol; resolve to concrete struct member!
        if (boundMethod.ContainingType is InterfaceSymbol) {
          if (FindStructMethodByName(structType.Definition, memberName) is { } concreteMethod) {
            var fnType = (FunctionType) fb.GetNodeType(member);
            return LowerBoundMethodValue(fb, target, structType, concreteMethod, fnType, member.Span);
          }

          if (FindStructFieldByName(structType.Definition, memberName) is { } callableField) {
            return EmitStructFieldGet(fb, target, structType.Definition, callableField, member.Span);
          }
        } else {
          var fnType = (FunctionType) fb.GetNodeType(member);
          return LowerBoundMethodValue(fb, target, structType, boundMethod, fnType, member.Span);
        }
      }

      // GEN-8 / GEN-9: bounded generic property access (`x:prop` where `T is Bound`) has no referenced FieldSymbol.
      if (FindStructFieldByName(structType.Definition, memberName) is { } resolvedField) {
        return EmitStructFieldGet(fb, target, structType.Definition, resolvedField, member.Span);
      }

      if (FindStructMethodByName(structType.Definition, memberName) is { } resolvedMethod) {
        var fnType = (FunctionType) fb.GetNodeType(member);
        return LowerBoundMethodValue(fb, target, structType, resolvedMethod, fnType, member.Span);
      }
    }

    // Stdlib read-only properties (`len`, `length`, `cap`, `pattern`).
    IrType targetIrType = _types.GetType(fb.GetOperandType(target));
    switch (targetIrType.Kind) {
      case IrTypeKind.Vector when memberName is "len" or "length": {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new VecLen(d, target, member.Span));
          return d;
        }

      case IrTypeKind.Vector when memberName is "cap" or "capacity": {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new VecCap(d, target, member.Span));
          return d;
        }

      case IrTypeKind.String when memberName is "len" or "length": {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new StrLen(d, target, member.Span));
          return d;
        }

      case IrTypeKind.Map when memberName is "len" or "length": {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new MapLen(d, target, member.Span));
          return d;
        }

      case IrTypeKind.Set when memberName is "len" or "length": {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new SetLen(d, target, member.Span));
          return d;
        }

      case IrTypeKind.Pool when memberName is "len" or "length": {
          IrTempId d = fb.AddTemp(_types.Int);
          fb.Emit(new PoolLen(d, target, member.Span));
          return d;
        }

      case IrTypeKind.Regex when memberName == "pattern": {
          IrTempId d = fb.AddTemp(_types.String);
          fb.Emit(new CallRuntime(d, IrRuntimeOp.RegexPattern, [target], AllocRegion.Stack, member.Span));
          return d;
        }

      default:
        if (fb.Model.GetType(member) is { } rawMemType
            && fb.ResolveType(rawMemType) is FunctionType stdlibFnType
            && TryLowerBoundStdlibMethodValue(fb, target, targetSymType, memberName, stdlibFnType, member.Span) is { } boundStdlib) {
          return boundStdlib;
        }

        throw new InvalidOperationException(
            $"Unsupported member access '{memberName}' on '{targetIrType.Name}'.");
    }
  }

  private IrOperand LowerStaticRead(FunctionBuilder fb, StaticAccessExpressionSyntax access) {
    Symbol sym = fb.Model.GetReferencedSymbol(access)
        ?? throw new InvalidOperationException($"Unresolved static access '{NameOf(fb.Model, access.MemberToken)}'.");

    switch (sym) {
      case EnumMemberSymbol enumMember: {
          TypeSymbol enumType = fb.Model.GetType(access) is { } rawEnum
              ? fb.ResolveType(rawEnum)
              : _compilation.Types.GetEnum(enumMember.ContainingEnum);
          IrTypeId enumIrType = InternType(enumType);
          IrTempId dest = fb.AddTemp(enumIrType);
          fb.Emit(new ConstEnum(dest, enumIrType, enumMember.Ordinal, access.Span));
          return dest;
        }

      case UnionVariantSymbol variant: {
          // Payload-less variant (e.g., Option::None, Result::Success on Result[void, E], List::Nil).
          TypeSymbol unionType = fb.GetNodeType(access);
          IrTypeId unionIrType = InternType(unionType);
          IrTempId dest = fb.AddTemp(unionIrType);
          fb.Emit(new UnionNew(dest, variant.TagOrdinal, [], UnionAllocRegion(unionIrType), access.Span));
          return dest;
        }

      case LocalSymbol modVal:
        return EmitSymbolValueRead(fb, modVal, access.Span);

      case FunctionSymbol fnSym:
        return LowerFunctionAsValue(fb, fnSym, access.Span);

      case MethodSymbol methodSym:
        return LowerStaticMethodAsValue(fb, access, methodSym);

      default:
        throw new InvalidOperationException($"Unsupported static access symbol '{sym.GetType().Name}'.");
    }
  }

  private IrOperand? LowerStructLiteral(FunctionBuilder fb, StructLiteralExpressionSyntax literal) {
    var structType = (StructType) fb.GetNodeType(literal);
    IrTypeId irStructId = InternType(structType);

    // Evaluate all field initializers in source textual order first.
    var fieldValues = new Dictionary<string, IrOperand>(literal.Fields.Length, StringComparer.Ordinal);
    foreach (StructFieldInitSyntax fieldInit in literal.Fields) {
      string fieldName = NameOf(fb.Model, fieldInit.NameToken);
      IrOperand? evaluated;
      if (fieldInit.Value is { } valExpr) {
        evaluated = LowerExpression(fb, valExpr);
      } else {
        // STRUCT-15 shorthand: `Point { x }` -> GetShorthandValueSymbol(fieldInit).
        Symbol shorthandSym = fb.Model.GetShorthandValueSymbol(fieldInit)
            ?? throw new InvalidOperationException($"Missing shorthand symbol for struct field '{fieldName}'.");
        IrOperand rawVal = EmitSymbolValueRead(fb, shorthandSym, fieldInit.Span);
        evaluated = ApplyConversion(fb, fieldInit, rawVal, fieldInit.Span);
      }

      if (fb.IsTerminated || evaluated is not { } v) {
        return null;
      }

      fieldValues[fieldName] = v;
    }

    return BuildStructNewRecursive(fb, irStructId, structType.Definition, fieldValues, literal.Span);
  }

  private IrOperand BuildStructNewRecursive(
      FunctionBuilder fb,
      IrTypeId irStructId,
      StructSymbol structSym,
      Dictionary<string, IrOperand> fieldValues,
      SourceSpan span) {
    var irStruct = (IrStructType) _types.GetType(irStructId);
    var args = ImmutableArray.CreateBuilder<IrOperand>(irStruct.Fields.Length);

    if (structSym.EmbeddedStructs.IsEmpty) {
      foreach (IrStructField f in irStruct.Fields) {
        args.Add(CoerceOperandToTargetType(fb, fieldValues[f.Name], f.Type, span));
      }
    } else {
      for (int i = 0; i < structSym.Fields.Length; i++) {
        FieldSymbol directField = structSym.Fields[i];
        args.Add(CoerceOperandToTargetType(fb, fieldValues[directField.Name], irStruct.Fields[i].Type, span));
      }

      for (int i = 0; i < structSym.EmbeddedStructs.Length; i++) {
        StructSymbol emb = structSym.EmbeddedStructs[i];
        IrTypeId embIrId = irStruct.Fields[structSym.Fields.Length + i].Type;
        args.Add(BuildStructNewRecursive(fb, embIrId, emb, fieldValues, span));
      }
    }

    IrTempId dest = fb.AddTemp(irStructId);
    fb.Emit(new StructNew(dest, args.MoveToImmutable(), span));
    return dest;
  }

  private IrOperand? LowerTupleLiteral(FunctionBuilder fb, TupleLiteralExpressionSyntax tupleLit) {
    var tupleType = (TupleType) fb.GetNodeType(tupleLit);
    IrTypeId irTupleId = InternType(tupleType);
    var irTuple = (IrTupleType) _types.GetType(irTupleId);

    var valuesByLabel = new Dictionary<string, IrOperand>(tupleLit.Fields.Length, StringComparer.Ordinal);
    foreach (TupleFieldSyntax field in tupleLit.Fields) {
      string label = NameOf(fb.Model, field.NameToken);
      IrOperand? evaluated;
      if (field.Value is { } valExpr) {
        evaluated = LowerExpression(fb, valExpr);
      } else {
        Symbol sym = fb.Model.GetReferencedSymbol(field)
            ?? throw new InvalidOperationException($"Missing shorthand symbol for tuple field '{label}'.");
        IrOperand rawVal = EmitSymbolValueRead(fb, sym, field.Span);
        evaluated = ApplyConversion(fb, field, rawVal, field.Span);
      }

      if (fb.IsTerminated || evaluated is not { } v) {
        return null;
      }

      valuesByLabel[label] = v;
    }

    // Assemble in canonical TupleType.Fields order (Ordinal sort by label, TUP-1..10).
    var ordered = ImmutableArray.CreateBuilder<IrOperand>(irTuple.Fields.Length);
    foreach (IrTupleField f in irTuple.Fields) {
      ordered.Add(valuesByLabel[f.Label]);
    }

    IrTempId dest = fb.AddTemp(irTupleId);
    fb.Emit(new TupleNew(dest, ordered.MoveToImmutable(), tupleLit.Span));
    return dest;
  }

  private IrOperand? LowerVectorLiteral(FunctionBuilder fb, VectorLiteralExpressionSyntax vecLit) {
    TypeSymbol vecType = fb.GetNodeType(vecLit);
    IrTypeId irVecId = InternType(vecType);

    var elements = ImmutableArray.CreateBuilder<IrOperand>(vecLit.Elements.Length);
    foreach (ExpressionSyntax elem in vecLit.Elements) {
      IrOperand? val = LowerExpression(fb, elem);
      if (fb.IsTerminated || val is not { } v) {
        return null;
      }

      elements.Add(v);
    }

    IrTempId dest = fb.AddTemp(irVecId);
    fb.Emit(new VecNew(dest, elements.MoveToImmutable(), AllocRegion.Heap, vecLit.Span));
    return dest;
  }

  private IrOperand? LowerVectorInitializer(FunctionBuilder fb, VectorInitializerExpressionSyntax vecInit) {
    TypeSymbol vecType = fb.GetNodeType(vecInit);
    IrTypeId irVecId = InternType(vecType);

    if (vecInit.Properties.IsEmpty) {
      IrTempId empty = fb.AddTemp(irVecId);
      fb.Emit(new VecNew(empty, [], AllocRegion.Heap, vecInit.Span));
      return empty;
    }

    ExpressionSyntax? lenExpr = null;
    ExpressionSyntax? capExpr = null;
    VectorPropertySyntax? initProp = null;

    foreach (VectorPropertySyntax prop in vecInit.Properties) {
      string propName = NameOf(fb.Model, prop.NameToken);
      switch (propName) {
        case "len":
          lenExpr = prop.Value;
          break;
        case "cap":
          capExpr = prop.Value;
          break;
        case "init":
          initProp = prop;
          break;
      }
    }

    // Evaluate len and cap once, then perform runtime clamping with [WARNING] to stderr (VEC-7..15).
    IrSlotId lenSlot = fb.AddSlot("vec_len", _types.Int, isMutSlot: true);
    IrSlotId capSlot = fb.AddSlot("vec_cap", _types.Int, isMutSlot: true);

    if (lenExpr is not null) {
      IrOperand? rawLen = LowerExpression(fb, lenExpr);
      if (fb.IsTerminated || rawLen is not { } rl) {
        return null;
      }

      fb.Emit(new StoreSlot(lenSlot, rl, lenExpr.Span));

      // VEC-11, VEC-14, VEC-15: clamp negative runtime len to 0 with [WARNING].
      IrTempId isNegLen = fb.AddTemp(_types.Bool);
      fb.Emit(new Cmp(isNegLen, IrCmpOp.Lt, rl, fb.EmitConstInt(0, lenExpr.Span), lenExpr.Span));
      IrBasicBlock bbNegLen = fb.NewBlock();
      IrBasicBlock bbAfterLen = fb.NewBlock();
      fb.Terminate(new BrIf(isNegLen, bbNegLen.Id, bbAfterLen.Id, lenExpr.Span));

      fb.SwitchTo(bbNegLen);
      IrTempId warnLen = fb.EmitConstString(
          "[WARNING] Vector 'len' was negative at runtime; clamped to 0.",
          lenExpr.Span);
      fb.Emit(new CallRuntime(null, IrRuntimeOp.Eprintln, [warnLen], AllocRegion.Stack, lenExpr.Span));
      fb.Emit(new StoreSlot(lenSlot, fb.EmitConstInt(0, lenExpr.Span), lenExpr.Span));
      fb.Terminate(new Br(bbAfterLen.Id, lenExpr.Span));

      fb.SwitchTo(bbAfterLen);
    } else {
      fb.Emit(new StoreSlot(lenSlot, fb.EmitConstInt(0, vecInit.Span), vecInit.Span));
    }

    if (capExpr is not null) {
      IrOperand? rawCap = LowerExpression(fb, capExpr);
      if (fb.IsTerminated || rawCap is not { } rc) {
        return null;
      }

      fb.Emit(new StoreSlot(capSlot, rc, capExpr.Span));

      // Clamp negative cap to 0 with [WARNING] (VEC-14, VEC-15).
      IrTempId isNegCap = fb.AddTemp(_types.Bool);
      fb.Emit(new Cmp(isNegCap, IrCmpOp.Lt, rc, fb.EmitConstInt(0, capExpr.Span), capExpr.Span));
      IrBasicBlock bbNegCap = fb.NewBlock();
      IrBasicBlock bbCheckCapLen = fb.NewBlock();
      fb.Terminate(new BrIf(isNegCap, bbNegCap.Id, bbCheckCapLen.Id, capExpr.Span));

      fb.SwitchTo(bbNegCap);
      IrTempId warnCap = fb.EmitConstString(
          "[WARNING] Vector 'cap' was negative at runtime; clamped to 0.",
          capExpr.Span);
      fb.Emit(new CallRuntime(null, IrRuntimeOp.Eprintln, [warnCap], AllocRegion.Stack, capExpr.Span));
      fb.Emit(new StoreSlot(capSlot, fb.EmitConstInt(0, capExpr.Span), capExpr.Span));
      fb.Terminate(new Br(bbCheckCapLen.Id, capExpr.Span));

      fb.SwitchTo(bbCheckCapLen);
      // VEC-12, VEC-15: clamp cap < len to len with [WARNING].
      IrTempId curCap = fb.AddTemp(_types.Int);
      IrTempId curLen = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(curCap, capSlot, capExpr.Span));
      fb.Emit(new LoadSlot(curLen, lenSlot, capExpr.Span));
      IrTempId capLtLen = fb.AddTemp(_types.Bool);
      fb.Emit(new Cmp(capLtLen, IrCmpOp.Lt, curCap, curLen, capExpr.Span));
      IrBasicBlock bbClampCap = fb.NewBlock();
      IrBasicBlock bbAfterCap = fb.NewBlock();
      fb.Terminate(new BrIf(capLtLen, bbClampCap.Id, bbAfterCap.Id, capExpr.Span));

      fb.SwitchTo(bbClampCap);
      IrTempId warnCapLen = fb.EmitConstString(
          "[WARNING] Vector 'cap' was less than 'len' at runtime; clamped to 'len'.",
          capExpr.Span);
      fb.Emit(new CallRuntime(null, IrRuntimeOp.Eprintln, [warnCapLen], AllocRegion.Stack, capExpr.Span));
      fb.Emit(new StoreSlot(capSlot, curLen, capExpr.Span));
      fb.Terminate(new Br(bbAfterCap.Id, capExpr.Span));

      fb.SwitchTo(bbAfterCap);
    } else {
      IrTempId curLen = fb.AddTemp(_types.Int);
      fb.Emit(new LoadSlot(curLen, lenSlot, vecInit.Span));
      fb.Emit(new StoreSlot(capSlot, curLen, vecInit.Span));
    }

    // VEC-13: minimum capacity of 4 for dynamically sized vectors.
    IrTempId capBeforeMin = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(capBeforeMin, capSlot, vecInit.Span));
    IrTempId capLt4 = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(capLt4, IrCmpOp.Lt, capBeforeMin, fb.EmitConstInt(4, vecInit.Span), vecInit.Span));
    IrBasicBlock bbMin4 = fb.NewBlock();
    IrBasicBlock bbAlloc = fb.NewBlock();
    fb.Terminate(new BrIf(capLt4, bbMin4.Id, bbAlloc.Id, vecInit.Span));

    fb.SwitchTo(bbMin4);
    fb.Emit(new StoreSlot(capSlot, fb.EmitConstInt(4, vecInit.Span), vecInit.Span));
    fb.Terminate(new Br(bbAlloc.Id, vecInit.Span));

    fb.SwitchTo(bbAlloc);
    IrTempId finalCap = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(finalCap, capSlot, vecInit.Span));
    IrTempId vecVal = fb.AddTemp(irVecId);
    fb.Emit(new VecWithCap(vecVal, finalCap, AllocRegion.Heap, vecInit.Span));

    if (initProp is null) {
      return vecVal;
    }

    IrSlotId vecSlot = fb.AddSlot("vec_init", irVecId, isMutSlot: true);
    fb.Emit(new StoreSlot(vecSlot, vecVal, vecInit.Span));
    IrSlotId idxSlot = fb.AddSlot("it", _types.Int, isMutSlot: true);
    fb.Emit(new StoreSlot(idxSlot, fb.EmitConstInt(0, initProp.Span), initProp.Span));

    IrBasicBlock bbHead = fb.NewBlock();
    IrBasicBlock bbBody = fb.NewBlock();
    IrBasicBlock bbExit = fb.NewBlock();
    fb.Terminate(new Br(bbHead.Id, initProp.Span));

    fb.SwitchTo(bbHead);
    IrTempId curIt = fb.AddTemp(_types.Int, "it");
    IrTempId targetLen = fb.AddTemp(_types.Int);
    fb.Emit(new LoadSlot(curIt, idxSlot, initProp.Span));
    fb.Emit(new LoadSlot(targetLen, lenSlot, initProp.Span));
    IrTempId loopCond = fb.AddTemp(_types.Bool);
    fb.Emit(new Cmp(loopCond, IrCmpOp.Lt, curIt, targetLen, initProp.Span));
    fb.Terminate(new BrIf(loopCond, bbBody.Id, bbExit.Id, initProp.Span));

    fb.SwitchTo(bbBody);
    Scope? initScope = fb.Model.GetScope(initProp);
    LocalSymbol? itSym = initScope is not null
        && initScope.TryLookupLocal("it", out Symbol? foundIt)
        && foundIt is LocalSymbol foundLocal
            ? foundLocal
            : null;
    if (itSym is not null) {
      fb.LocalTemps[itSym] = curIt;
    }

    IrOperand? elemVal;
    TypeSymbol? initValType = fb.Model.GetType(initProp.Value) is { } rawInitT ? fb.ResolveType(rawInitT) : null;
    if (initValType is FunctionType) {
      // Optional function reference `init: make_item` (BodyChecker.Collections.cs:117-134).
      if (initProp.Value is IdentifierExpressionSyntax fnId
          && fb.Model.GetReferencedSymbol(fnId) is FunctionSymbol fnSym
          && !fb.CapturingLocals.ContainsKey(fnSym)
          && (fb.LocalFuncIds.TryGetValue(fnSym, out IrFuncId fnIdRef) || _funcIds.TryGetValue(fnSym, out fnIdRef))) {
        var irVec = (IrVectorType) _types.GetType(irVecId);
        IrTempId called = fb.AddTemp(irVec.ElementType);
        fb.Emit(new CallDirect(called, fnIdRef, [curIt], initProp.Value.Span));
        elemVal = called;
      } else {
        IrOperand? closOpt = LowerExpression(fb, initProp.Value);
        if (fb.IsTerminated || closOpt is not { } clos) {
          elemVal = null;
        } else {
          elemVal = EmitClosureCall(fb, clos, [curIt], initProp.Value.Span);
        }
      }
    } else {
      elemVal = LowerExpression(fb, initProp.Value);
    }

    if (itSym is not null) {
      fb.LocalTemps.Remove(itSym);
    }

    if (!fb.IsTerminated && elemVal is { } ev) {
      IrTempId vecPtr = fb.AddTemp(_types.GetPtr(irVecId));
      fb.Emit(new AddrLocal(vecPtr, vecSlot, initProp.Span));
      fb.Emit(new VecPush(vecPtr, ev, initProp.Span));
      IrTempId nextIt = fb.AddTemp(_types.Int);
      fb.Emit(new Add(nextIt, curIt, fb.EmitConstInt(1, initProp.Span), initProp.Span));
      fb.Emit(new StoreSlot(idxSlot, nextIt, initProp.Span));
      fb.Terminate(new Br(bbHead.Id, initProp.Span));
    }

    fb.SwitchTo(bbExit);
    IrTempId doneVec = fb.AddTemp(irVecId);
    fb.Emit(new LoadSlot(doneVec, vecSlot, vecInit.Span));
    return doneVec;
  }

  private IrOperand? LowerMapLiteral(FunctionBuilder fb, MapLiteralExpressionSyntax mapLit) {
    TypeSymbol mapType = fb.GetNodeType(mapLit);
    IrTypeId irMapId = InternType(mapType);

    var entries = ImmutableArray.CreateBuilder<(IrOperand Key, IrOperand Value)>(mapLit.Entries.Length);
    foreach (MapEntrySyntax entry in mapLit.Entries) {
      IrOperand? keyOpt = LowerExpression(fb, entry.Key);
      if (fb.IsTerminated || keyOpt is not { } k) {
        return null;
      }

      IrOperand? valOpt = LowerExpression(fb, entry.Value);
      if (fb.IsTerminated || valOpt is not { } v) {
        return null;
      }

      entries.Add((k, v));
    }

    IrTempId dest = fb.AddTemp(irMapId);
    fb.Emit(new MapNew(dest, entries.MoveToImmutable(), AllocRegion.Heap, mapLit.Span));
    return dest;
  }

  private IrOperand? LowerSetLiteral(FunctionBuilder fb, SetLiteralExpressionSyntax setLit) {
    TypeSymbol setType = fb.GetNodeType(setLit);
    IrTypeId irSetId = InternType(setType);

    var elems = ImmutableArray.CreateBuilder<IrOperand>(setLit.Elements.Length);
    foreach (ExpressionSyntax elem in setLit.Elements) {
      IrOperand? valOpt = LowerExpression(fb, elem);
      if (fb.IsTerminated || valOpt is not { } v) {
        return null;
      }

      elems.Add(v);
    }

    IrTempId dest = fb.AddTemp(irSetId);
    fb.Emit(new SetNew(dest, elems.MoveToImmutable(), AllocRegion.Heap, setLit.Span));
    return dest;
  }

  private IrOperand LowerPoolExpression(FunctionBuilder fb, PoolExpressionSyntax poolExpr) {
    TypeSymbol poolType = fb.GetNodeType(poolExpr);
    IrTypeId irPoolId = InternType(poolType);
    IrTempId dest = fb.AddTemp(irPoolId);
    fb.Emit(new PoolNew(dest, AllocRegion.Heap, poolExpr.Span));
    return dest;
  }

  private IrOperand? LowerBubble(FunctionBuilder fb, BubbleExpressionSyntax bubble) {
    // ERR-7..9, ERR-23: `?` on Result[T, E] or Option[T].
    IrOperand? operandOpt = LowerExpression(fb, bubble.Expression);
    if (fb.IsTerminated || operandOpt is not { } unionVal) {
      return null;
    }

    var operandUnionType = (UnionType) fb.GetNodeType(bubble.Expression);
    bool isResult = ReferenceEquals(operandUnionType.Definition, _compilation.Prelude.ResultUnion);

    IrTempId tag = fb.AddTemp(_types.Int, "tag");
    fb.Emit(new UnionTag(tag, unionVal, bubble.Span));

    IrBasicBlock bbOk = fb.NewBlock();
    IrBasicBlock bbErr = fb.NewBlock();
    fb.Terminate(new Switch(tag, [(0, bbOk.Id), (1, bbErr.Id)], null, bubble.Span));

    fb.SwitchTo(bbErr);
    bool inInlinedLambda = fb.InlineReturnStack.Count > 0;
    InlineReturnTarget? inlineTarget = inInlinedLambda ? fb.InlineReturnStack.Peek() : null;
    IrTypeId enclosingRetIr = inlineTarget?.ResultSlot is { } inSlot
        ? fb.Function.Slots[inSlot.Index].Type
        : fb.Function.ReturnType;

    IrTempId errReturnVal;
    if (isResult) {
      IrTypeId errType = InternType(operandUnionType.TypeArguments[1]);
      IrTempId errVal = fb.AddTemp(errType, "err");
      fb.Emit(new UnionField(errVal, unionVal, VariantIndex: 1, FieldIndex: 0, bubble.Span));

      errReturnVal = fb.AddTemp(enclosingRetIr);
      fb.Emit(new UnionNew(errReturnVal, VariantIndex: 1, [errVal], UnionAllocRegion(enclosingRetIr), bubble.Span));
    } else {
      errReturnVal = fb.AddTemp(enclosingRetIr);
      fb.Emit(new UnionNew(errReturnVal, VariantIndex: 1, [], UnionAllocRegion(enclosingRetIr), bubble.Span));
    }

    if (inlineTarget is { } target) {
      if (target.ResultSlot is { } slot) {
        fb.Emit(new StoreSlot(slot, errReturnVal, bubble.Span));
      }

      fb.Terminate(new Br(target.ExitBlock, bubble.Span));
    } else {
      fb.Terminate(new Ret(errReturnVal, bubble.Span));
    }

    fb.SwitchTo(bbOk);
    TypeSymbol successType = operandUnionType.TypeArguments[0];
    if (successType.IsVoid) {
      return null;
    }

    if (successType.IsNever) {
      fb.Terminate(new Unreachable(bubble.Span));
      return null;
    }

    IrTypeId okIrType = InternType(successType);
    IrTempId okVal = fb.AddTemp(okIrType);
    fb.Emit(new UnionField(okVal, unionVal, VariantIndex: 0, FieldIndex: 0, bubble.Span));
    return okVal;
  }

  private IrOperand? LowerRecovery(FunctionBuilder fb, RecoveryExpressionSyntax recovery) {
    // ERR-10..14: `expr or { ... }` recovery block.
    IrOperand? operandOpt = LowerExpression(fb, recovery.Expression);
    if (fb.IsTerminated || operandOpt is not { } unionVal) {
      return null;
    }

    var operandUnionType = (UnionType) fb.GetNodeType(recovery.Expression);
    bool isResult = ReferenceEquals(operandUnionType.Definition, _compilation.Prelude.ResultUnion);
    TypeSymbol resType = fb.Model.GetType(recovery) is { } rawRec
        ? fb.ResolveType(rawRec)
        : operandUnionType.TypeArguments[0];

    bool hasValue = !resType.IsVoid && !resType.IsNever;
    IrTypeId? resIrType = hasValue ? InternType(resType) : null;
    IrSlotId? resSlot = hasValue ? fb.AddSlot("or_res", resIrType!.Value, isMutSlot: true) : null;

    IrTempId tag = fb.AddTemp(_types.Int, "tag");
    fb.Emit(new UnionTag(tag, unionVal, recovery.Span));

    IrBasicBlock bbOk = fb.NewBlock();
    IrBasicBlock bbErr = fb.NewBlock();
    IrBasicBlock bbMerge = fb.NewBlock();
    fb.Terminate(new Switch(tag, [(0, bbOk.Id), (1, bbErr.Id)], null, recovery.Span));

    bool mergeReachable = false;

    fb.SwitchTo(bbOk);
    TypeSymbol successType = operandUnionType.TypeArguments[0];
    if (successType.IsNever) {
      fb.Terminate(new Unreachable(recovery.Span));
    } else {
      if (hasValue && !successType.IsVoid) {
        IrTempId okVal = fb.AddTemp(resIrType!.Value);
        fb.Emit(new UnionField(okVal, unionVal, VariantIndex: 0, FieldIndex: 0, recovery.Span));
        fb.Emit(new StoreSlot(resSlot!.Value, okVal, recovery.Span));
      }

      fb.Terminate(new Br(bbMerge.Id, recovery.Span));
      mergeReachable = true;
    }

    fb.SwitchTo(bbErr);
    Scope? recScope = fb.Model.GetScope(recovery.Block);
    LocalSymbol? errSym = recScope is not null
        && recScope.TryLookupLocal("err", out Symbol? foundErr)
        && foundErr is LocalSymbol errLocal
            ? errLocal
            : null;
    if (errSym is not null) {
      IrOperand errOp;
      if (isResult) {
        IrTypeId errIrType = InternType(operandUnionType.TypeArguments[1]);
        IrTempId errVal = fb.AddTemp(errIrType, "err");
        fb.Emit(new UnionField(errVal, unionVal, VariantIndex: 1, FieldIndex: 0, recovery.Span));
        errOp = errVal;
      } else {
        // ERR-12: Option::None binds `err` to the string "None".
        errOp = fb.EmitConstString("None", recovery.Span, "err");
      }

      fb.LocalTemps[errSym] = errOp;
    }

    fb.YieldStack.Push(new YieldTarget(resSlot, bbMerge.Id, hasValue ? resType : null));
    bool yielded = false;
    LowerBlockItems(fb, recovery.Block.Items);
    fb.YieldStack.Pop();

    if (errSym is not null) {
      fb.LocalTemps.Remove(errSym);
    }

    if (!fb.IsTerminated) {
      fb.Terminate(new Br(bbMerge.Id, recovery.Block.Span));
      mergeReachable = true;
    } else {
      // Check whether any block inside recovery jumped to bbMerge via `yield`.
      foreach (IrBasicBlock blk in fb.Function.Blocks) {
        if (blk.Terminator is Br { Target: var t } && t == bbMerge.Id) {
          yielded = true;
          break;
        }
      }
    }

    mergeReachable = mergeReachable || yielded;
    fb.SwitchTo(bbMerge);
    if (!mergeReachable) {
      fb.Terminate(new Unreachable(recovery.Span));
      return null;
    }

    if (!hasValue) {
      return null;
    }

    IrTempId loaded = fb.AddTemp(resIrType!.Value);
    fb.Emit(new LoadSlot(loaded, resSlot!.Value, recovery.Span));
    return loaded;
  }
}
