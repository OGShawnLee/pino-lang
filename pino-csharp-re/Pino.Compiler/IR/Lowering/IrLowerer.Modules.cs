using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.IR;

public sealed partial class IrLowerer {
  private void LowerCompilation() {
    foreach (SemanticModel model in _compilation.SemanticModels) {
      _modelsByModule[model.Module] = model;
    }

    List<SemanticModel> orderedModules = TopologicalSortModules();

    // Phase 1: Pre-register all globals, top-level functions, struct methods, and generic templates.
    foreach (SemanticModel model in orderedModules) {
      bool isRoot = ReferenceEquals(model, _compilation.RootModel);
      RegisterModuleDeclarations(model, isRoot);
    }

    // Seed concrete generic instantiations from Compilation.GenericInstantiations (GEN-1..11, IQ-6).
    SeedGenericInstantiations();

    // Phase 2: Lower all registered non-generic function and method bodies.
    foreach ((SemanticModel model, FunctionDeclarationSyntax decl, FunctionSymbol symbol, IrFuncId funcId) in _pendingFunctions) {
      LowerFunctionBody(model, decl, symbol, funcId);
    }

    foreach ((SemanticModel model, StructSymbol owner, MethodDeclarationSyntax decl, MethodSymbol symbol, IrFuncId funcId) in _pendingMethods) {
      LowerMethodBody(model, owner, decl, symbol, funcId);
    }

    // Drain monomorphization worklist discovered so far.
    DrainMonomorphizationWorklist();

    // Phase 3: Lower imported module top-level initializers in dependency topological order (D-165).
    foreach (SemanticModel model in orderedModules) {
      if (ReferenceEquals(model, _compilation.RootModel)) {
        continue;
      }

      IrFuncId initId = LowerModuleInitializer(model);
      _module.InitFuncIds.Add(initId);
    }

    // Phase 4: Synthesize __pino_shutdown() and __pino_entry() (Script vs Program mode, MAIN-1..4, ERR-18..20).
    IrFuncId shutdownId = BuildShutdownFunction();
    _module.ShutdownFuncId = shutdownId;

    IrFuncId entryId = BuildEntryFunction(_compilation.RootModel, shutdownId);
    _module.EntryFuncId = entryId;

    // Drain any remaining transitive generic instantiations discovered in initializers, entry, or vtables.
    DrainMonomorphizationWorklist();
  }

