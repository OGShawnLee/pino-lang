using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  private readonly Dictionary<BoundMethodKey, (IrFuncId FuncId, IrTypeId EnvTypeId)> _boundMethodTrampolines = [];
  private readonly Dictionary<(IrTypeId RecvType, IntrinsicId Intrinsic), (IrFuncId FuncId, IrTypeId EnvTypeId)> _boundStdlibTrampolines = [];
  private int _lambdaCounter;

  private sealed record CapturingLocalInfo(
      IrFuncId FuncId,
      IrTypeId EnvTypeId,
      ImmutableArray<CaptureEntry> Captures);

  // ---------------------------------------------------------------------------
  // Pre-lowering capture scan: mark D-056 mutable-captured locals as `is_cell`
  // ---------------------------------------------------------------------------

  private void PrepareCallableScope(FunctionBuilder fb, SyntaxNode bodyRoot, bool registerLocalFunctions = true) {
    ScanCapturedMutableLocals(fb, bodyRoot);
    if (registerLocalFunctions) {
      RegisterBodyLocalFunctions(fb, bodyRoot);
    }
  }

  private void ScanCapturedMutableLocals(FunctionBuilder fb, SyntaxNode bodyRoot) {
    var stack = new Stack<SyntaxNode>();
    foreach (SyntaxElement elem in bodyRoot.GetChildren()) {
      if (elem.IsNode) {
        stack.Push(elem.AsNode());
      }
    }

    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();

      // Do not descend into separate nested struct/interface/enum/union declarations.
      if (node is StructDeclarationSyntax or InterfaceDeclarationSyntax or EnumDeclarationSyntax or UnionDeclarationSyntax) {
        continue;
      }

      // A callable node's CaptureList marks any outer mutable LocalSymbol (D-056 / FN-21).
      // Skip the CaptureList of callbacks that will be inlined directly into a loop (no closure created),
      // while still descending into their body in case a deeper non-inlined closure captures a local.
      bool isCallable = node is LambdaExpressionSyntax or FunctionDeclarationSyntax
          || fb.Model.GetConversion(node)?.Kind == ConversionKind.ImplicitItLambda;

      if (isCallable && !IsInlinedCallbackArgumentNode(fb, node)) {
        CaptureList? captures = fb.Model.GetCaptures(node);
        if (captures is not null && !captures.IsEmpty) {
          foreach (CaptureEntry entry in captures.Entries) {
            if (entry.IsMutable
                && entry.Symbol is LocalSymbol local
                && !IsSelfLocal(local)
                && !_globalIds.ContainsKey(local)) {
              fb.BoxedCellSymbols.Add(local);
            }
          }
        }
      }

      foreach (SyntaxElement child in node.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }
  }

  private bool IsInlinedCallbackArgumentNode(FunctionBuilder fb, SyntaxNode node) {
    // Unwrap parenthesized wrappers up to the ArgumentSyntax.
    SyntaxNode? current = node;
    while (current is not null && fb.Model.GetParent(current) is ParenthesizedExpressionSyntax paren) {
      current = paren;
    }

    if (current is null
        || fb.Model.GetParent(current) is not ArgumentSyntax arg
        || fb.Model.GetParent(arg) is not CallExpressionSyntax call
        || call.Arguments.Length != 1
        || !ReferenceEquals(call.Arguments[0], arg)) {
      return false;
    }

    CallResolution? res = fb.Model.GetCallResolution(call);
    return res is { Kind: CallKind.Intrinsic, Intrinsic: { } intr } && IsCallbackIntrinsic(intr);
  }

  private ImmutableArray<CaptureEntry> GetEffectiveCaptures(FunctionBuilder fb, SyntaxNode callableNode) {
    CaptureList? captures = fb.Model.GetCaptures(callableNode);
    if (captures is null || captures.IsEmpty) {
      return [];
    }

    var builder = ImmutableArray.CreateBuilder<CaptureEntry>(captures.Entries.Length);
    foreach (CaptureEntry entry in captures.Entries) {
      // Top-level globals are addressed via AddrGlobal directly, never boxed or placed in PinoEnv.
      if (entry.Symbol is LocalSymbol local && _globalIds.ContainsKey(local)) {
        continue;
      }

      builder.Add(entry);
    }

    return builder.ToImmutable();
  }

  // ---------------------------------------------------------------------------
  // Local nested functions (hoisted & capturing)
  // ---------------------------------------------------------------------------

  private void RegisterBodyLocalFunctions(FunctionBuilder fb, SyntaxNode bodyRoot) {
    var localDecls = new List<(FunctionDeclarationSyntax Decl, FunctionSymbol Sym)>();
    CollectDirectLocalFunctions(fb.Model, bodyRoot, localDecls);
    if (localDecls.Count == 0) {
      return;
    }

    var pendingLocalBodies = new List<(FunctionDeclarationSyntax Decl, FunctionSymbol Sym, IrFuncId FuncId, CapturingLocalInfo? CaptureInfo)>();

    foreach ((FunctionDeclarationSyntax decl, FunctionSymbol fnSym) in localDecls) {
      if (!fnSym.TypeParameters.IsEmpty) {
        // Generic local function template: record declaration for on-demand monomorphization.
        _genericFunctionDecls[fnSym] = (fb.Model, decl, $"{fb.Function.Name}$");
        continue;
      }

      ImmutableArray<CaptureEntry> captures = GetEffectiveCaptures(fb, decl);
      if (captures.IsEmpty) {
        if (fb.TypeParams.IsEmpty && _funcIds.TryGetValue(fnSym, out IrFuncId alreadyRegistered)) {
          fb.LocalFuncIds[fnSym] = alreadyRegistered;
          continue;
        }

        IrFuncId localFuncId = CreateIrFunctionForDecl(fb, fnSym, $"{fb.Function.Name}$", envTypeId: null);
        fb.LocalFuncIds[fnSym] = localFuncId;
        if (fb.TypeParams.IsEmpty) {
          _funcIds[fnSym] = localFuncId;
        }

        pendingLocalBodies.Add((decl, fnSym, localFuncId, null));
      } else {
        TypeSymbol concreteFnType = BuildConcreteFunctionType(fb, fnSym);
        IrTypeId envTypeId = BuildEnvType(fb, $"{fb.Function.Name}${fnSym.Name}", captures, concreteFnType);
        IrFuncId localFuncId = CreateIrFunctionForDecl(fb, fnSym, $"{fb.Function.Name}$", envTypeId);
        var info = new CapturingLocalInfo(localFuncId, envTypeId, captures);
        fb.CapturingLocals[fnSym] = info;
        pendingLocalBodies.Add((decl, fnSym, localFuncId, info));
      }
    }

    // Lower bodies after all sibling local functions are registered (enables mutual recursion & hoisting).
    foreach ((FunctionDeclarationSyntax decl, FunctionSymbol fnSym, IrFuncId funcId, CapturingLocalInfo? captureInfo) in pendingLocalBodies) {
      LowerNestedLocalFunctionBody(fb, decl, fnSym, funcId, captureInfo);
    }
  }

  private static void CollectDirectLocalFunctions(
      SemanticModel model,
      SyntaxNode container,
      List<(FunctionDeclarationSyntax Decl, FunctionSymbol Sym)> output) {
    var stack = new Stack<SyntaxNode>();
    foreach (SyntaxElement elem in container.GetChildren()) {
      if (elem.IsNode) {
        stack.Push(elem.AsNode());
      }
    }

    while (stack.Count > 0) {
      SyntaxNode current = stack.Pop();
      if (current is FunctionDeclarationSyntax nestedDecl) {
        if (model.GetDeclaredSymbol(nestedDecl) is FunctionSymbol fnSym) {
          output.Add((nestedDecl, fnSym));
        }

        // Do not descend into nested function bodies here; they register their own locals when lowered.
        continue;
      }

      if (current is LambdaExpressionSyntax or StructDeclarationSyntax) {
        continue;
      }

      foreach (SyntaxElement child in current.GetChildren()) {
        if (child.IsNode) {
          stack.Push(child.AsNode());
        }
      }
    }
  }

  private IrFuncId CreateIrFunctionForDecl(
      FunctionBuilder outerFb,
      FunctionSymbol fnSym,
      string prefix,
      IrTypeId? envTypeId) {
    int extra = envTypeId is not null ? 1 : 0;
    var irParams = ImmutableArray.CreateBuilder<IrParam>(fnSym.Parameters.Length + extra);
    if (envTypeId is { } envId) {
      irParams.Add(new IrParam("env", envId, IrParamOwnership.Owned));
    }

    foreach (ParameterSymbol p in fnSym.Parameters) {
      IrTypeId pType = InternType(outerFb, p.Type);
      if (p.IsMutable) {
        irParams.Add(new IrParam(p.Name, _types.GetPtr(pType), IrParamOwnership.Mut));
      } else {
        irParams.Add(new IrParam(p.Name, pType, IrParamOwnership.Owned));
      }
    }

    IrTypeId retType = InternType(outerFb, fnSym.ReturnType);
    ModuleStateEffects effects = _compilation.GetModuleStateEffects(fnSym);
    string fnName = ReserveFunctionName($"{prefix}{fnSym.Name}");
    IrFunction irFn = _module.AddFunction(fnName, irParams.MoveToImmutable(), retType, effects);
    return irFn.Id;
  }

  private void LowerNestedLocalFunctionBody(
      FunctionBuilder outerFb,
      FunctionDeclarationSyntax decl,
      FunctionSymbol fnSym,
      IrFuncId funcId,
      CapturingLocalInfo? captureInfo) {
    IrFunction irFn = _module.Functions[funcId.Index];
    var innerFb = new FunctionBuilder(
        this,
        irFn,
        outerFb.Model,
        enclosingStruct: outerFb.EnclosingStruct,
        isMutatingMethod: false,
        declaredReturnType: outerFb.ResolveType(fnSym.ReturnType),
        typeParams: outerFb.TypeParams,
        typeArgs: outerFb.TypeArgs);

    InheritVisibleLocalFunctions(innerFb, outerFb);

    int paramOffset = 0;
    if (captureInfo is not null) {
      paramOffset = 1;
      UnpackClosureEnv(innerFb, IrOperand.FromParam(0), captureInfo.Captures, decl.Span);
    }

    for (int i = 0; i < fnSym.Parameters.Length; i++) {
      innerFb.ParamIndices[fnSym.Parameters[i]] = i + paramOffset;
    }

    PrepareCallableScope(innerFb, decl.Body);
    LowerBlockItems(innerFb, decl.Body.Items);
    FinalizeFunctionBody(innerFb, decl.Body.Span);
  }

  private static void InheritVisibleLocalFunctions(FunctionBuilder child, FunctionBuilder parent) {
    foreach ((FunctionSymbol sym, IrFuncId id) in parent.LocalFuncIds) {
      child.LocalFuncIds[sym] = id;
    }

    foreach ((FunctionSymbol sym, CapturingLocalInfo info) in parent.CapturingLocals) {
      child.CapturingLocals[sym] = info;
    }
  }

  // ---------------------------------------------------------------------------
  // Cell helpers (D-056)
  // ---------------------------------------------------------------------------

  private bool IsSymbolBoxedCell(FunctionBuilder fb, LocalSymbol local) =>
      fb.CellSlots.ContainsKey(local) || fb.CellOperands.ContainsKey(local);

  private IrOperand LoadCellHandle(FunctionBuilder fb, LocalSymbol local, SourceSpan span) {
    if (fb.CellOperands.TryGetValue(local, out IrOperand cellOp)) {
      return cellOp;
    }

    IrSlotId cellSlot = fb.CellSlots[local];
    IrTempId loaded = fb.AddTemp(fb.Function.Slots[cellSlot.Index].Type);
    fb.Emit(new LoadSlot(loaded, cellSlot, span));
    return loaded;
  }

  private IrOperand EmitCellRead(FunctionBuilder fb, LocalSymbol local, SourceSpan span) {
    IrOperand cell = LoadCellHandle(fb, local, span);
    var cellType = (IrCellType) _types.GetType(fb.GetOperandType(cell));
    IrTempId val = fb.AddTemp(cellType.ElementType);
    fb.Emit(new CellGet(val, cell, span));
    return val;
  }

  private IrOperand EmitCellAddr(FunctionBuilder fb, LocalSymbol local, SourceSpan span) {
    IrOperand cell = LoadCellHandle(fb, local, span);
    var cellType = (IrCellType) _types.GetType(fb.GetOperandType(cell));
    IrTempId ptr = fb.AddTemp(_types.GetPtr(cellType.ElementType));
    fb.Emit(new CellAddr(ptr, cell, span));
    return ptr;
  }

  private void EmitCellWrite(FunctionBuilder fb, LocalSymbol local, IrOperand value, SourceSpan span) {
    IrOperand cell = LoadCellHandle(fb, local, span);
    fb.Emit(new CellSet(cell, value, span));
  }

  // ---------------------------------------------------------------------------
  // Env synthesis & unpacking
  // ---------------------------------------------------------------------------

  private IrTypeId BuildEnvType(
      FunctionBuilder fb,
      string envName,
      ImmutableArray<CaptureEntry> captures,
      TypeSymbol concreteClosureType) {
    bool isCycleCapable = _compilation.Reachability.IsCycleCapable(concreteClosureType);
    var fields = ImmutableArray.CreateBuilder<IrEnvField>(captures.Length);

    foreach (CaptureEntry entry in captures) {
      TypeSymbol symType = GetCapturedSymbolConcreteType(fb, entry.Symbol);
      IrTypeId irSymType = InternType(symType);

      if (entry.IsMutable && entry.Symbol is LocalSymbol) {
        bool cellCycle = _compilation.Reachability.IsCycleCapable(symType);
        if (cellCycle) {
          isCycleCapable = true;
        }

        IrTypeId cellType = _types.GetCell(irSymType, cellCycle);
        fields.Add(new IrEnvField(entry.Symbol.Name, cellType));
      } else {
        fields.Add(new IrEnvField(entry.Symbol.Name, irSymType));
      }
    }

    return _types.GetEnv(envName, fields.MoveToImmutable(), isCycleCapable);
  }

  private IrOperand EmitEnvNew(
      FunctionBuilder fb,
      IrTypeId envTypeId,
      ImmutableArray<CaptureEntry> captures,
      SourceSpan span) {
    var operands = ImmutableArray.CreateBuilder<IrOperand>(captures.Length);
    foreach (CaptureEntry entry in captures) {
      if (entry.IsMutable && entry.Symbol is LocalSymbol mutLocal && !IsSelfLocal(mutLocal)) {
        operands.Add(LoadCellHandle(fb, mutLocal, span));
      } else {
        operands.Add(EmitSymbolValueRead(fb, entry.Symbol, span));
      }
    }

    IrTempId envTmp = fb.AddTemp(envTypeId, "env");
    fb.Emit(new EnvNew(envTmp, operands.MoveToImmutable(), AllocRegion.Heap, span));
    return envTmp;
  }

  private void UnpackClosureEnv(
      FunctionBuilder innerFb,
      IrOperand envParam,
      ImmutableArray<CaptureEntry> captures,
      SourceSpan span) {
    var envType = (IrEnvType) _types.GetType(innerFb.GetOperandType(envParam));
    for (int i = 0; i < captures.Length; i++) {
      CaptureEntry entry = captures[i];
      IrTypeId fieldType = envType.Fields[i].Type;
      string tempName = entry.IsMutable ? $"{entry.Symbol.Name}_cell" : entry.Symbol.Name;
      IrTempId extracted = innerFb.AddTemp(fieldType, tempName);
      innerFb.Emit(new EnvFieldGet(extracted, envParam, i, span));

      if (entry.IsMutable && entry.Symbol is LocalSymbol mutLocal && !IsSelfLocal(mutLocal)) {
        innerFb.CellOperands[mutLocal] = extracted;
      } else if (entry.Symbol is LocalSymbol selfLocal && IsSelfLocal(selfLocal)) {
        innerFb.CapturedSelf = extracted;
      } else {
        innerFb.LocalTemps[entry.Symbol] = extracted;
      }
    }
  }

  private TypeSymbol GetCapturedSymbolConcreteType(FunctionBuilder fb, Symbol sym) => sym switch {
    LocalSymbol local when IsSelfLocal(local) && fb.EnclosingStruct is { } st =>
        fb.ResolveType(_compilation.Types.GetStruct(st, [.. st.TypeParameters.Select(static tp => (TypeSymbol) tp.Type)])),
    LocalSymbol local => fb.ResolveType(local.Type),
    ParameterSymbol param => fb.ResolveType(param.Type),
    _ => throw new InvalidOperationException($"Unsupported captured symbol '{sym.Name}' ({sym.GetType().Name})."),
  };

  private TypeSymbol BuildConcreteFunctionType(FunctionBuilder fb, FunctionSymbol fnSym) {
    var paramTypes = ImmutableArray.CreateBuilder<FunctionParameter>(fnSym.Parameters.Length);
    foreach (ParameterSymbol p in fnSym.Parameters) {
      paramTypes.Add(new FunctionParameter(fb.ResolveType(p.Type), p.IsMutable));
    }

    return _compilation.Types.GetFunction(paramTypes.MoveToImmutable(), fb.ResolveType(fnSym.ReturnType));
  }

  // ---------------------------------------------------------------------------
  // Lambda & Implicit `it` closure lowering
  // ---------------------------------------------------------------------------

  private IrOperand LowerLambdaExpression(FunctionBuilder fb, LambdaExpressionSyntax lambda) {
    var concreteFnType = (FunctionType) fb.GetNodeType(lambda);
    IrTypeId closureIrType = InternType(concreteFnType);
    ImmutableArray<CaptureEntry> captures = GetEffectiveCaptures(fb, lambda);

    string lamName = ReserveFunctionName($"__lam_{_lambdaCounter++}");
    IrTypeId? envTypeId = null;
    IrOperand? envOp = null;
    if (!captures.IsEmpty) {
      envTypeId = BuildEnvType(fb, lamName, captures, concreteFnType);
      envOp = EmitEnvNew(fb, envTypeId.Value, captures, lambda.Span);
    }

    var irParams = ImmutableArray.CreateBuilder<IrParam>(lambda.Parameters.Length + (envTypeId is not null ? 1 : 0));
    if (envTypeId is { } envId) {
      irParams.Add(new IrParam("env", envId, IrParamOwnership.Owned));
    }

    for (int i = 0; i < lambda.Parameters.Length; i++) {
      ParameterSyntax pSyntax = lambda.Parameters[i];
      var pSym = fb.Model.GetDeclaredSymbol(pSyntax) as ParameterSymbol;
      string pName = pSym?.Name ?? NameOf(fb.Model, pSyntax.NameToken);
      FunctionParameter fp = concreteFnType.Parameters[i];
      IrTypeId pIrType = InternType(fp.Type);
      if (fp.IsMutable) {
        irParams.Add(new IrParam(pName, _types.GetPtr(pIrType), IrParamOwnership.Mut));
      } else {
        irParams.Add(new IrParam(pName, pIrType, IrParamOwnership.Owned));
      }
    }

    IrTypeId retIrType = InternType(concreteFnType.ReturnType);
    IrFunction lamFn = _module.AddFunction(
        lamName,
        irParams.MoveToImmutable(),
        retIrType,
        ModuleStateEffects.ReadWrite);

    var lamFb = new FunctionBuilder(
        this,
        lamFn,
        fb.Model,
        enclosingStruct: fb.EnclosingStruct,
        isMutatingMethod: false,
        declaredReturnType: concreteFnType.ReturnType,
        typeParams: fb.TypeParams,
        typeArgs: fb.TypeArgs);

    InheritVisibleLocalFunctions(lamFb, fb);

    int paramOffset = 0;
    if (envTypeId is not null) {
      paramOffset = 1;
      UnpackClosureEnv(lamFb, IrOperand.FromParam(0), captures, lambda.Span);
    }

    for (int i = 0; i < lambda.Parameters.Length; i++) {
      if (fb.Model.GetDeclaredSymbol(lambda.Parameters[i]) is ParameterSymbol pSym) {
        lamFb.ParamIndices[pSym] = i + paramOffset;
      }
    }

    if (lambda.BodyExpression is { } bodyExpr) {
      PrepareCallableScope(lamFb, bodyExpr);
      IrOperand? bodyVal = LowerExpression(lamFb, bodyExpr);
      if (!lamFb.IsTerminated) {
        if (retIrType == _types.Void) {
          lamFb.Terminate(new RetVoid(bodyExpr.Span));
        } else if (bodyVal is { } bv) {
          IrOperand coerced = CoerceValueToExpectedType(lamFb, bv, fb.GetNodeType(bodyExpr), concreteFnType.ReturnType, bodyExpr.Span);
          lamFb.Terminate(new Ret(coerced, bodyExpr.Span));
        }
      }

      FinalizeFunctionBody(lamFb, bodyExpr.Span);
    } else if (lambda.BodyBlock is { } bodyBlock) {
      PrepareCallableScope(lamFb, bodyBlock);
      LowerBlockItems(lamFb, bodyBlock.Items);
      FinalizeFunctionBody(lamFb, bodyBlock.Span);
    } else {
      FinalizeFunctionBody(lamFb, lambda.Span);
    }

    IrTempId closDest = fb.AddTemp(closureIrType);
    fb.Emit(new ClosureMake(closDest, lamFn.Id, envOp, lambda.Span));
    return closDest;
  }

  private IrOperand LowerImplicitItAsClosure(FunctionBuilder fb, ExpressionSyntax expr, ConversionInfo conv) {
    var concreteFnType = (FunctionType) fb.ResolveType(conv.ImplicitLambdaType ?? conv.TargetType);
    IrTypeId closureIrType = InternType(concreteFnType);
    ImmutableArray<CaptureEntry> captures = GetEffectiveCaptures(fb, expr);

    string lamName = ReserveFunctionName($"__it_{_lambdaCounter++}");
    IrTypeId? envTypeId = null;
    IrOperand? envOp = null;
    if (!captures.IsEmpty) {
      envTypeId = BuildEnvType(fb, lamName, captures, concreteFnType);
      envOp = EmitEnvNew(fb, envTypeId.Value, captures, expr.Span);
    }

    var irParams = ImmutableArray.CreateBuilder<IrParam>(1 + (envTypeId is not null ? 1 : 0));
    if (envTypeId is { } envId) {
      irParams.Add(new IrParam("env", envId, IrParamOwnership.Owned));
    }

    IrTypeId itIrType = InternType(concreteFnType.Parameters[0].Type);
    irParams.Add(new IrParam("it", itIrType, IrParamOwnership.Owned));

    IrTypeId retIrType = InternType(concreteFnType.ReturnType);
    IrFunction lamFn = _module.AddFunction(
        lamName,
        irParams.MoveToImmutable(),
        retIrType,
        ModuleStateEffects.ReadWrite);

    var lamFb = new FunctionBuilder(
        this,
        lamFn,
        fb.Model,
        enclosingStruct: fb.EnclosingStruct,
        isMutatingMethod: false,
        declaredReturnType: concreteFnType.ReturnType,
        typeParams: fb.TypeParams,
        typeArgs: fb.TypeArgs);

    InheritVisibleLocalFunctions(lamFb, fb);

    int itParamIdx = 0;
    if (envTypeId is not null) {
      itParamIdx = 1;
      UnpackClosureEnv(lamFb, IrOperand.FromParam(0), captures, expr.Span);
    }

    if (conv.ImplicitItSymbol is { } itSym) {
      lamFb.LocalTemps[itSym] = IrOperand.FromParam(itParamIdx);
    }

    PrepareCallableScope(lamFb, expr);
    IrOperand? bodyVal = LowerExpressionCore(lamFb, expr);
    if (!lamFb.IsTerminated) {
      if (retIrType == _types.Void) {
        lamFb.Terminate(new RetVoid(expr.Span));
      } else if (bodyVal is { } bv) {
        TypeSymbol actualBodyType = fb.Model.GetType(expr) is { } rawT
            ? fb.ResolveType(rawT)
            : concreteFnType.ReturnType;
        IrOperand coerced = CoerceValueToExpectedType(lamFb, bv, actualBodyType, concreteFnType.ReturnType, expr.Span);
        lamFb.Terminate(new Ret(coerced, expr.Span));
      }
    }

    FinalizeFunctionBody(lamFb, expr.Span);

    IrTempId closDest = fb.AddTemp(closureIrType);
    fb.Emit(new ClosureMake(closDest, lamFn.Id, envOp, expr.Span));
    return closDest;
  }

  // ---------------------------------------------------------------------------
  // First-class function values, static method values, & bound methods (FN-11, STRUCT-13/14)
  // ---------------------------------------------------------------------------

  private IrOperand LowerFunctionAsValue(FunctionBuilder fb, FunctionSymbol fnSym, SourceSpan span) {
    TypeSymbol fnType = BuildConcreteFunctionType(fb, fnSym);
    IrTypeId closureIrType = InternType(fnType);

    if (fb.CapturingLocals.TryGetValue(fnSym, out CapturingLocalInfo? capInfo)) {
      IrOperand envOp = EmitEnvNew(fb, capInfo.EnvTypeId, capInfo.Captures, span);
      IrTempId dest = fb.AddTemp(closureIrType);
      fb.Emit(new ClosureMake(dest, capInfo.FuncId, envOp, span));
      return dest;
    }

    IrFuncId funcId;
    if (fb.LocalFuncIds.TryGetValue(fnSym, out IrFuncId localId)) {
      funcId = localId;
    } else if (_funcIds.TryGetValue(fnSym, out IrFuncId topId)) {
      funcId = topId;
    } else {
      throw new InvalidOperationException($"Function '{fnSym.Name}' was not registered.");
    }

    IrTempId clos = fb.AddTemp(closureIrType);
    fb.Emit(new ClosureMake(clos, funcId, Env: null, span));
    return clos;
  }

  private IrOperand LowerStaticMethodAsValue(
      FunctionBuilder fb,
      StaticAccessExpressionSyntax access,
      MethodSymbol methodSym) {
    var concreteFnType = (FunctionType) fb.GetNodeType(access);
    IrTypeId closureIrType = InternType(concreteFnType);
    var ownerStruct = (StructSymbol) methodSym.ContainingType;

    IrFuncId funcId;
    if (!ownerStruct.TypeParameters.IsEmpty) {
      ImmutableArray<TypeSymbol> ownerArgs = ResolveStaticAccessOwnerTypeArgs(fb, access, ownerStruct, methodSym, concreteFnType);
      funcId = GetOrRequestMonoMethod(methodSym, ownerArgs, []);
    } else if (_methodIds.TryGetValue(methodSym, out IrFuncId registeredId)) {
      funcId = registeredId;
    } else {
      throw new InvalidOperationException($"Static method '{ownerStruct.Name}::{methodSym.Name}' was not registered.");
    }

    IrTempId clos = fb.AddTemp(closureIrType);
    fb.Emit(new ClosureMake(clos, funcId, Env: null, access.Span));
    return clos;
  }

  private IrOperand LowerBoundMethodValue(
      FunctionBuilder fb,
      IrOperand receiverVal,
      TypeSymbol concreteRecvType,
      MethodSymbol methodSym,
      FunctionType concreteFnType,
      SourceSpan span) {
    IrTypeId recvIrType = fb.GetOperandType(receiverVal);
    IrTypeId closureIrType = InternType(concreteFnType);

    if (concreteRecvType is StructType concreteStruct) {
      MethodSymbol canonical = methodSym.PromotedFrom ?? methodSym;
      IrFuncId targetMethodId = ResolveConcreteStructMethodId(concreteStruct, methodSym);
      var key = new BoundMethodKey(recvIrType, targetMethodId, canonical);

      if (!_boundMethodTrampolines.TryGetValue(key, out (IrFuncId FuncId, IrTypeId EnvTypeId) cached)) {
        string baseName = $"__bound_{SanitizeTypeName(concreteStruct.DisplayName)}_{methodSym.Name}";
        string trampName = ReserveFunctionName(baseName);
        IrTypeId envTypeId = _types.GetEnv(trampName, [new IrEnvField("self", recvIrType)]);

        var irParams = ImmutableArray.CreateBuilder<IrParam>(concreteFnType.Parameters.Length + 1);
        irParams.Add(new IrParam("env", envTypeId, IrParamOwnership.Owned));
        for (int i = 0; i < concreteFnType.Parameters.Length; i++) {
          FunctionParameter fp = concreteFnType.Parameters[i];
          string pName = i < methodSym.Parameters.Length ? methodSym.Parameters[i].Name : $"arg{i}";
          IrTypeId pType = InternType(fp.Type);
          irParams.Add(fp.IsMutable
              ? new IrParam(pName, _types.GetPtr(pType), IrParamOwnership.Mut)
              : new IrParam(pName, pType, IrParamOwnership.Owned));
        }

        IrTypeId retIrType = InternType(concreteFnType.ReturnType);
        IrFunction trampFn = _module.AddFunction(
            trampName,
            irParams.MoveToImmutable(),
            retIrType,
            _compilation.GetModuleStateEffects(canonical));

        var trampFb = new FunctionBuilder(this, trampFn, fb.Model);
        IrTempId selfVal = trampFb.AddTemp(recvIrType, "self");
        trampFb.Emit(new EnvFieldGet(selfVal, IrOperand.FromParam(0), 0));
        IrOperand promotedSelf = PromoteSelfValue(trampFb, selfVal, methodSym, default);

        var callArgs = ImmutableArray.CreateBuilder<IrOperand>(concreteFnType.Parameters.Length + 1);
        callArgs.Add(promotedSelf);
        for (int i = 0; i < concreteFnType.Parameters.Length; i++) {
          callArgs.Add(IrOperand.FromParam(i + 1));
        }

        IrOperand? callRes = EmitDirectCall(trampFb, targetMethodId, callArgs.MoveToImmutable(), default);
        if (retIrType == _types.Void) {
          trampFb.Terminate(new RetVoid());
        } else {
          trampFb.Terminate(new Ret(callRes!.Value));
        }

        cached = (trampFn.Id, envTypeId);
        _boundMethodTrampolines[key] = cached;
      }

      IrTempId envTmp = fb.AddTemp(cached.EnvTypeId, "bound_env");
      fb.Emit(new EnvNew(envTmp, [receiverVal], AllocRegion.Heap, span));
      IrTempId clos = fb.AddTemp(closureIrType);
      fb.Emit(new ClosureMake(clos, cached.FuncId, envTmp, span));
      return clos;
    }

    if (concreteRecvType is InterfaceType concreteIface) {
      int methodIndex = FindInterfaceMethodIndex(concreteIface.Definition, methodSym.Name);
      var key = new BoundMethodKey(recvIrType, new IrFuncId(methodIndex), methodSym);

      if (!_boundMethodTrampolines.TryGetValue(key, out (IrFuncId FuncId, IrTypeId EnvTypeId) cached)) {
        string baseName = $"__bound_iface_{SanitizeTypeName(concreteIface.DisplayName)}_{methodSym.Name}";
        string trampName = ReserveFunctionName(baseName);
        IrTypeId envTypeId = _types.GetEnv(trampName, [new IrEnvField("self", recvIrType)]);

        var irParams = ImmutableArray.CreateBuilder<IrParam>(concreteFnType.Parameters.Length + 1);
        irParams.Add(new IrParam("env", envTypeId, IrParamOwnership.Owned));
        for (int i = 0; i < concreteFnType.Parameters.Length; i++) {
          FunctionParameter fp = concreteFnType.Parameters[i];
          string pName = i < methodSym.Parameters.Length ? methodSym.Parameters[i].Name : $"arg{i}";
          IrTypeId pType = InternType(fp.Type);
          irParams.Add(fp.IsMutable
              ? new IrParam(pName, _types.GetPtr(pType), IrParamOwnership.Mut)
              : new IrParam(pName, pType, IrParamOwnership.Owned));
        }

        IrTypeId retIrType = InternType(concreteFnType.ReturnType);
        IrFunction trampFn = _module.AddFunction(
            trampName,
            irParams.MoveToImmutable(),
            retIrType,
            ModuleStateEffects.ReadWrite);

        var trampFb = new FunctionBuilder(this, trampFn, fb.Model);
        IrTempId selfVal = trampFb.AddTemp(recvIrType, "self");
        trampFb.Emit(new EnvFieldGet(selfVal, IrOperand.FromParam(0), 0));

        var callArgs = ImmutableArray.CreateBuilder<IrOperand>(concreteFnType.Parameters.Length);
        for (int i = 0; i < concreteFnType.Parameters.Length; i++) {
          callArgs.Add(IrOperand.FromParam(i + 1));
        }

        if (retIrType == _types.Void) {
          trampFb.Emit(new CallIface(null, selfVal, methodIndex, IsMutating: false, callArgs.MoveToImmutable()));
          trampFb.Terminate(new RetVoid());
        } else {
          IrTempId res = trampFb.AddTemp(retIrType);
          trampFb.Emit(new CallIface(res, selfVal, methodIndex, IsMutating: false, callArgs.MoveToImmutable()));
          trampFb.Terminate(new Ret(res));
        }

        cached = (trampFn.Id, envTypeId);
        _boundMethodTrampolines[key] = cached;
      }

      IrTempId envTmp = fb.AddTemp(cached.EnvTypeId, "bound_env");
      fb.Emit(new EnvNew(envTmp, [receiverVal], AllocRegion.Heap, span));
      IrTempId clos = fb.AddTemp(closureIrType);
      fb.Emit(new ClosureMake(clos, cached.FuncId, envTmp, span));
      return clos;
    }

    throw new InvalidOperationException($"Cannot bind method '{methodSym.Name}' on '{concreteRecvType.DisplayName}'.");
  }

  private IrOperand? TryLowerBoundStdlibMethodValue(
      FunctionBuilder fb,
      IrOperand receiverVal,
      TypeSymbol concreteRecvType,
      string memberName,
      FunctionType concreteFnType,
      SourceSpan span) {
    if (!StdlibCatalog.TryGetMember(concreteRecvType, memberName, out IntrinsicDescriptor? desc)
        || desc.IsProperty
        || desc.MutatesReceiver
        || desc.Signatures.Length != 1) {
      return null;
    }

    IntrinsicId intrinsic = desc.Signatures[0].Id;
    IrTypeId recvIrType = fb.GetOperandType(receiverVal);
    IrTypeId closureIrType = InternType(concreteFnType);
    var cacheKey = (recvIrType, intrinsic);

    if (!_boundStdlibTrampolines.TryGetValue(cacheKey, out (IrFuncId FuncId, IrTypeId EnvTypeId) cached)) {
      string trampName = ReserveFunctionName($"__bound_stdlib_{SanitizeTypeName(concreteRecvType.DisplayName)}_{memberName}");
      IrTypeId envTypeId = _types.GetEnv(trampName, [new IrEnvField("self", recvIrType)]);

      var irParams = ImmutableArray.CreateBuilder<IrParam>(concreteFnType.Parameters.Length + 1);
      irParams.Add(new IrParam("env", envTypeId, IrParamOwnership.Owned));
      for (int i = 0; i < concreteFnType.Parameters.Length; i++) {
        IrTypeId pType = InternType(concreteFnType.Parameters[i].Type);
        irParams.Add(new IrParam($"arg{i}", pType, IrParamOwnership.Owned));
      }

      IrTypeId retIrType = InternType(concreteFnType.ReturnType);
      IrFunction trampFn = _module.AddFunction(
          trampName,
          irParams.MoveToImmutable(),
          retIrType,
          ModuleStateEffects.Pure);

      var trampFb = new FunctionBuilder(this, trampFn, fb.Model);
      IrTempId selfVal = trampFb.AddTemp(recvIrType, "self");
      trampFb.Emit(new EnvFieldGet(selfVal, IrOperand.FromParam(0), 0));

      var evalArgs = new IrOperand[concreteFnType.Parameters.Length];
      for (int i = 0; i < evalArgs.Length; i++) {
        evalArgs[i] = IrOperand.FromParam(i + 1);
      }

      IrOperand? res = EmitNonCallbackIntrinsicCore(trampFb, intrinsic, selfVal, evalArgs, concreteFnType.ReturnType, default);
      if (retIrType == _types.Void) {
        trampFb.Terminate(new RetVoid());
      } else {
        trampFb.Terminate(new Ret(res!.Value));
      }

      cached = (trampFn.Id, envTypeId);
      _boundStdlibTrampolines[cacheKey] = cached;
    }

    IrTempId envTmp = fb.AddTemp(cached.EnvTypeId, "bound_env");
    fb.Emit(new EnvNew(envTmp, [receiverVal], AllocRegion.Heap, span));
    IrTempId clos = fb.AddTemp(closureIrType);
    fb.Emit(new ClosureMake(clos, cached.FuncId, envTmp, span));
    return clos;
  }

  // ---------------------------------------------------------------------------
  // Indirect & Indexed Closure Calls (D-055, FN-11..15, STRUCT-12..14)
  // ---------------------------------------------------------------------------

  private IrOperand? LowerIndirectClosureCall(FunctionBuilder fb, CallExpressionSyntax call) {
    IrOperand? closOpt = LowerExpression(fb, call.Callee);
    if (fb.IsTerminated || closOpt is not { } closVal) {
      return null;
    }

    ImmutableArray<IrOperand>? args = LowerCallArgsWithExclusivity(
        fb,
        call,
        mutatingReceiverExpr: null,
        hasImplicitMutSelf: false,
        out _);
    if (fb.IsTerminated || args is not { } argOps) {
      return null;
    }

    return EmitClosureCall(fb, closVal, argOps, call.Span);
  }

  private IrOperand? LowerIndexedElementCall(
      FunctionBuilder fb,
      CallExpressionSyntax call,
      CallResolution res) {
    // D-055: fns[i](args...) parsed with TypeArguments = [NamedTypeSyntax(i)].
    IrOperand? containerOpt = LowerExpression(fb, call.Callee);
    if (fb.IsTerminated || containerOpt is not { } container) {
      return null;
    }

    SyntaxNode indexNode = call.TypeArguments!.Arguments[0];
    Symbol idxSym = res.IndexSymbol
        ?? fb.Model.GetReferencedSymbol(indexNode)
        ?? throw new InvalidOperationException("Missing index symbol on D-055 indexed element call.");
    IrOperand rawIdx = EmitSymbolValueRead(fb, idxSym, indexNode.Span);
    IrOperand? convIdx = ApplyConversion(fb, indexNode, rawIdx, indexNode.Span);
    if (fb.IsTerminated || convIdx is not { } keyVal) {
      return null;
    }

    IrType containerType = _types.GetType(fb.GetOperandType(container));
    IrTempId closVal;
    switch (containerType) {
      case IrVectorType vt:
        closVal = fb.AddTemp(vt.ElementType);
        fb.Emit(new VecGet(closVal, container, keyVal, Unchecked: false, call.Span));
        break;

      case IrMapType mt:
        closVal = fb.AddTemp(mt.ValueType);
        fb.Emit(new MapGet(closVal, container, keyVal, call.Span));
        break;

      case IrPoolType pt: {
          string poolName = ExtractFirstIdentifier(fb.Model, call.Callee);
          closVal = fb.AddTemp(pt.ElementType);
          fb.Emit(new PoolGetOrPanic(closVal, container, keyVal, poolName, call.Span));
          break;
        }

      default:
        throw new InvalidOperationException($"Unsupported indexed-call container type '{containerType.Name}'.");
    }

    ImmutableArray<IrOperand>? args = LowerCallArgsWithExclusivity(
        fb,
        call,
        mutatingReceiverExpr: null,
        hasImplicitMutSelf: false,
        out _);
    if (fb.IsTerminated || args is not { } argOps) {
      return null;
    }

    return EmitClosureCall(fb, closVal, argOps, call.Span);
  }

  private IrOperand? EmitClosureCall(
      FunctionBuilder fb,
      IrOperand closureVal,
      ImmutableArray<IrOperand> args,
      SourceSpan span) {
    var closType = (IrClosureType) _types.GetType(fb.GetOperandType(closureVal));
    var coercedArgs = ImmutableArray.CreateBuilder<IrOperand>(args.Length);
    for (int i = 0; i < args.Length; i++) {
      IrClosureParam p = closType.Parameters[i];
      coercedArgs.Add(p.Ownership == IrParamOwnership.Mut
          ? args[i]
          : CoerceOperandToTargetType(fb, args[i], p.Type, span));
    }

    ImmutableArray<IrOperand> finalArgs = coercedArgs.MoveToImmutable();
    if (closType.ReturnType == _types.Void) {
      fb.Emit(new CallClosure(null, closureVal, finalArgs, span));
      return null;
    }

    IrTempId dest = fb.AddTemp(closType.ReturnType);
    fb.Emit(new CallClosure(dest, closureVal, finalArgs, span));
    return dest;
  }

  private readonly struct BoundMethodKey(IrTypeId receiverType, IrFuncId targetId, MethodSymbol method)
      : IEquatable<BoundMethodKey> {
    public IrTypeId ReceiverType { get; } = receiverType;
    public IrFuncId TargetId { get; } = targetId;
    public MethodSymbol Method { get; } = method;

    public bool Equals(BoundMethodKey other) =>
        ReceiverType == other.ReceiverType
        && TargetId == other.TargetId
        && ReferenceEquals(Method, other.Method);

    public override bool Equals(object? obj) => obj is BoundMethodKey other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            ReceiverType.Index,
            TargetId.Index,
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Method));
  }
}
