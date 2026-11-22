using System.Collections.Frozen;
using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed class ModuleCheckState {
  public ModuleCheckState(
      ParseResult tree,
      ModuleSymbol module,
      bool isRoot,
      bool isTestFile,
      Scope moduleScope,
      SyntaxNode?[] parentsByNodeId) {
    Tree = tree;
    Module = module;
    IsRoot = isRoot;
    IsTestFile = isTestFile;
    ModuleScope = moduleScope;

    int nodeCount = Math.Max(0, tree.NodeCount);
    NodeTypes = new TypeSymbol?[nodeCount];
    ReferencedSymbols = new Symbol?[nodeCount];
    DeclaredSymbols = new Symbol?[nodeCount];
    Conversions = new ConversionInfo?[nodeCount];
    CallResolutions = new CallResolution?[nodeCount];
    ScopesByBlockId = new Scope?[nodeCount];
    ConstantValues = new ConstantValue?[nodeCount];
    CapturesByCallableId = new CaptureList?[nodeCount];
    LoopInfos = new LoopInfo?[nodeCount];
    PatternBindings = new PatternBindingInfo?[nodeCount];
    ParentsByNodeId = parentsByNodeId;
    ShorthandValueSymbols = new Symbol?[nodeCount];
    MutationPlaces = new MutationPlaceInfo?[nodeCount];
    RuntimeDisjointnessChecks = new ImmutableArray<RuntimeDisjointnessCheck>[nodeCount];

    if ((uint) tree.Root.Id < (uint) ScopesByBlockId.Length) {
      ScopesByBlockId[tree.Root.Id] = moduleScope;
    }

    Diagnostics.AddRange(tree.Diagnostics);
  }

  public ParseResult Tree { get; }

  public LexResult Lex => Tree.Lex;

  public string? FilePath => Tree.Source.FilePath;

  public ModuleSymbol Module { get; }

  public bool IsRoot { get; }

  public bool IsTestFile { get; }

  public bool IsProgramMode { get; set; }

  public bool IsPhase2Complete { get; set; }

  public bool SuppressInstantiationRecording { get; set; }

  public Action<StructSymbol>? EnsureStructMethodsResolved { get; set; }

  public Scope ModuleScope { get; }

  private DiagnosticBag _diagnostics = new();
  private HashSet<Symbol> _assignedSymbols = new(ReferenceEqualityComparer.Instance);

  public DiagnosticBag Diagnostics => _diagnostics;

  public DiagnosticBag SwapDiagnostics(DiagnosticBag replacement) {
    DiagnosticBag previous = _diagnostics;
    _diagnostics = replacement;
    return previous;
  }

  public HashSet<Symbol> SwapAssignedSymbols(HashSet<Symbol> replacement) {
    HashSet<Symbol> previous = _assignedSymbols;
    _assignedSymbols = replacement;
    return previous;
  }

  public Dictionary<string, ModuleSymbol> ImportedModules { get; } = new(StringComparer.Ordinal);

  public List<Symbol> TopLevelDeclarations { get; } = [];

  public void AddTopLevelValue(LocalSymbol symbol) {
    TopLevelDeclarations.Add(symbol);
    var exports = new Dictionary<string, Symbol>(StringComparer.Ordinal);
    foreach (Symbol decl in TopLevelDeclarations) {
      if (decl.Visibility == SymbolVisibility.Public && !exports.ContainsKey(decl.Name)) {
        exports.Add(decl.Name, decl);
      }
    }

    Module.SetMembers(
        exports.ToFrozenDictionary(StringComparer.Ordinal),
        [.. TopLevelDeclarations]);
  }

  public List<(TypeSymbol Symbol, Scope Scope)> DeclaredTypeItems { get; } = [];

  public List<(FunctionDeclarationSyntax Syntax, FunctionSymbol Symbol, Scope Scope)> Functions { get; } = [];

  public List<(StructDeclarationSyntax Syntax, StructSymbol Symbol, Scope TypeParamScope, Scope EnclosingScope)> Structs { get; } = [];

  public List<(InterfaceDeclarationSyntax Syntax, InterfaceSymbol Symbol, Scope Scope)> Interfaces { get; } = [];

  public List<(EnumDeclarationSyntax Syntax, EnumSymbol Symbol, Scope Scope)> Enums { get; } = [];

  public List<(UnionDeclarationSyntax Syntax, UnionSymbol Symbol, Scope Scope)> Unions { get; } = [];

  public List<(
      TypeSyntax ArgumentSyntax,
      TypeParameterSymbol TypeParameter,
      TypeSymbol ArgumentType,
      ImmutableArray<TypeParameterSymbol> OwnerTypeParameters,
      ImmutableArray<TypeSymbol> OwnerTypeArguments)> DeferredConstraintChecks { get; } = [];

  public List<TypeSymbol> ResolvedTypes { get; } = [];

  public List<Scope> AllScopes { get; } = [];

  public HashSet<Symbol> AssignedSymbols => _assignedSymbols;

  public TypeSymbol?[] NodeTypes { get; }

  public Symbol?[] ReferencedSymbols { get; }

  public Symbol?[] DeclaredSymbols { get; }

  public ConversionInfo?[] Conversions { get; }

  public CallResolution?[] CallResolutions { get; }

  public Scope?[] ScopesByBlockId { get; }

  public ConstantValue?[] ConstantValues { get; }

  public CaptureList?[] CapturesByCallableId { get; }

  public LoopInfo?[] LoopInfos { get; }

  public PatternBindingInfo?[] PatternBindings { get; }

  public SyntaxNode?[] ParentsByNodeId { get; }

  public Symbol?[] ShorthandValueSymbols { get; }

  public MutationPlaceInfo?[] MutationPlaces { get; }

  public ImmutableArray<RuntimeDisjointnessCheck>[] RuntimeDisjointnessChecks { get; }

  public void SetScope(SyntaxNode node, Scope scope) {
    AllScopes.Add(scope);
    if ((uint) node.Id < (uint) ScopesByBlockId.Length) {
      ScopesByBlockId[node.Id] = scope;
    }
  }

  public void SetDeclaredSymbol(SyntaxNode node, Symbol symbol) {
    if ((uint) node.Id < (uint) DeclaredSymbols.Length) {
      DeclaredSymbols[node.Id] = symbol;
    }
  }

  public void SetReferencedSymbol(SyntaxNode node, Symbol symbol) {
    if ((uint) node.Id < (uint) ReferencedSymbols.Length) {
      ReferencedSymbols[node.Id] = symbol;
    }
  }

  public void SetNodeType(SyntaxNode node, TypeSymbol type) {
    if ((uint) node.Id < (uint) NodeTypes.Length) {
      NodeTypes[node.Id] = type;
    }
  }

  public void SetShorthandValueSymbol(StructFieldInitSyntax node, Symbol symbol) {
    if ((uint) node.Id < (uint) ShorthandValueSymbols.Length) {
      ShorthandValueSymbols[node.Id] = symbol;
    }
  }

  public void SetMutationPlace(SyntaxNode node, MutationPlaceInfo info) {
    if ((uint) node.Id < (uint) MutationPlaces.Length) {
      MutationPlaces[node.Id] = info;
    }
  }

  public void SetRuntimeDisjointnessChecks(CallExpressionSyntax call, ImmutableArray<RuntimeDisjointnessCheck> checks) {
    if ((uint) call.Id < (uint) RuntimeDisjointnessChecks.Length) {
      RuntimeDisjointnessChecks[call.Id] = checks;
    }
  }

  public SemanticModel BuildSemanticModel(FrozenDictionary<Symbol, ModuleStateEffects>? moduleStateEffects = null) {
    foreach (Scope scope in AllScopes) {
      scope.Freeze();
    }
    ModuleScope.Freeze();

    return new SemanticModel(
        Tree,
        Module,
        IsProgramMode,
        Diagnostics.ToImmutableSorted(),
        AssignedSymbols.Count == 0 ? AssignedBindings.Empty : new AssignedBindings(AssignedSymbols),
        NodeTypes,
        ReferencedSymbols,
        DeclaredSymbols,
        Conversions,
        CallResolutions,
        ScopesByBlockId,
        ConstantValues,
        CapturesByCallableId,
        LoopInfos,
        PatternBindings,
        ParentsByNodeId,
        ShorthandValueSymbols,
        MutationPlaces,
        RuntimeDisjointnessChecks,
        moduleStateEffects);
  }
}