  private IrFuncId LowerReplChunkCore(
      int committedItemCount,
      int chunkNumber,
      IReadOnlyDictionary<string, IrGlobalId> existingGlobals,
      IReadOnlyDictionary<string, IrFuncId> existingFunctions,
      IReadOnlyDictionary<string, IrFuncId> existingMethods) {
    foreach (SemanticModel model in _compilation.SemanticModels) {
      _modelsByModule[model.Module] = model;
    }

    SemanticModel rootModel = _compilation.RootModel;
    ImmutableArray<SyntaxNode> items = rootModel.Tree.Root.Items;

    // Phase 1: Bind previously committed declarations to their existing IR IDs, and register new ones.
    for (int idx = 0; idx < items.Length; idx++) {
      SyntaxNode item = items[idx];
      bool isCommitted = idx < committedItemCount;
      if (isCommitted) {
        BindCommittedReplDeclaration(rootModel, item, existingGlobals, existingFunctions, existingMethods);
      } else {
        switch (item) {
          case VariableDeclarationSyntax varDecl:
            RegisterGlobalVariable(rootModel, varDecl, isRoot: true);
            break;
          case TupleDestructuringStatementSyntax destructDecl:
            RegisterGlobalDestructuring(rootModel, destructDecl, isRoot: true);
            break;
          case FunctionDeclarationSyntax fnDecl:
            RegisterFunction(rootModel, fnDecl, string.Empty);
            break;
          case StructDeclarationSyntax structDecl:
            RegisterStructMethods(rootModel, structDecl, string.Empty);
            break;
          default:
            RegisterNestedStructs(rootModel, item, $"__repl_{chunkNumber}$");
            break;
        }
      }
    }

    SeedGenericInstantiations();

    // Phase 2: Lower newly declared functions and methods only.
    foreach ((SemanticModel model, FunctionDeclarationSyntax decl, FunctionSymbol symbol, IrFuncId funcId) in _pendingFunctions) {
      LowerFunctionBody(model, decl, symbol, funcId);
    }

    foreach ((SemanticModel model, StructSymbol owner, MethodDeclarationSyntax decl, MethodSymbol symbol, IrFuncId funcId) in _pendingMethods) {
      LowerMethodBody(model, owner, decl, symbol, funcId);
    }

    DrainMonomorphizationWorklist();

    // Phase 3: Lower only the new chunk's statements into __repl_chunk_<N>() (no shutdown call).
    string chunkName = ReserveFunctionName($"__repl_chunk_{chunkNumber}");
    IrFunction chunkFn = _module.AddFunction(
        chunkName,
        [],
        _types.Void,
        ModuleStateEffects.ReadWrite);
    var fb = new FunctionBuilder(this, chunkFn, rootModel);

    for (int idx = committedItemCount; idx < items.Length; idx++) {
      SyntaxNode item = items[idx];
      if (item is not (FunctionDeclarationSyntax
          or StructDeclarationSyntax
          or InterfaceDeclarationSyntax
          or EnumDeclarationSyntax
          or UnionDeclarationSyntax
          or ModuleDeclarationSyntax
          or ImportStatementSyntax
          or FromImportStatementSyntax
          or TestDeclarationSyntax)) {
        PrepareCallableScope(fb, item);
      }
    }

    for (int idx = committedItemCount; idx < items.Length; idx++) {
      if (fb.IsTerminated) {
        break;
      }

      SyntaxNode item = items[idx];
      if (item is FunctionDeclarationSyntax
          or StructDeclarationSyntax
          or InterfaceDeclarationSyntax
          or EnumDeclarationSyntax
          or UnionDeclarationSyntax
          or ModuleDeclarationSyntax
          or ImportStatementSyntax
          or FromImportStatementSyntax
          or TestDeclarationSyntax) {
        continue;
      }

      LowerStatement(fb, item);
    }

    FinalizeFunctionBody(fb, rootModel.Tree.Root.Span);
    DrainMonomorphizationWorklist();
    return chunkFn.Id;
  }

  private void BindCommittedReplDeclaration(
      SemanticModel model,
      SyntaxNode item,
      IReadOnlyDictionary<string, IrGlobalId> existingGlobals,
      IReadOnlyDictionary<string, IrFuncId> existingFunctions,
      IReadOnlyDictionary<string, IrFuncId> existingMethods) {
    switch (item) {
      case VariableDeclarationSyntax varDecl:
        if (model.GetDeclaredSymbol(varDecl) is LocalSymbol localSym
            && existingGlobals.TryGetValue(localSym.Name, out IrGlobalId existingGlobal)) {
          _globalIds[localSym] = existingGlobal;
        }

        break;

      case TupleDestructuringStatementSyntax destructDecl:
        foreach (DestructureFieldSyntax field in destructDecl.Fields) {
          if (model.GetDeclaredSymbol(field) is LocalSymbol fieldSym
              && existingGlobals.TryGetValue(fieldSym.Name, out IrGlobalId existingFieldGlobal)) {
            _globalIds[fieldSym] = existingFieldGlobal;
          }
        }

        break;

      case FunctionDeclarationSyntax fnDecl:
        if (model.GetDeclaredSymbol(fnDecl) is FunctionSymbol fnSym) {
          if (!fnSym.TypeParameters.IsEmpty) {
            _genericFunctionDecls[fnSym] = (model, fnDecl, string.Empty);
          } else if (existingFunctions.TryGetValue(fnSym.Name, out IrFuncId existingFn)) {
            _funcIds[fnSym] = existingFn;
          } else {
            RegisterFunction(model, fnDecl, string.Empty);
          }
        }

        break;

      case StructDeclarationSyntax structDecl:
        if (model.GetDeclaredSymbol(structDecl) is StructSymbol structSym) {
          if (!structSym.TypeParameters.IsEmpty) {
            foreach (SyntaxNode member in structDecl.Members) {
              if (member is MethodDeclarationSyntax methodDecl
                  && model.GetDeclaredSymbol(methodDecl) is MethodSymbol methodSym) {
                _genericMethodDecls[methodSym] = (model, structSym, methodDecl, string.Empty);
              }
            }
          } else {
            StructType concreteStructType = _compilation.Types.GetStruct(structSym);
            _ = InternType(concreteStructType);
            foreach (SyntaxNode member in structDecl.Members) {
              if (member is not MethodDeclarationSyntax methodDecl
                  || model.GetDeclaredSymbol(methodDecl) is not MethodSymbol methodSym) {
                continue;
              }

              if (!methodSym.TypeParameters.IsEmpty) {
                _genericMethodDecls[methodSym] = (model, structSym, methodDecl, string.Empty);
              } else if (existingMethods.TryGetValue($"{structSym.Name}::{methodSym.Name}", out IrFuncId existingMethod)) {
                _methodIds[methodSym] = existingMethod;
              }
            }
          }
        }

        break;
    }
  }

