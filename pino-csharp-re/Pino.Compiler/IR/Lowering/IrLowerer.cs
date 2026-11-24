using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.IR;

/// <summary>
/// Thrown when <see cref="IrLowerer"/> encounters a language construct deferred to a later
/// lowering ticket (closures/lambdas, interfaces, user generic monomorphization, or callback intrinsics).
/// </summary>
public sealed class IrLoweringUnsupportedException(string message) : NotSupportedException(message);

/// <summary>
/// Core AST + <see cref="SemanticModel"/> to PIR lowering pass (<c>056-re-ir-design</c> §3, §5.1–§5.10).
/// Never re-runs type resolution or symbol lookup from the AST; all resolved types, symbols,
/// conversions, call resolutions, loop kinds, pattern bindings, mutation places, module-state
/// effects, and D-184 checks come directly from <see cref="Compilation"/> and <see cref="SemanticModel"/>.
/// </summary>
public sealed partial class IrLowerer {
  /// <summary>Exact runtime panic message for division or modulo by zero (TYPE-E4).</summary>
  public const string PanicDivByZero = "Cannot divide by zero.";

  /// <summary>Exact runtime panic message for INT64_MIN // -1 or % -1 overflow (TYPE-E6, D-186).</summary>
  public const string PanicIntMinDivOverflow =
      "Integer division overflowed: the smallest 64-bit integer cannot be divided by -1.";

  private readonly Compilation _compilation;
  private readonly IrTypeTable _types;
  private readonly IrModule _module;
  private readonly Dictionary<ModuleSymbol, SemanticModel> _modelsByModule =
      new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<FunctionSymbol, IrFuncId> _funcIds =
      new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<MethodSymbol, IrFuncId> _methodIds =
      new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<LocalSymbol, IrGlobalId> _globalIds =
      new(ReferenceEqualityComparer.Instance);
  private readonly Dictionary<IrTypeId, IrFuncId> _eqHelpers = [];
  private readonly List<(SemanticModel Model, FunctionDeclarationSyntax Decl, FunctionSymbol Symbol, IrFuncId FuncId)> _pendingFunctions = [];
  private readonly List<(SemanticModel Model, StructSymbol Owner, MethodDeclarationSyntax Decl, MethodSymbol Symbol, IrFuncId FuncId)> _pendingMethods = [];
  private readonly HashSet<string> _usedFunctionNames = new(StringComparer.Ordinal);
  private readonly HashSet<string> _usedGlobalNames = new(StringComparer.Ordinal);

  private IrLowerer(Compilation compilation, IrModule? existingModule = null) {
    _compilation = compilation;
    if (existingModule is not null) {
      _module = existingModule;
      _types = existingModule.Types;
      _types.UpdateReachability(compilation.Reachability);
      foreach (IrFunction fn in existingModule.Functions) {
        _usedFunctionNames.Add(fn.Name);
      }

      foreach (IrGlobal g in existingModule.Globals) {
        _usedGlobalNames.Add(g.Name);
      }
    } else {
      _types = new IrTypeTable(compilation.Reachability);
      _module = new IrModule(_types, compilation.RootModel.IsProgramMode);
    }
  }

  /// <summary>
  /// Lowers a checked, error-free <see cref="Compilation"/> into a verified-ready <see cref="IrModule"/>.
  /// </summary>
  public static IrModule Lower(Compilation compilation) {
    ArgumentNullException.ThrowIfNull(compilation);
    foreach (Diagnostic diag in compilation.Diagnostics) {
      if (diag.Severity == DiagnosticSeverity.Error) {
        throw new InvalidOperationException(
            $"Cannot lower compilation with errors: {diag.Code}: {diag.Message}");
      }
    }

    var lowerer = new IrLowerer(compilation);
    lowerer.LowerCompilation();
    return lowerer._module;
  }

  /// <summary>
  /// Lowers only the newly submitted REPL items (<paramref name="committedItemCount"/>..end) into an
  /// incremental <c>__repl_chunk_N</c> function on <paramref name="module"/>, binding prior globals,
  /// functions, and methods to their existing IDs without re-executing earlier chunks.
  /// </summary>
  public static IrFuncId LowerReplChunk(
      Compilation compilation,
      IrModule module,
      int committedItemCount,
      int chunkNumber,
      IReadOnlyDictionary<string, IrGlobalId> existingGlobals,
      IReadOnlyDictionary<string, IrFuncId> existingFunctions,
      IReadOnlyDictionary<string, IrFuncId> existingMethods) {
    ArgumentNullException.ThrowIfNull(compilation);
    ArgumentNullException.ThrowIfNull(module);
    ArgumentNullException.ThrowIfNull(existingGlobals);
    ArgumentNullException.ThrowIfNull(existingFunctions);
    ArgumentNullException.ThrowIfNull(existingMethods);

    foreach (Diagnostic diag in compilation.Diagnostics) {
      if (diag.Severity == DiagnosticSeverity.Error) {
        throw new InvalidOperationException(
            $"Cannot lower compilation with errors: {diag.Code}: {diag.Message}");
      }
    }

    var lowerer = new IrLowerer(compilation, module);
    return lowerer.LowerReplChunkCore(
        committedItemCount,
        chunkNumber,
        existingGlobals,
        existingFunctions,
        existingMethods);
  }

