using System.Collections.Frozen;
using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

/// <summary>
/// Computes transitive <see cref="ModuleStateEffects"/> (<see cref="ModuleStateEffects.ReadsModuleState"/>
/// and <see cref="ModuleStateEffects.WritesModuleState"/>) for every declared function and method across
/// a compilation using a Tarjan SCC fixed point over the call graph (D-183). Produces no diagnostics.
/// </summary>
/// <remarks>
/// Calls through function values (lambdas, function-typed parameters/locals, callable fields, indexed
/// function elements, and interface methods whose concrete struct implementation is not statically
/// known) are treated conservatively as unknown and mark both <c>ReadsModuleState</c> and
/// <c>WritesModuleState</c> <c>true</c> for the caller, because static points-to analysis is not
/// performed and the target could read or mutate any module's top-level <c>var</c> or <c>val mut</c>.
/// A closure body's effects count for the function that calls it, not the function that declares it.
/// </remarks>
internal static class ModuleStateEffectAnalyzer {
  private readonly record struct CallableNode(
      Symbol Symbol,
      BlockStatementSyntax Body,
      ModuleCheckState State);

  public static FrozenDictionary<Symbol, ModuleStateEffects> Build(IReadOnlyList<ModuleCheckState> modules) {
    // 1. Collect all top-level mutable module state (`var` and `val mut`) across every module.
    var moduleMutableLocals = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
    var callables = new List<CallableNode>();
    var indexBySymbol = new Dictionary<Symbol, int>(ReferenceEqualityComparer.Instance);

    foreach (ModuleCheckState state in modules) {
      foreach (Symbol sym in state.ModuleScope.DeclaredSymbols.Values) {
        if (sym is LocalSymbol { Kind: LocalKind.Variable, IsMutable: true }) {
          moduleMutableLocals.Add(sym);
        }
      }

      foreach ((FunctionDeclarationSyntax decl, FunctionSymbol fnSym, _) in state.Functions) {
        if (indexBySymbol.TryAdd(fnSym, callables.Count)) {
          callables.Add(new CallableNode(fnSym, decl.Body, state));
        }
      }

      foreach ((StructDeclarationSyntax structDecl, _, _, _) in state.Structs) {
        foreach (SyntaxNode member in structDecl.Members) {
          if (member is MethodDeclarationSyntax methodDecl
              && (uint) methodDecl.Id < (uint) state.DeclaredSymbols.Length
              && state.DeclaredSymbols[methodDecl.Id] is MethodSymbol methodSym
              && indexBySymbol.TryAdd(methodSym, callables.Count)) {
            callables.Add(new CallableNode(methodSym, methodDecl.Body, state));
          }
        }
      }
    }

    if (callables.Count == 0) {
      return FrozenDictionary<Symbol, ModuleStateEffects>.Empty;
    }

    // 2. Analyze direct module-state reads/writes and direct call-graph edges per callable.
    int n = callables.Count;
    bool[] directReads = new bool[n];
    bool[] directWrites = new bool[n];
    var callees = new List<int>[n];

    for (int i = 0; i < n; i++) {
      CallableNode node = callables[i];
      var calleeSet = new HashSet<int>();
      var walker = new BodyEffectWalker(
          node.State,
          moduleMutableLocals,
          indexBySymbol,
          calleeSet);
      walker.WalkNode(node.Body);
      directReads[i] = walker.ReadsModuleState;
      directWrites[i] = walker.WritesModuleState;
      callees[i] = [.. calleeSet];
    }

    // 3. Iterative Tarjan SCC + monotone fixed point over SCCs (reverse topological order).
    int[] index = new int[n];
    int[] lowlink = new int[n];
    bool[] onStack = new bool[n];
    int[] sccId = new int[n];
    Array.Fill(index, -1);
    Array.Fill(sccId, -1);

    var tarjanStack = new Stack<int>();
    var callStack = new Stack<(int Node, int EdgeIndex)>();
    var sccs = new List<List<int>>();
    int nextIndex = 0;

    for (int start = 0; start < n; start++) {
      if (index[start] != -1) {
        continue;
      }

      index[start] = lowlink[start] = nextIndex++;
      onStack[start] = true;
      tarjanStack.Push(start);
      callStack.Push((start, 0));

      while (callStack.Count > 0) {
        (int v, int edgeIdx) = callStack.Pop();
        List<int> edges = callees[v];

        if (edgeIdx < edges.Count) {
          callStack.Push((v, edgeIdx + 1));
          int w = edges[edgeIdx];
          if (index[w] == -1) {
            index[w] = lowlink[w] = nextIndex++;
            onStack[w] = true;
            tarjanStack.Push(w);
            callStack.Push((w, 0));
          } else if (onStack[w] && index[w] < lowlink[v]) {
            lowlink[v] = index[w];
          }
        } else {
          if (lowlink[v] == index[v]) {
            int id = sccs.Count;
            var component = new List<int>();
            while (true) {
              int w = tarjanStack.Pop();
              onStack[w] = false;
              sccId[w] = id;
              component.Add(w);
              if (w == v) {
                break;
              }
            }
            sccs.Add(component);
          }

          if (callStack.Count > 0) {
            int parent = callStack.Peek().Node;
            if (lowlink[v] < lowlink[parent]) {
              lowlink[parent] = lowlink[v];
            }
          }
        }
      }
    }

    // Tarjan produces SCCs in reverse topological order (callees before callers).
    // For each SCC, every member can reach every other member, so the least fixed point
    // assigns the OR-union of all members' direct effects and already-solved callee SCCs.
    int sccCount = sccs.Count;
    bool[] sccReads = new bool[sccCount];
    bool[] sccWrites = new bool[sccCount];

    for (int c = 0; c < sccCount; c++) {
      bool reads = false;
      bool writes = false;
      foreach (int u in sccs[c]) {
        reads |= directReads[u];
        writes |= directWrites[u];
        foreach (int v in callees[u]) {
          int targetScc = sccId[v];
          if (targetScc != c) {
            reads |= sccReads[targetScc];
            writes |= sccWrites[targetScc];
          }
        }
      }

      sccReads[c] = reads;
      sccWrites[c] = writes;
    }

    var results = new Dictionary<Symbol, ModuleStateEffects>(n, ReferenceEqualityComparer.Instance);
    for (int i = 0; i < n; i++) {
      int c = sccId[i];
      results[callables[i].Symbol] = new ModuleStateEffects(sccReads[c], sccWrites[c]);
    }

    // Propagate computed effects to promoted embedded struct methods.
    foreach (ModuleCheckState state in modules) {
      foreach ((_, StructSymbol structSym, _, _) in state.Structs) {
        foreach (MethodSymbol method in structSym.Methods) {
          if (method.PromotedFrom is { } original && results.TryGetValue(original, out ModuleStateEffects effects)) {
            results[method] = effects;
          }
        }
      }
    }

    return results.ToFrozenDictionary(ReferenceEqualityComparer.Instance);
  }