  private List<SemanticModel> TopologicalSortModules() {
    var result = new List<SemanticModel>();
    var visited = new HashSet<ModuleSymbol>(ReferenceEqualityComparer.Instance);

    void Visit(SemanticModel model) {
      if (!visited.Add(model.Module)) {
        return;
      }

      foreach (SyntaxNode item in model.Tree.Root.Items) {
        if (item is ImportStatementSyntax or FromImportStatementSyntax
            && model.GetReferencedSymbol(item) is ModuleSymbol depMod
            && _modelsByModule.TryGetValue(depMod, out SemanticModel? depModel)) {
          Visit(depModel);
        }
      }

      result.Add(model);
    }

    Visit(_compilation.RootModel);
    return result;
  }

  private void RegisterModuleDeclarations(SemanticModel model, bool isRoot) {
    string modPrefix = isRoot ? string.Empty : $"{model.Module.Name}::";

    foreach (SyntaxNode item in model.Tree.Root.Items) {
      switch (item) {
        case VariableDeclarationSyntax varDecl:
          RegisterGlobalVariable(model, varDecl, isRoot);
          break;

        case TupleDestructuringStatementSyntax destructDecl:
          RegisterGlobalDestructuring(model, destructDecl, isRoot);
          break;

        case FunctionDeclarationSyntax fnDecl:
          RegisterFunction(model, fnDecl, modPrefix);
          break;

        case StructDeclarationSyntax structDecl:
          RegisterStructMethods(model, structDecl, modPrefix);
          break;

        default:
          RegisterNestedStructs(model, item, $"{modPrefix}__top$");
          break;
      }
    }
  }

  private void RegisterGlobalVariable(SemanticModel model, VariableDeclarationSyntax varDecl, bool isRoot) {
    if (model.GetDeclaredSymbol(varDecl) is not LocalSymbol localSym) {
      return;
    }

    IrTypeId irType = InternType(localSym.Type);
    string name = isRoot ? localSym.Name : $"{model.Module.Name}__{localSym.Name}";
    IrGlobalId globalId = _module.AddGlobal(ReserveGlobalName(name), irType, localSym.IsMutable);
    _globalIds[localSym] = globalId;
  }

  private void RegisterGlobalDestructuring(
      SemanticModel model,
      TupleDestructuringStatementSyntax destructDecl,
      bool isRoot) {
    foreach (DestructureFieldSyntax field in destructDecl.Fields) {
      if (model.GetDeclaredSymbol(field) is not LocalSymbol localSym) {
        continue;
      }

      IrTypeId irType = InternType(localSym.Type);
      string name = isRoot ? localSym.Name : $"{model.Module.Name}__{localSym.Name}";
      IrGlobalId globalId = _module.AddGlobal(ReserveGlobalName(name), irType, localSym.IsMutable);
      _globalIds[localSym] = globalId;
    }
  }

