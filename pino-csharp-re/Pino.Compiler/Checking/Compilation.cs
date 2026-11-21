using System.Collections.Frozen;
using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

/// <summary>
/// Immutable, reentrant compilation unit holding interned types, prelude handles,
/// resolved modules, AD-002 memory facts, and per-tree <see cref="SemanticModel"/>s.
/// </summary>
public sealed class Compilation {
  private readonly FrozenDictionary<ParseResult, SemanticModel> _modelsByTree;

  private Compilation(
      ParseResult rootTree,
      SemanticModel rootModel,
      FrozenDictionary<string, ModuleSymbol> modules,
      FrozenDictionary<ParseResult, SemanticModel> modelsByTree,
      PreludeSymbols prelude,
      TypeInterner types,
      TypeReachabilityGraph reachability,
      ImmutableArray<Diagnostic> diagnostics,
      ImmutableArray<GenericInstantiation> genericInstantiations) {
    RootTree = rootTree;
    RootModel = rootModel;
    Modules = modules;
    _modelsByTree = modelsByTree;
    Prelude = prelude;
    Types = types;
    Reachability = reachability;
    Diagnostics = diagnostics;
    GenericInstantiations = genericInstantiations;
  }

  public ParseResult RootTree { get; }

  public SemanticModel RootModel { get; }

  public FrozenDictionary<string, ModuleSymbol> Modules { get; }

  public PreludeSymbols Prelude { get; }

  public TypeInterner Types { get; }

  public TypeReachabilityGraph Reachability { get; }

  public ImmutableArray<Diagnostic> Diagnostics { get; }

  public ImmutableArray<GenericInstantiation> GenericInstantiations { get; }

  public static Compilation Create(
      ParseResult rootTree,
      IModuleSource? moduleSource = null,
      CancellationToken cancellationToken = default) {
    ArgumentNullException.ThrowIfNull(rootTree);
    cancellationToken.ThrowIfCancellationRequested();

    _ = moduleSource ?? NullModuleSource.Instance;
    PreludeSymbols prelude = PreludeSymbols.Instance;
    var types = new TypeInterner();

    int nodeCount = Math.Max(0, rootTree.NodeCount);
    SyntaxNode?[] parentsByNodeId = BuildParentMap(rootTree.Root, nodeCount, cancellationToken);

    string? filePath = rootTree.Source.FilePath;
    string moduleName = string.IsNullOrEmpty(filePath)
        ? "<main>"
        : Path.GetFileNameWithoutExtension(filePath);
    var rootModule = new ModuleSymbol(
        moduleName,
        rootTree.Root.Id,
        rootTree.Root.Span,
        filePath);

    var scopesByBlockId = new Scope?[nodeCount];
    var rootScope = new Scope(ScopeKind.Module, prelude.Scope, rootModule);
    if ((uint) rootTree.Root.Id < (uint) scopesByBlockId.Length) {
      scopesByBlockId[rootTree.Root.Id] = rootScope;
    }

    var rootModel = new SemanticModel(
        rootTree,
        rootModule,
        isProgramMode: false,
        rootTree.Diagnostics,
        AssignedBindings.Empty,
        new TypeSymbol?[nodeCount],
        new Symbol?[nodeCount],
        new Symbol?[nodeCount],
        new ConversionInfo?[nodeCount],
        new CallResolution?[nodeCount],
        scopesByBlockId,
        new ConstantValue?[nodeCount],
        new CaptureList?[nodeCount],
        new LoopInfo?[nodeCount],
        new PatternBindingInfo?[nodeCount],
        parentsByNodeId);

    string moduleKey = string.IsNullOrEmpty(filePath) ? moduleName : filePath;
    var modules = new Dictionary<string, ModuleSymbol>(StringComparer.Ordinal) {
      [moduleKey] = rootModule,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    var modelsByTree = new Dictionary<ParseResult, SemanticModel>(ReferenceEqualityComparer.Instance) {
      [rootTree] = rootModel,
    }.ToFrozenDictionary(ReferenceEqualityComparer.Instance);

    return new Compilation(
        rootTree,
        rootModel,
        modules,
        modelsByTree,
        prelude,
        types,
        TypeReachabilityGraph.Empty,
        rootTree.Diagnostics,
        []);
  }

  public SemanticModel? GetSemanticModel(ParseResult tree) {
    ArgumentNullException.ThrowIfNull(tree);
    return _modelsByTree.TryGetValue(tree, out SemanticModel? model) ? model : null;
  }

  internal static SyntaxNode?[] BuildParentMap(
      CompilationUnitSyntax root,
      int nodeCount,
      CancellationToken cancellationToken) {
    var parents = new SyntaxNode?[nodeCount];
    var stack = new Stack<SyntaxNode>();
    stack.Push(root);

    while (stack.Count > 0) {
      cancellationToken.ThrowIfCancellationRequested();
      SyntaxNode current = stack.Pop();
      foreach (SyntaxElement element in current.GetChildren()) {
        if (element.IsNode) {
          SyntaxNode child = element.AsNode();
          if ((uint) child.Id < (uint) parents.Length) {
            parents[child.Id] = current;
          }
          stack.Push(child);
        }
      }
    }

    return parents;
  }
}