  private sealed class BodyEffectWalker(
      ModuleCheckState state,
      HashSet<Symbol> moduleMutableLocals,
      Dictionary<Symbol, int> indexBySymbol,
      HashSet<int> callees) {
    public bool ReadsModuleState { get; private set; }
    public bool WritesModuleState { get; private set; }

    public void WalkNode(SyntaxNode node) {
      // Closure bodies (lambdas, implicit-`it` lambdas) and nested declarations do not execute
      // when declared: a closure body's facts count for the function that calls it, not the
      // function that declares it (D-183).
      if (node is LambdaExpressionSyntax
          or FunctionDeclarationSyntax
          or StructDeclarationSyntax
          or InterfaceDeclarationSyntax
          or EnumDeclarationSyntax
          or UnionDeclarationSyntax) {
        return;
      }

      if (IsImplicitItLambda(node)) {
        return;
      }

      switch (node) {
        case AssignmentExpressionSyntax assign:
          WalkAssignment(assign);
          return;

        case CallExpressionSyntax call:
          WalkCall(call);
          return;

        case IdentifierExpressionSyntax id:
          if (GetReferencedSymbol(id) is { } idSym && moduleMutableLocals.Contains(idSym)) {
            ReadsModuleState = true;
          }
          return;

        case StructFieldInitSyntax fieldInit:
          if (fieldInit.Value is not null) {
            WalkNode(fieldInit.Value);
          } else if (GetShorthandValueSymbol(fieldInit) is { } shorthandSym && moduleMutableLocals.Contains(shorthandSym)) {
            ReadsModuleState = true;
          }
          return;

        case TupleFieldSyntax tupleField:
          if (tupleField.Value is not null) {
            WalkNode(tupleField.Value);
          } else if (GetReferencedSymbol(tupleField) is { } tupleSym && moduleMutableLocals.Contains(tupleSym)) {
            ReadsModuleState = true;
          }
          return;
      }

      foreach (SyntaxElement child in node.GetChildren()) {
        if (child.IsNode) {
          WalkNode(child.AsNode());
        }
      }
    }

    private void WalkAssignment(AssignmentExpressionSyntax assign) {
      bool isCompound = assign.OperatorToken.Kind != TokenKind.Assign;
      WalkWriteTargetPlace(assign.Target, alsoReadsRoot: isCompound);
      WalkNode(assign.Value);

      if (GetMutationPlace(assign) is { RootSymbol: { } root } && moduleMutableLocals.Contains(root)) {
        WritesModuleState = true;
        if (isCompound) {
          ReadsModuleState = true;
        }
      }
    }

    private void WalkWriteTargetPlace(ExpressionSyntax place, bool alsoReadsRoot) {
      while (place is ParenthesizedExpressionSyntax paren) {
        place = paren.Expression;
      }

      switch (place) {
        case IdentifierExpressionSyntax id:
          if (GetReferencedSymbol(id) is { } sym && moduleMutableLocals.Contains(sym)) {
            WritesModuleState = true;
            if (alsoReadsRoot) {
              ReadsModuleState = true;
            }
          }
          return;

        case MemberAccessExpressionSyntax member:
          WalkWriteTargetPlace(member.Target, alsoReadsRoot);
          return;

        case IndexExpressionSyntax index:
          WalkWriteTargetPlace(index.Target, alsoReadsRoot);
          WalkNode(index.Index);
          return;

        default:
          WalkNode(place);
          return;
      }
    }

    private void WalkCall(CallExpressionSyntax call) {
      CallResolution? resolution = GetCallResolution(call);

      if (resolution is null) {
        // Unresolved or error-recovered call: walk children normally without failing.
        WalkNode(call.Callee);
        WalkCallArguments(call, skipCallbackArgIndex: -1);
        return;
      }

      switch (resolution.Kind) {
        case CallKind.DirectFunction:
          WalkCalleeSubexpressions(call.Callee);
          WalkCallArguments(call, skipCallbackArgIndex: -1);
          AddKnownCallee(resolution.TargetSymbol);
          break;

        case CallKind.StaticMethod:
          WalkCalleeSubexpressions(call.Callee);
          WalkCallArguments(call, skipCallbackArgIndex: -1);
          AddKnownCallee(resolution.TargetSymbol);
          break;

        case CallKind.InstanceMethod: {
            MethodSymbol? method = resolution.TargetSymbol as MethodSymbol;
            MethodSymbol? canonical = method?.PromotedFrom ?? method;
            if (call.Callee is MemberAccessExpressionSyntax memberCallee) {
              if (method is { IsMutating: true }) {
                WalkWriteTargetPlace(memberCallee.Target, alsoReadsRoot: true);
              } else {
                WalkNode(memberCallee.Target);
              }
            } else {
              WalkCalleeSubexpressions(call.Callee);
            }

            WalkCallArguments(call, skipCallbackArgIndex: -1);

            if (canonical is null) {
              break;
            }

            if (canonical.ContainingType is InterfaceSymbol) {
              // Dynamic dispatch through an interface: concrete struct implementation is unknown.
              MarkUnknownCallEffects();
            } else {
              AddKnownCallee(canonical);
            }
            break;
          }

        case CallKind.IndirectFunctionValue:
        case CallKind.CallableField:
        case CallKind.BoundMethodValue:
        case CallKind.IndexedElementCall:
          // D-183: a call through a function value (lambda, function-typed field/parameter,
          // bound method value, or indexed function element) is treated as unknown -> both true.
          WalkNode(call.Callee);
          if (resolution.IndexSymbol is { } idxSym && moduleMutableLocals.Contains(idxSym)) {
            ReadsModuleState = true;
          }
          WalkCallArguments(call, skipCallbackArgIndex: -1);
          MarkUnknownCallEffects();
          break;

        case CallKind.UnionVariantConstructor:
          WalkCalleeSubexpressions(call.Callee);
          WalkCallArguments(call, skipCallbackArgIndex: -1);
          break;

        case CallKind.Intrinsic:
          WalkIntrinsicCall(call, resolution.Intrinsic);
          break;
      }

      if (GetMutationPlace(call) is { RootSymbol: { } mutRoot } && moduleMutableLocals.Contains(mutRoot)) {
        WritesModuleState = true;
      }
    }

    private void WalkIntrinsicCall(CallExpressionSyntax call, IntrinsicId? intrinsic) {
      if (call.Callee is MemberAccessExpressionSyntax memberCallee) {
        if (intrinsic is IntrinsicId.VecPush or IntrinsicId.SetAdd) {
          WalkWriteTargetPlace(memberCallee.Target, alsoReadsRoot: false);
        } else if (intrinsic is IntrinsicId.VecPop or IntrinsicId.VecSwap or IntrinsicId.MapRemove or IntrinsicId.SetRemove) {
          WalkWriteTargetPlace(memberCallee.Target, alsoReadsRoot: true);
        } else {
          WalkNode(memberCallee.Target);
        }
      } else {
        WalkCalleeSubexpressions(call.Callee);
      }

      int callbackArgIndex = IsCallbackIntrinsic(intrinsic) && call.Arguments.Length > 0 ? 0 : -1;
      WalkCallArguments(call, callbackArgIndex);

      if (callbackArgIndex >= 0) {
        WalkSynchronousCallbackArgument(call.Arguments[callbackArgIndex].Expression);
      }
    }

    private void WalkSynchronousCallbackArgument(ExpressionSyntax callbackExpr) {
      ExpressionSyntax unwrapped = callbackExpr;
      while (unwrapped is ParenthesizedExpressionSyntax paren) {
        unwrapped = paren.Expression;
      }

      if (unwrapped is LambdaExpressionSyntax lambda) {
        if (lambda.BodyExpression is { } bodyExpr) {
          WalkNode(bodyExpr);
        } else if (lambda.BodyBlock is { } bodyBlock) {
          WalkNode(bodyBlock);
        }
        return;
      }

      if (IsImplicitItLambda(callbackExpr)) {
        WalkImplicitItBody(callbackExpr);
        return;
      }

      if (unwrapped is IdentifierExpressionSyntax or StaticAccessExpressionSyntax) {
        Symbol? target = GetReferencedSymbol(unwrapped);
        if (target is FunctionSymbol or MethodSymbol) {
          AddKnownCallee(target);
          return;
        }
      } else if (unwrapped is MemberAccessExpressionSyntax memberTarget) {
        WalkNode(memberTarget.Target);
        if (GetReferencedSymbol(memberTarget) is MethodSymbol methodTarget) {
          MethodSymbol canonical = methodTarget.PromotedFrom ?? methodTarget;
          if (canonical.ContainingType is InterfaceSymbol) {
            MarkUnknownCallEffects();
          } else {
            AddKnownCallee(canonical);
          }
          return;
        }
      }

      // Callback is an arbitrary function value (e.g., parameter, local, callable field, call result).
      WalkNode(callbackExpr);
      MarkUnknownCallEffects();
    }

    private void WalkImplicitItBody(SyntaxNode root) {
      // Walk inside an implicit-`it` argument being invoked synchronously, without triggering
      // the outer IsImplicitItLambda skip on `root` itself.
      if (root is AssignmentExpressionSyntax assign) {
        WalkAssignment(assign);
        return;
      }

      if (root is CallExpressionSyntax call) {
        WalkCall(call);
        return;
      }

      if (root is IdentifierExpressionSyntax id) {
        if (GetReferencedSymbol(id) is { } idSym && moduleMutableLocals.Contains(idSym)) {
          ReadsModuleState = true;
        }
        return;
      }

      foreach (SyntaxElement child in root.GetChildren()) {
        if (child.IsNode) {
          WalkNode(child.AsNode());
        }
      }
    }

    private void WalkCallArguments(CallExpressionSyntax call, int skipCallbackArgIndex) {
      for (int i = 0; i < call.Arguments.Length; i++) {
        if (i == skipCallbackArgIndex) {
          continue;
        }

        ArgumentSyntax arg = call.Arguments[i];
        if (GetMutationPlace(arg) is { RootSymbol: { } mutRoot } && moduleMutableLocals.Contains(mutRoot)) {
          WritesModuleState = true;
          ReadsModuleState = true;
        }

        WalkNode(arg.Expression);
      }
    }

    private void WalkCalleeSubexpressions(ExpressionSyntax callee) {
      if (callee is MemberAccessExpressionSyntax member) {
        WalkNode(member.Target);
      }
    }

    private void AddKnownCallee(Symbol? target) {
      if (target is MethodSymbol method && method.PromotedFrom is { } original) {
        target = original;
      }

      if (target is not null && indexBySymbol.TryGetValue(target, out int targetIdx)) {
        callees.Add(targetIdx);
      }
    }

    private void MarkUnknownCallEffects() {
      ReadsModuleState = true;
      WritesModuleState = true;
    }

    private bool IsImplicitItLambda(SyntaxNode node) =>
        (uint) node.Id < (uint) state.Conversions.Length
        && state.Conversions[node.Id] is { Kind: ConversionKind.ImplicitItLambda };

    private Symbol? GetReferencedSymbol(SyntaxNode node) =>
        (uint) node.Id < (uint) state.ReferencedSymbols.Length
            ? state.ReferencedSymbols[node.Id]
            : null;

    private Symbol? GetShorthandValueSymbol(StructFieldInitSyntax node) =>
        (uint) node.Id < (uint) state.ShorthandValueSymbols.Length
            ? state.ShorthandValueSymbols[node.Id]
            : null;

    private CallResolution? GetCallResolution(CallExpressionSyntax call) =>
        (uint) call.Id < (uint) state.CallResolutions.Length
            ? state.CallResolutions[call.Id]
            : null;

    private MutationPlaceInfo? GetMutationPlace(SyntaxNode node) =>
        (uint) node.Id < (uint) state.MutationPlaces.Length
            ? state.MutationPlaces[node.Id]
            : null;

    private static bool IsCallbackIntrinsic(IntrinsicId? intrinsic) =>
        intrinsic is IntrinsicId.VecEach1
            or IntrinsicId.VecEach2
            or IntrinsicId.VecMap1
            or IntrinsicId.VecMap2
            or IntrinsicId.VecFilter1
            or IntrinsicId.VecFilter2
            or IntrinsicId.VecFind1
            or IntrinsicId.VecFind2
            or IntrinsicId.VecFindIndex1
            or IntrinsicId.VecFindIndex2
            or IntrinsicId.VecAny1
            or IntrinsicId.VecAny2
            or IntrinsicId.VecAll1
            or IntrinsicId.VecAll2
            or IntrinsicId.VecCount1
            or IntrinsicId.VecCount2
            or IntrinsicId.SetAny
            or IntrinsicId.SetAll
            or IntrinsicId.SetEach;
  }
}