  private void RegisterFunction(SemanticModel model, FunctionDeclarationSyntax fnDecl, string prefix) {
    if (model.GetDeclaredSymbol(fnDecl) is not FunctionSymbol fnSym) {
      return;
    }

    if (!fnSym.TypeParameters.IsEmpty) {
      // Record generic function template for monomorphization worklist (GEN-5..11).
      _genericFunctionDecls[fnSym] = (model, fnDecl, prefix);
      return;
    }

    var irParams = ImmutableArray.CreateBuilder<IrParam>(fnSym.Parameters.Length);
    foreach (ParameterSymbol p in fnSym.Parameters) {
      IrTypeId pType = InternType(p.Type);
      if (p.IsMutable) {
        irParams.Add(new IrParam(p.Name, _types.GetPtr(pType), IrParamOwnership.Mut));
      } else {
        irParams.Add(new IrParam(p.Name, pType, IrParamOwnership.Owned));
      }
    }

    IrTypeId retType = InternType(fnSym.ReturnType);
    ModuleStateEffects effects = _compilation.GetModuleStateEffects(fnSym);
    string fnName = ReserveFunctionName($"{prefix}{fnSym.Name}");
    IrFunction irFn = _module.AddFunction(fnName, irParams.MoveToImmutable(), retType, effects);
    _funcIds[fnSym] = irFn.Id;
    _pendingFunctions.Add((model, fnDecl, fnSym, irFn.Id));

    RegisterNestedStructs(model, fnDecl.Body, fnName);
  }

  private void RegisterStructMethods(SemanticModel model, StructDeclarationSyntax structDecl, string prefix) {
    if (model.GetDeclaredSymbol(structDecl) is not StructSymbol structSym) {
      return;
    }

    if (!structSym.TypeParameters.IsEmpty) {
      foreach (SyntaxNode member in structDecl.Members) {
        if (member is MethodDeclarationSyntax methodDecl
            && model.GetDeclaredSymbol(methodDecl) is MethodSymbol methodSym) {
          _genericMethodDecls[methodSym] = (model, structSym, methodDecl, prefix);
        }
      }

      return;
    }

    StructType concreteStructType = _compilation.Types.GetStruct(structSym);
    IrTypeId structIrType = InternType(concreteStructType);

    foreach (SyntaxNode member in structDecl.Members) {
      if (member is not MethodDeclarationSyntax methodDecl
          || model.GetDeclaredSymbol(methodDecl) is not MethodSymbol methodSym) {
        continue;
      }

      if (!methodSym.TypeParameters.IsEmpty) {
        _genericMethodDecls[methodSym] = (model, structSym, methodDecl, prefix);
        continue;
      }

      var irParams = ImmutableArray.CreateBuilder<IrParam>(
          methodSym.Parameters.Length + (methodSym.IsStatic ? 0 : 1));
      if (!methodSym.IsStatic) {
        if (methodSym.IsMutating) {
          irParams.Add(new IrParam("self", _types.GetPtr(structIrType), IrParamOwnership.Mut));
        } else {
          irParams.Add(new IrParam("self", structIrType, IrParamOwnership.Owned));
        }
      }

      foreach (ParameterSymbol p in methodSym.Parameters) {
        IrTypeId pType = InternType(p.Type);
        if (p.IsMutable) {
          irParams.Add(new IrParam(p.Name, _types.GetPtr(pType), IrParamOwnership.Mut));
        } else {
          irParams.Add(new IrParam(p.Name, pType, IrParamOwnership.Owned));
        }
      }

      IrTypeId retType = InternType(methodSym.ReturnType);
      ModuleStateEffects effects = _compilation.GetModuleStateEffects(methodSym);
      string methodName = ReserveFunctionName($"{prefix}{structSym.Name}::{methodSym.Name}");
      IrFunction irFn = _module.AddFunction(methodName, irParams.MoveToImmutable(), retType, effects);
      _methodIds[methodSym] = irFn.Id;
      _pendingMethods.Add((model, structSym, methodDecl, methodSym, irFn.Id));

      RegisterNestedStructs(model, methodDecl.Body, methodName);
    }
  }

