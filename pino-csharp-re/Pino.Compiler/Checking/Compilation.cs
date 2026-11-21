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
  private readonly FrozenDictionary<Symbol, ModuleStateEffects> _moduleStateEffects;

  private Compilation(
      ParseResult rootTree,
      SemanticModel rootModel,
      FrozenDictionary<string, ModuleSymbol> modules,
      FrozenDictionary<ParseResult, SemanticModel> modelsByTree,
      PreludeSymbols prelude,
      TypeInterner types,
      TypeReachabilityGraph reachability,
      ImmutableArray<Diagnostic> diagnostics,
      ImmutableArray<GenericInstantiation> genericInstantiations,
      FrozenDictionary<Symbol, ModuleStateEffects> moduleStateEffects) {
    RootTree = rootTree;
    RootModel = rootModel;
    Modules = modules;
    _modelsByTree = modelsByTree;
    Prelude = prelude;
    Types = types;
    Reachability = reachability;
    Diagnostics = diagnostics;
    GenericInstantiations = genericInstantiations;
    _moduleStateEffects = moduleStateEffects;
  }

  public ParseResult RootTree { get; }

  public SemanticModel RootModel { get; }

  public FrozenDictionary<string, ModuleSymbol> Modules { get; }

  public PreludeSymbols Prelude { get; }

  public TypeInterner Types { get; }

  public TypeReachabilityGraph Reachability { get; }

  public ImmutableArray<Diagnostic> Diagnostics { get; }

  public ImmutableArray<GenericInstantiation> GenericInstantiations { get; }

  public IEnumerable<SemanticModel> SemanticModels => _modelsByTree.Values;

  public static Compilation Create(
      ParseResult rootTree,
      IModuleSource? moduleSource = null,
      CancellationToken cancellationToken = default) {
    ArgumentNullException.ThrowIfNull(rootTree);
    cancellationToken.ThrowIfCancellationRequested();

    IModuleSource effectiveSource = moduleSource ?? NullModuleSource.Instance;
    var session = new CheckerSession(effectiveSource, cancellationToken);
    return session.BuildCompilation(rootTree);
  }

  internal static Compilation CreateFromSession(
      ParseResult rootTree,
      SemanticModel rootModel,
      FrozenDictionary<string, ModuleSymbol> modules,
      FrozenDictionary<ParseResult, SemanticModel> modelsByTree,
      PreludeSymbols prelude,
      TypeInterner types,
      TypeReachabilityGraph reachability,
      ImmutableArray<Diagnostic> diagnostics,
      ImmutableArray<GenericInstantiation> genericInstantiations,
      FrozenDictionary<Symbol, ModuleStateEffects>? moduleStateEffects = null) =>
      new(
          rootTree,
          rootModel,
          modules,
          modelsByTree,
          prelude,
          types,
          reachability,
          diagnostics,
          genericInstantiations,
          moduleStateEffects ?? FrozenDictionary<Symbol, ModuleStateEffects>.Empty);

  public SemanticModel? GetSemanticModel(ParseResult tree) {
    ArgumentNullException.ThrowIfNull(tree);
    return _modelsByTree.TryGetValue(tree, out SemanticModel? model) ? model : null;
  }

  /// <summary>
  /// Returns the inferred module-state purity facts for <paramref name="function"/> across the
  /// compilation (D-183).
  /// </summary>
  public ModuleStateEffects GetModuleStateEffects(FunctionSymbol function) {
    ArgumentNullException.ThrowIfNull(function);
    return _moduleStateEffects.TryGetValue(function, out ModuleStateEffects effects)
        ? effects
        : ModuleStateEffects.Pure;
  }

  /// <summary>
  /// Returns the inferred module-state purity facts for <paramref name="method"/> across the
  /// compilation (D-183).
  /// </summary>
  public ModuleStateEffects GetModuleStateEffects(MethodSymbol method) {
    ArgumentNullException.ThrowIfNull(method);
    return _moduleStateEffects.TryGetValue(method, out ModuleStateEffects effects)
        ? effects
        : ModuleStateEffects.Pure;
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