  /// <summary>Formats the exact FN-34 panic text for aliased dynamic <c>mut</c> arguments (D-184).</summary>
  public static string FormatAliasedMutPanic(string collectionName, string targetDisplay) =>
      $"Cannot pass the same element of '{collectionName}' as two 'mut' arguments to {targetDisplay}.";

  /// <summary>Formats the exact POOL-E1 panic text for a stale <c>Handle[T]</c> lookup (D-185).</summary>
  public static string FormatPoolStaleHandlePanic(string poolName) =>
      $"This handle no longer points to a value in '{poolName}'; it was removed.";

  private IrTypeId InternType(TypeSymbol type) => _types.Intern(type);

  private IrTypeId InternType(FunctionBuilder fb, TypeSymbol type) =>
      _types.Intern(fb.ResolveType(type));

  private IrOperand CoerceValueToExpectedType(
      FunctionBuilder fb,
      IrOperand val,
      TypeSymbol actualConcreteType,
      TypeSymbol expectedConcreteType,
      SourceSpan span) {
    IrTypeId actualIr = fb.GetOperandType(val);
    IrTypeId expectedIr = InternType(expectedConcreteType);
    if (actualIr == expectedIr) {
      return val;
    }

    if (actualIr == _types.Int && expectedIr == _types.Float) {
      IrTempId widened = fb.AddTemp(_types.Float);
      fb.Emit(new CastI64ToF64(widened, val, span));
      return widened;
    }

    if (expectedConcreteType is InterfaceType targetIface) {
      return EmitStructToInterface(fb, val, actualConcreteType, targetIface, span);
    }

    return val;
  }

  private AllocRegion UnionAllocRegion(IrTypeId unionTypeId) =>
      _types.GetType(unionTypeId).IsRecursiveUnion ? AllocRegion.Heap : AllocRegion.Stack;

  private string ReserveFunctionName(string preferred) {
    if (_usedFunctionNames.Add(preferred)) {
      return preferred;
    }

    int suffix = 1;
    while (true) {
      string candidate = $"{preferred}${suffix}";
      if (_usedFunctionNames.Add(candidate)) {
        return candidate;
      }

      suffix++;
    }
  }

  private string ReserveGlobalName(string preferred) {
    if (_usedGlobalNames.Add(preferred)) {
      return preferred;
    }

    int suffix = 1;
    while (true) {
      string candidate = $"{preferred}_{suffix}";
      if (_usedGlobalNames.Add(candidate)) {
        return candidate;
      }

      suffix++;
    }
  }

  private static string NameOf(SemanticModel model, Token token) =>
      model.Tree.Lex.GetText(token).ToString();

  private static ExpressionSyntax UnwrapParens(ExpressionSyntax expr) {
    while (expr is ParenthesizedExpressionSyntax paren) {
      expr = paren.Expression;
    }

    return expr;
  }

  private static bool IsSelfLocal(LocalSymbol local) =>
      string.Equals(local.Name, "self", StringComparison.Ordinal)
      && ((local.ContainingSymbol is MethodSymbol m && local.DeclaringNodeId == m.DeclaringNodeId)
          || (local.ContainingSymbol is StructSymbol s && local.DeclaringNodeId == s.DeclaringNodeId));

  private static string ExtractFirstIdentifier(SemanticModel model, ExpressionSyntax expr) {
    ExpressionSyntax current = UnwrapParens(expr);
    while (true) {
      switch (current) {
        case MemberAccessExpressionSyntax mem:
          current = UnwrapParens(mem.Target);
          break;
        case IndexExpressionSyntax idx:
          current = UnwrapParens(idx.Target);
          break;
        case IdentifierExpressionSyntax id:
          return NameOf(model, id.IdentifierToken);
        default:
          return "pool";
      }
    }
  }

  private int FindStructFieldIndex(StructSymbol structSym, FieldSymbol field) {
    if (!field.IsPromotedFromEmbedded) {
      for (int i = 0; i < structSym.Fields.Length; i++) {
        if (ReferenceEquals(structSym.Fields[i], field)
            || string.Equals(structSym.Fields[i].Name, field.Name, StringComparison.Ordinal)) {
          return i;
        }
      }
    }

    return field.Ordinal;
  }

