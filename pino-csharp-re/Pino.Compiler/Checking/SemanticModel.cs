using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

/// <summary>
/// Immutable per-file semantic query surface backed by dense arrays indexed by
/// <see cref="SyntaxNode.Id"/>.
/// </summary>
public sealed class SemanticModel {
  private readonly TypeSymbol?[] _nodeTypes;
  private readonly Symbol?[] _referencedSymbols;
  private readonly Symbol?[] _declaredSymbols;
  private readonly ConversionInfo?[] _conversions;
  private readonly CallResolution?[] _callResolutions;
  private readonly Scope?[] _scopesByBlockId;
  private readonly ConstantValue?[] _constantValues;
  private readonly CaptureList?[] _capturesByCallableId;
  private readonly LoopInfo?[] _loopInfos;
  private readonly PatternBindingInfo?[] _patternBindings;
  private readonly SyntaxNode?[] _parentsByNodeId;

  internal SemanticModel(
      ParseResult tree,
      ModuleSymbol module,
      bool isProgramMode,
      ImmutableArray<Diagnostic> diagnostics,
      AssignedBindings assignedBindings,
      TypeSymbol?[] nodeTypes,
      Symbol?[] referencedSymbols,
      Symbol?[] declaredSymbols,
      ConversionInfo?[] conversions,
      CallResolution?[] callResolutions,
      Scope?[] scopesByBlockId,
      ConstantValue?[] constantValues,
      CaptureList?[] capturesByCallableId,
      LoopInfo?[] loopInfos,
      PatternBindingInfo?[] patternBindings,
      SyntaxNode?[] parentsByNodeId) {
    Tree = tree;
    Module = module;
    IsProgramMode = isProgramMode;
    Diagnostics = diagnostics;
    AssignedBindings = assignedBindings;
    _nodeTypes = nodeTypes;
    _referencedSymbols = referencedSymbols;
    _declaredSymbols = declaredSymbols;
    _conversions = conversions;
    _callResolutions = callResolutions;
    _scopesByBlockId = scopesByBlockId;
    _constantValues = constantValues;
    _capturesByCallableId = capturesByCallableId;
    _loopInfos = loopInfos;
    _patternBindings = patternBindings;
    _parentsByNodeId = parentsByNodeId;
  }

  public ParseResult Tree { get; }

  public ModuleSymbol Module { get; }

  public bool IsProgramMode { get; }

  public ImmutableArray<Diagnostic> Diagnostics { get; }

  public AssignedBindings AssignedBindings { get; }

  public TypeSymbol? GetType(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_nodeTypes, node.Id);
  }

  public Symbol? GetReferencedSymbol(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_referencedSymbols, node.Id);
  }

  public Symbol? GetDeclaredSymbol(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_declaredSymbols, node.Id);
  }

  public ConversionInfo? GetConversion(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_conversions, node.Id);
  }

  public CallResolution? GetCallResolution(CallExpressionSyntax node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_callResolutions, node.Id);
  }

  public CaptureList? GetCaptures(SyntaxNode callableNode) {
    ArgumentNullException.ThrowIfNull(callableNode);
    return Lookup(_capturesByCallableId, callableNode.Id);
  }

  public Scope? GetScope(SyntaxNode blockOrUnitNode) {
    ArgumentNullException.ThrowIfNull(blockOrUnitNode);
    return Lookup(_scopesByBlockId, blockOrUnitNode.Id);
  }

  public SyntaxNode? GetParent(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_parentsByNodeId, node.Id);
  }

  public ConstantValue? GetConstantValue(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_constantValues, node.Id);
  }

  public LoopInfo? GetLoopInfo(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_loopInfos, node.Id);
  }

  public PatternBindingInfo? GetPatternBindings(SyntaxNode node) {
    ArgumentNullException.ThrowIfNull(node);
    return Lookup(_patternBindings, node.Id);
  }

  public bool IsAssignedAfterInit(Symbol symbol) => AssignedBindings.IsAssignedAfterInit(symbol);

  private static T? Lookup<T>(T?[] table, int id) where T : class =>
      (uint) id < (uint) table.Length ? table[id] : null;
}