internal sealed partial class CheckerSession {
  private readonly IModuleSource _moduleSource;
  private readonly CancellationToken _cancellationToken;
  private readonly Dictionary<string, ModuleCheckState> _modulesByPath = new(StringComparer.Ordinal);
  private readonly HashSet<string> _activeModulePaths = new(StringComparer.Ordinal);
  private readonly List<ModuleCheckState> _orderedModules = [];
  private readonly HashSet<InstantiationKey> _instantiationKeys = [];
  private readonly List<GenericInstantiation> _genericInstantiations = [];

  private readonly struct InstantiationKey(
      Symbol definition,
      ImmutableArray<TypeSymbol> typeArguments) : IEquatable<InstantiationKey> {
    public Symbol Definition { get; } = definition;
    public ImmutableArray<TypeSymbol> TypeArguments { get; } = typeArguments;

    public bool Equals(InstantiationKey other) {
      if (!ReferenceEquals(Definition, other.Definition)
          || TypeArguments.Length != other.TypeArguments.Length) {
        return false;
      }

      for (int i = 0; i < TypeArguments.Length; i++) {
        if (!ReferenceEquals(TypeArguments[i], other.TypeArguments[i])) {
          return false;
        }
      }

      return true;
    }

    public override bool Equals(object? obj) => obj is InstantiationKey other && Equals(other);

    public override int GetHashCode() {
      var hc = new HashCode();
      hc.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Definition));
      hc.Add(TypeArguments.Length);
      foreach (TypeSymbol arg in TypeArguments) {
        hc.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(arg));
      }
      return hc.ToHashCode();
    }
  }

  public void RecordGenericInstantiation(Symbol definition, ImmutableArray<TypeSymbol> typeArguments) {
    if (typeArguments.IsDefaultOrEmpty) {
      return;
    }

    foreach (TypeSymbol arg in typeArguments) {
      if (!Unifier.IsConcreteType(arg)) {
        return;
      }
    }

    var key = new InstantiationKey(definition, typeArguments);
    if (_instantiationKeys.Add(key)) {
      _genericInstantiations.Add(new GenericInstantiation(definition, typeArguments));
    }
  }

  public void RecordInstantiationsInType(TypeSymbol type) {
    switch (type) {
      case VectorType v:
        RecordInstantiationsInType(v.ElementType);
        break;

      case MapType m:
        RecordInstantiationsInType(m.KeyType);
        RecordInstantiationsInType(m.ValueType);
        break;

      case SetType s:
        RecordInstantiationsInType(s.ElementType);
        break;

      case PoolType p:
        RecordInstantiationsInType(p.ElementType);
        break;

      case HandleType h:
        RecordInstantiationsInType(h.ElementType);
        break;

      case TupleType t:
        foreach (TupleField f in t.Fields) {
          RecordInstantiationsInType(f.Type);
        }
        break;

      case FunctionType fn:
        foreach (FunctionParameter p in fn.Parameters) {
          RecordInstantiationsInType(p.Type);
        }
        RecordInstantiationsInType(fn.ReturnType);
        break;

      case StructType st when !st.TypeArguments.IsEmpty:
        RecordGenericInstantiation(st.Definition, st.TypeArguments);
        foreach (TypeSymbol a in st.TypeArguments) {
          RecordInstantiationsInType(a);
        }
        break;

      case UnionType ut when !ut.TypeArguments.IsEmpty:
        RecordGenericInstantiation(ut.Definition, ut.TypeArguments);
        foreach (TypeSymbol a in ut.TypeArguments) {
          RecordInstantiationsInType(a);
        }
        break;

      case InterfaceType it when !it.TypeArguments.IsEmpty:
        RecordGenericInstantiation(it.Definition, it.TypeArguments);
        foreach (TypeSymbol a in it.TypeArguments) {
          RecordInstantiationsInType(a);
        }
        break;
    }
  }

  public Dictionary<(TypeSymbol Candidate, InterfaceType Target, ModuleSymbol? Module), (bool Satisfied, string? FailureDetail)> InterfaceSatisfactionCache { get; } = [];

  public CheckerSession(IModuleSource moduleSource, CancellationToken cancellationToken) {
    _moduleSource = moduleSource;
    _cancellationToken = cancellationToken;
    Prelude = PreludeSymbols.Instance;
    Types = new TypeInterner();
  }

  public PreludeSymbols Prelude { get; }

  public TypeInterner Types { get; }

  public Compilation BuildCompilation(ParseResult rootTree) {
    string? filePath = rootTree.Source.FilePath;
    string defaultName = string.IsNullOrEmpty(filePath)
        ? "<main>"
        : Path.GetFileNameWithoutExtension(filePath);
    string rootKey = string.IsNullOrEmpty(filePath) ? defaultName : filePath;

    ModuleCheckState rootState = CheckModule(rootTree, rootKey, defaultName, isRoot: true);

    ImmutableArray<GenericInstantiation> genericInstantiations = [.. _genericInstantiations];
    TypeReachabilityGraph reachability = ReachabilityBuilder.Build(_orderedModules, Prelude, Types, genericInstantiations);
    FrozenDictionary<Symbol, ModuleStateEffects> moduleStateEffects = ModuleStateEffectAnalyzer.Build(_orderedModules);

    var modulesBuilder = new Dictionary<string, ModuleSymbol>(_orderedModules.Count, StringComparer.Ordinal);
    var modelsByTreeBuilder = new Dictionary<ParseResult, SemanticModel>(_orderedModules.Count, ReferenceEqualityComparer.Instance);
    var allDiagnostics = ImmutableArray.CreateBuilder<Diagnostic>();

    SemanticModel? rootModel = null;
    foreach (ModuleCheckState state in _orderedModules) {
      SemanticModel model = state.BuildSemanticModel(moduleStateEffects);
      if (ReferenceEquals(state, rootState)) {
        rootModel = model;
      }

      string key = state.IsRoot ? rootKey : (state.FilePath ?? state.Module.Name);
      modulesBuilder[key] = state.Module;
      modelsByTreeBuilder[state.Tree] = model;
      allDiagnostics.AddRange(model.Diagnostics);
    }

    return Compilation.CreateFromSession(
        rootTree,
        rootModel!,
        modulesBuilder.ToFrozenDictionary(StringComparer.Ordinal),
        modelsByTreeBuilder.ToFrozenDictionary(ReferenceEqualityComparer.Instance),
        Prelude,
        Types,
        reachability,
        allDiagnostics.ToImmutable(),
        genericInstantiations,
        moduleStateEffects);
  }

  private ModuleCheckState CheckModule(
      ParseResult tree,
      string canonicalKey,
      string fallbackName,
      bool isRoot) {
    _cancellationToken.ThrowIfCancellationRequested();

    if (_modulesByPath.TryGetValue(canonicalKey, out ModuleCheckState? cached)) {
      return cached;
    }

    int nodeCount = Math.Max(0, tree.NodeCount);
    SyntaxNode?[] parentsByNodeId = Compilation.BuildParentMap(tree.Root, nodeCount, _cancellationToken);
    string moduleName = ResolveModuleName(tree, fallbackName);

    var moduleSymbol = new ModuleSymbol(
        moduleName,
        tree.Root.Id,
        tree.Root.Span,
        tree.Source.FilePath);

    var moduleScope = new Scope(ScopeKind.Module, Prelude.Scope, moduleSymbol);
    bool isTestFile = _moduleSource.IsTestFile(tree.Source.FilePath);
    var state = new ModuleCheckState(tree, moduleSymbol, isRoot, isTestFile, moduleScope, parentsByNodeId);

    _modulesByPath.Add(canonicalKey, state);
    _orderedModules.Add(state);
    _activeModulePaths.Add(canonicalKey);

    try {
      RunPhase1(state);
      RunPhase2(state);
      new BodyChecker(this, state, _cancellationToken).Run();
    } finally {
      _activeModulePaths.Remove(canonicalKey);
    }

    return state;
  }

  private ModuleSymbol? ResolveAndCheckImport(
      ModuleCheckState importingState,
      string moduleName,
      SourceSpan importSpan) {
    _cancellationToken.ThrowIfCancellationRequested();

    if (!_moduleSource.TryResolveModule(
        importingState.FilePath,
        moduleName,
        out ParseResult? targetTree,
        out string? expectedPath) || targetTree is null) {
      string hint = string.IsNullOrEmpty(expectedPath)
          ? string.Empty
          : $" (expected at '{expectedPath}')";
      importingState.Diagnostics.ReportError(
          DiagnosticCodes.ModuleNotFound,
          importSpan,
          $"Module '{moduleName}' could not be found{hint}.");
      return null;
    }

    string targetKey = expectedPath ?? targetTree.Source.FilePath ?? moduleName;
    if (_activeModulePaths.Contains(targetKey)) {
      importingState.Diagnostics.ReportError(
          DiagnosticCodes.ModuleImportCycle,
          importSpan,
          $"Circular module import detected involving module '{moduleName}'.");
      return _modulesByPath.TryGetValue(targetKey, out ModuleCheckState? activeState)
          ? activeState.Module
          : null;
    }

    if (_modulesByPath.TryGetValue(targetKey, out ModuleCheckState? existingState)) {
      return existingState.Module;
    }

    ModuleCheckState targetState = CheckModule(targetTree, targetKey, moduleName, isRoot: false);
    return targetState.Module;
  }

  private static string ResolveModuleName(ParseResult tree, string fallbackName) {
    foreach (SyntaxNode item in tree.Root.Items) {
      if (item is ModuleDeclarationSyntax modDecl && !IsMissingToken(modDecl.NameToken)) {
        return tree.Lex.GetText(modDecl.NameToken).ToString();
      }
    }

    return fallbackName;
  }

  internal static bool IsMissingToken(Token token) => (token.Flags & TokenFlags.Missing) != 0;

  private static SymbolVisibility GetVisibility(VisibilitySyntax? visibility) {
    if (visibility is null) {
      return SymbolVisibility.Private;
    }

    return visibility.IsPub ? SymbolVisibility.Public : SymbolVisibility.Testable;
  }

  private static bool IsAsciiCapitalized(string name) =>
      name.Length > 0 && name[0] is >= 'A' and <= 'Z';

  internal static void ReportDuplicate(
      ModuleCheckState state,
      string name,
      SourceSpan newSpan,
      Symbol existing) {
    ImmutableArray<RelatedLocation> related = existing.NameSpan.Length > 0
        ? [new RelatedLocation(existing.NameSpan, $"'{name}' was previously declared here", existing.FilePath)]
        : [];
    state.Diagnostics.ReportError(
        DiagnosticCodes.DuplicateDeclaration,
        newSpan,
        $"Identifier '{name}' is already declared in this scope.",
        related);
  }

  private static void ReportCapitalizationError(
      ModuleCheckState state,
      string name,
      SourceSpan span) {
    state.Diagnostics.ReportError(
        DiagnosticCodes.InvalidTypeNameCasing,
        span,
        $"Name '{name}' must be capitalized (begin with an ASCII uppercase letter 'A'-'Z').");
  }
}