  private static int FindEmbeddedStructFieldIndex(StructSymbol outer, StructSymbol embedded) {
    for (int i = 0; i < outer.EmbeddedStructs.Length; i++) {
      if (ReferenceEquals(outer.EmbeddedStructs[i], embedded)) {
        return outer.Fields.Length + i;
      }
    }

    throw new InvalidOperationException(
        $"Embedded struct '{embedded.Name}' not found in '{outer.Name}'.");
  }

  private static int FindDirectFieldIndexByName(StructSymbol owner, string fieldName) {
    for (int i = 0; i < owner.Fields.Length; i++) {
      if (string.Equals(owner.Fields[i].Name, fieldName, StringComparison.Ordinal)) {
        return i;
      }
    }

    throw new InvalidOperationException(
        $"Direct field '{fieldName}' not found in struct '{owner.Name}'.");
  }

  private static int FindTupleFieldIndex(IrTupleType tupleType, string label) {
    for (int i = 0; i < tupleType.Fields.Length; i++) {
      if (string.Equals(tupleType.Fields[i].Label, label, StringComparison.Ordinal)) {
        return i;
      }
    }

    throw new InvalidOperationException(
        $"Tuple field '{label}' not found in '{tupleType.Name}'.");
  }

  private static long ParseIntLiteral(string rawText) {
    string cleaned = rawText.Replace("_", string.Empty, StringComparison.Ordinal);
    return long.Parse(cleaned, NumberStyles.None, CultureInfo.InvariantCulture);
  }