  private void RegisterNestedStructs(SemanticModel model, SyntaxNode container, string ownerPrefix) {
    var stack = new Stack<SyntaxNode>();
    foreach (SyntaxElement child in container.GetChildren()) {
      if (child.IsNode) {
        stack.Push(child.AsNode());
      }
    }

    while (stack.Count > 0) {
      SyntaxNode current = stack.Pop();
      if (current is StructDeclarationSyntax nestedStruct) {
        RegisterStructMethods(model, nestedStruct, $"{ownerPrefix}$");
        continue;
      }

      foreach (SyntaxElement elem in current.GetChildren()) {
        if (elem.IsNode) {
          stack.Push(elem.AsNode());
        }
      }
    }
  }

  private void LowerFunctionBody(
      SemanticModel model,
      FunctionDeclarationSyntax decl,
      FunctionSymbol fnSym,
      IrFuncId funcId) {
    IrFunction irFn = _module.Functions[funcId.Index];
    var fb = new FunctionBuilder(this, irFn, model, declaredReturnType: fnSym.ReturnType);
    for (int i = 0; i < fnSym.Parameters.Length; i++) {
      fb.ParamIndices[fnSym.Parameters[i]] = i;
    }

    PrepareCallableScope(fb, decl.Body);
    LowerBlockItems(fb, decl.Body.Items);
    FinalizeFunctionBody(fb, decl.Body.Span);
  }

  private void LowerMethodBody(
      SemanticModel model,
      StructSymbol owner,
      MethodDeclarationSyntax decl,
      MethodSymbol methodSym,
      IrFuncId funcId) {
    IrFunction irFn = _module.Functions[funcId.Index];
    var fb = new FunctionBuilder(
        this,
        irFn,
        model,
        enclosingStruct: owner,
        isMutatingMethod: methodSym.IsMutating,
        declaredReturnType: methodSym.ReturnType);

    int paramOffset = methodSym.IsStatic ? 0 : 1;
    for (int i = 0; i < methodSym.Parameters.Length; i++) {
      fb.ParamIndices[methodSym.Parameters[i]] = i + paramOffset;
    }

    PrepareCallableScope(fb, decl.Body);
    LowerBlockItems(fb, decl.Body.Items);
    FinalizeFunctionBody(fb, decl.Body.Span);
  }

  private void FinalizeFunctionBody(FunctionBuilder fb, Text.SourceSpan span) {
    if (!fb.IsTerminated) {
      if (fb.Function.ReturnType == _types.Void) {
        fb.Terminate(new RetVoid(span));
      } else {
        fb.Terminate(new Unreachable(span));
      }
    }

    // Ensure every created block has a valid terminator (e.g., an empty unreachable join block).
    foreach (IrBasicBlock block in fb.Function.Blocks) {
      block.Terminator ??= new Unreachable(span);
    }
  }

  private IrFuncId LowerModuleInitializer(SemanticModel model) {
    string initName = ReserveFunctionName($"__pino_init_{model.Module.Name}");
    IrFunction initFn = _module.AddFunction(
        initName,
        [],
        _types.Void,
        ModuleStateEffects.WriteOnly);
    var fb = new FunctionBuilder(this, initFn, model);

    foreach (SyntaxNode item in model.Tree.Root.Items) {
      if (item is VariableDeclarationSyntax or TupleDestructuringStatementSyntax) {
        PrepareCallableScope(fb, item);
      }
    }

    foreach (SyntaxNode item in model.Tree.Root.Items) {
      if (fb.IsTerminated) {
        break;
      }

      if (item is VariableDeclarationSyntax or TupleDestructuringStatementSyntax) {
        LowerStatement(fb, item);
      }
    }

    FinalizeFunctionBody(fb, model.Tree.Root.Span);
    return initFn.Id;
  }