  private static double ParseFloatLiteral(string rawText) {
    string cleaned = rawText.Replace("_", string.Empty, StringComparison.Ordinal);
    return double.Parse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture);
  }

  private static int DecodeRuneLiteral(string text) {
    if (text.Length >= 3 && text[0] == '\'' && text[^1] == '\'') {
      string inner = text[1..^1];
      if (inner.StartsWith('\\') && inner.Length == 2) {
        return inner[1] switch {
          'n' => '\n',
          't' => '\t',
          'r' => '\r',
          '\'' => '\'',
          '\\' => '\\',
          _ => inner[1],
        };
      }

      if (Rune.DecodeFromUtf16(inner, out Rune rune, out int consumed) == System.Buffers.OperationStatus.Done
          && consumed == inner.Length) {
        return rune.Value;
      }
    }

    throw new InvalidOperationException($"Malformed rune literal '{text}'.");
  }

  private static string UnescapeStringText(string text) {
    if (!text.Contains('\\', StringComparison.Ordinal)) {
      return text;
    }

    var sb = new StringBuilder(text.Length);
    for (int i = 0; i < text.Length; i++) {
      char ch = text[i];
      if (ch == '\\' && i + 1 < text.Length) {
        char next = text[++i];
        sb.Append(next switch {
          'n' => '\n',
          't' => '\t',
          'r' => '\r',
          '"' => '"',
          '\\' => '\\',
          '$' => '$',
          _ => next,
        });
      } else {
        sb.Append(ch);
      }
    }

    return sb.ToString();
  }

  private readonly record struct LoopTarget(IrBlockId BreakBlock, IrBlockId ContinueBlock);

  private readonly record struct YieldTarget(IrSlotId? ResultSlot, IrBlockId MergeBlock, TypeSymbol? ExpectedType);

  private readonly record struct InlineReturnTarget(
      IrSlotId? ResultSlot,
      IrBlockId ExitBlock,
      TypeSymbol? ExpectedReturnType);

  private sealed class FunctionBuilder {
    private readonly Dictionary<string, int> _usedSlotNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _usedTempNames = new(StringComparer.Ordinal);

    public FunctionBuilder(
        IrLowerer owner,
        IrFunction function,
        SemanticModel model,
        StructSymbol? enclosingStruct = null,
        bool isMutatingMethod = false,
        TypeSymbol? declaredReturnType = null,
        ImmutableArray<TypeParameterSymbol> typeParams = default,
        ImmutableArray<TypeSymbol> typeArgs = default) {
      Owner = owner;
      Function = function;
      Model = model;
      EnclosingStruct = enclosingStruct;
      IsMutatingMethod = isMutatingMethod;
      DeclaredReturnType = declaredReturnType;
      TypeParams = typeParams.IsDefault ? [] : typeParams;
      TypeArgs = typeArgs.IsDefault ? [] : typeArgs;
      CurrentBlock = function.AddBlock();
    }

    public IrLowerer Owner { get; }

    public IrFunction Function { get; }

    public SemanticModel Model { get; }

    public StructSymbol? EnclosingStruct { get; }

    public bool IsMutatingMethod { get; }

    public TypeSymbol? DeclaredReturnType { get; }

    public ImmutableArray<TypeParameterSymbol> TypeParams { get; }

    public ImmutableArray<TypeSymbol> TypeArgs { get; }

    public IrBasicBlock CurrentBlock { get; set; }

    public bool IsTerminated => CurrentBlock.Terminator is not null;

    public Dictionary<Symbol, IrSlotId> LocalSlots { get; } = new(ReferenceEqualityComparer.Instance);

    public Dictionary<Symbol, IrOperand> LocalTemps { get; } = new(ReferenceEqualityComparer.Instance);

    public Dictionary<Symbol, int> ParamIndices { get; } = new(ReferenceEqualityComparer.Instance);

    public HashSet<LocalSymbol> BoxedCellSymbols { get; } = new(ReferenceEqualityComparer.Instance);

    public Dictionary<LocalSymbol, IrSlotId> CellSlots { get; } = new(ReferenceEqualityComparer.Instance);

    public Dictionary<LocalSymbol, IrOperand> CellOperands { get; } = new(ReferenceEqualityComparer.Instance);

    public IrOperand? CapturedSelf { get; set; }

    public Dictionary<FunctionSymbol, IrFuncId> LocalFuncIds { get; } = new(ReferenceEqualityComparer.Instance);

    public Dictionary<FunctionSymbol, CapturingLocalInfo> CapturingLocals { get; } = new(ReferenceEqualityComparer.Instance);

    public Stack<LoopTarget> LoopStack { get; } = new();

    public Stack<YieldTarget> YieldStack { get; } = new();

    public Stack<InlineReturnTarget> InlineReturnStack { get; } = new();

    public TypeSymbol ResolveType(TypeSymbol type) =>
        TypeParams.IsEmpty
            ? type
            : TypeSubstitution.Substitute(type, TypeParams, TypeArgs, Owner._compilation.Types);

    public TypeSymbol GetNodeType(SyntaxNode node) =>
        ResolveType(Model.GetType(node)
            ?? throw new InvalidOperationException($"Missing semantic type on '{node.GetType().Name}'."));

    public IrBasicBlock NewBlock() => Function.AddBlock();

    public void SwitchTo(IrBasicBlock block) => CurrentBlock = block;

    public IrSlotId AddSlot(string preferredName, IrTypeId type, bool isMutSlot = false, bool isBoxedCell = false) {
      string unique = ReserveUnique(_usedSlotNames, preferredName);
      return Function.AddSlot(unique, type, isMutSlot, isBoxedCell);
    }

    public IrTempId AddTemp(IrTypeId type, string? preferredName = null) {
      string? unique = preferredName is null ? null : ReserveUnique(_usedTempNames, preferredName);
      return Function.AddTemp(type, unique);
    }

    public void Emit(IrInstruction instr) {
      if (IsTerminated) {
        return;
      }

      CurrentBlock.Instructions.Add(instr);
    }

    public void Terminate(IrTerminator terminator) {
      if (IsTerminated) {
        return;
      }

      CurrentBlock.Terminator = terminator;
    }

    public IrTempId EmitConstInt(long value, SourceSpan span = default, string? name = null) {
      IrTempId tmp = AddTemp(Owner._types.Int, name);
      Emit(new ConstInt(tmp, value, span));
      return tmp;
    }

    public IrTempId EmitConstFloat(double value, SourceSpan span = default, string? name = null) {
      IrTempId tmp = AddTemp(Owner._types.Float, name);
      Emit(new ConstFloat(tmp, value, span));
      return tmp;
    }

    public IrTempId EmitConstBool(bool value, SourceSpan span = default, string? name = null) {
      IrTempId tmp = AddTemp(Owner._types.Bool, name);
      Emit(new ConstBool(tmp, value, span));
      return tmp;
    }

    public IrTempId EmitConstRune(int codePoint, SourceSpan span = default, string? name = null) {
      IrTempId tmp = AddTemp(Owner._types.Rune, name);
      Emit(new ConstRune(tmp, codePoint, span));
      return tmp;
    }

    public IrTempId EmitConstString(string value, SourceSpan span = default, string? name = null) {
      int poolId = Owner._module.InternString(value);
      IrTempId tmp = AddTemp(Owner._types.String, name);
      Emit(new ConstString(tmp, poolId, span));
      return tmp;
    }

    public IrTypeId GetOperandType(IrOperand op) => op.Kind switch {
      IrOperandKind.Temp => Function.Temps[op.Index].Type,
      IrOperandKind.Param => Function.Parameters[op.Index].Type,
      _ => IrTypeId.Invalid,
    };

    private static string ReserveUnique(Dictionary<string, int> counts, string preferred) {
      if (!counts.TryGetValue(preferred, out int count)) {
        counts[preferred] = 1;
        return preferred;
      }

      while (true) {
        string candidate = $"{preferred}_{count}";
        count++;
        if (!counts.ContainsKey(candidate)) {
          counts[preferred] = count;
          counts[candidate] = 1;
          return candidate;
        }
      }
    }
  }
}