  private IrFuncId BuildShutdownFunction() {
    string name = ReserveFunctionName("__pino_shutdown");
    IrFunction shutdownFn = _module.AddFunction(
        name,
        [],
        _types.Void,
        ModuleStateEffects.WriteOnly);
    IrBasicBlock bb0 = shutdownFn.AddBlock();
    bb0.Terminator = new RetVoid();
    return shutdownFn.Id;
  }

  private IrFuncId BuildEntryFunction(SemanticModel rootModel, IrFuncId shutdownId) {
    string name = ReserveFunctionName("__pino_entry");
    IrFunction entryFn = _module.AddFunction(
        name,
        [],
        _types.Void,
        ModuleStateEffects.ReadWrite);
    var fb = new FunctionBuilder(this, entryFn, rootModel);

    // 1. Run imported module initializers in dependency topological order (D-165).
    foreach (IrFuncId initId in _module.InitFuncIds) {
      fb.Emit(new CallDirect(null, initId, []));
    }

    if (!rootModel.IsProgramMode) {
      // Script mode (MAIN-1): execute all top-level statements in strict textual order.
      // Scan for any block-local variables captured by closures and register block-local functions.
      foreach (SyntaxNode item in rootModel.Tree.Root.Items) {
        if (item is not (FunctionDeclarationSyntax
            or StructDeclarationSyntax
            or InterfaceDeclarationSyntax
            or EnumDeclarationSyntax
            or UnionDeclarationSyntax
            or ModuleDeclarationSyntax
            or ImportStatementSyntax
            or FromImportStatementSyntax
            or TestDeclarationSyntax)) {
          PrepareCallableScope(fb, item);
        }
      }

      foreach (SyntaxNode item in rootModel.Tree.Root.Items) {
        if (fb.IsTerminated) {
          break;
        }

        if (item is FunctionDeclarationSyntax
            or StructDeclarationSyntax
            or InterfaceDeclarationSyntax
            or EnumDeclarationSyntax
            or UnionDeclarationSyntax
            or ModuleDeclarationSyntax
            or ImportStatementSyntax
            or FromImportStatementSyntax
            or TestDeclarationSyntax) {
          continue;
        }

        LowerStatement(fb, item);
      }

      if (!fb.IsTerminated) {
        fb.Emit(new CallDirect(null, shutdownId, []));
        fb.Terminate(new RetVoid(rootModel.Tree.Root.Span));
      }

      FinalizeFunctionBody(fb, rootModel.Tree.Root.Span);
      return entryFn.Id;
    }

    // Program mode (MAIN-2): evaluate top-level val initializers in strict textual order, then call main().
    foreach (SyntaxNode item in rootModel.Tree.Root.Items) {
      if (item is VariableDeclarationSyntax or TupleDestructuringStatementSyntax) {
        PrepareCallableScope(fb, item);
      }
    }

    FunctionSymbol? mainSymbol = null;
    foreach (SyntaxNode item in rootModel.Tree.Root.Items) {
      if (item is VariableDeclarationSyntax or TupleDestructuringStatementSyntax) {
        if (!fb.IsTerminated) {
          LowerStatement(fb, item);
        }
      } else if (item is FunctionDeclarationSyntax fnDecl
          && rootModel.GetDeclaredSymbol(fnDecl) is FunctionSymbol fnSym
          && string.Equals(fnSym.Name, "main", StringComparison.Ordinal)) {
        mainSymbol = fnSym;
      }
    }

    if (mainSymbol is not null && _funcIds.TryGetValue(mainSymbol, out IrFuncId mainFuncId) && !fb.IsTerminated) {
      EmitMainCallAndFailureHandler(fb, mainSymbol, mainFuncId, shutdownId, rootModel.Tree.Root.Span);
    } else if (!fb.IsTerminated) {
      fb.Emit(new CallDirect(null, shutdownId, []));
      fb.Terminate(new RetVoid(rootModel.Tree.Root.Span));
    }

    FinalizeFunctionBody(fb, rootModel.Tree.Root.Span);
    return entryFn.Id;
  }

  private void EmitMainCallAndFailureHandler(
      FunctionBuilder fb,
      FunctionSymbol mainSym,
      IrFuncId mainFuncId,
      IrFuncId shutdownId,
      Text.SourceSpan span) {
    TypeSymbol mainRet = mainSym.ReturnType;
    if (mainRet.IsVoid) {
      fb.Emit(new CallDirect(null, mainFuncId, [], span));
      fb.Emit(new CallDirect(null, shutdownId, [], span));
      fb.Terminate(new RetVoid(span));
      return;
    }

    IrTypeId mainRetIr = InternType(mainRet);
    IrTempId resTmp = fb.AddTemp(mainRetIr, "main_res");
    fb.Emit(new CallDirect(resTmp, mainFuncId, [], span));

    bool isResult = mainRet is UnionType ru
        && ReferenceEquals(ru.Definition, _compilation.Prelude.ResultUnion)
        && ru.TypeArguments.Length == 2;
    bool isOption = mainRet is UnionType ou
        && ReferenceEquals(ou.Definition, _compilation.Prelude.OptionUnion)
        && ou.TypeArguments.Length == 1;

    if (!isResult && !isOption) {
      fb.Emit(new CallDirect(null, shutdownId, [], span));
      fb.Terminate(new RetVoid(span));
      return;
    }

    // ERR-18, ERR-19, ERR-20, ERR-24: check tag (0 = Success/Some, 1 = Failure/None).
    IrTempId tagTmp = fb.AddTemp(_types.Int, "main_tag");
    fb.Emit(new UnionTag(tagTmp, resTmp, span));

    IrBasicBlock bbOk = fb.NewBlock();
    IrBasicBlock bbFail = fb.NewBlock();
    fb.Terminate(new Switch(tagTmp, [(0, bbOk.Id), (1, bbFail.Id)], null, span));

    fb.SwitchTo(bbOk);
    fb.Emit(new CallDirect(null, shutdownId, [], span));
    fb.Terminate(new RetVoid(span));

    fb.SwitchTo(bbFail);
    IrOperand errLine;
    if (isResult) {
      var resUnion = (UnionType) mainRet;
      IrTypeId errIrType = InternType(resUnion.TypeArguments[1]);
      IrTempId errVal = fb.AddTemp(errIrType, "main_err");
      fb.Emit(new UnionField(errVal, resTmp, VariantIndex: 1, FieldIndex: 0, span));

      IrOperand errStr;
      if (errIrType == _types.String) {
        errStr = errVal;
      } else {
        IrTempId fmtErr = fb.AddTemp(_types.String);
        fb.Emit(new CallRuntime(fmtErr, IrRuntimeOp.ToStr, [errVal], AllocRegion.Heap, span));
        errStr = fmtErr;
      }

      IrTempId prefix = fb.EmitConstString("Error: ", span);
      IrTempId combined = fb.AddTemp(_types.String);
      fb.Emit(new StrConcat(combined, prefix, errStr, AllocRegion.Heap, span));
      errLine = combined;
    } else {
      errLine = fb.EmitConstString("Error: Option::None", span);
    }

    fb.Emit(new CallRuntime(null, IrRuntimeOp.Eprintln, [errLine], AllocRegion.Stack, span));
    fb.Emit(new CallDirect(null, shutdownId, [], span));
    IrTempId exitCode = fb.EmitConstInt(1, span);
    fb.Emit(new CallRuntime(null, IrRuntimeOp.Exit, [exitCode], AllocRegion.Stack, span));
    fb.Terminate(new Unreachable(span));
  }
}
