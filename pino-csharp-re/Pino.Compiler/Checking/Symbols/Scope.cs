using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Pino.Compiler.Checking;

public enum ScopeKind : byte {
  Prelude,
  Module,
  Function,
  Method,
  StructMember,
  Block,
  Lambda,
  Loop,
  MatchArm,
  Recovery,
  Synthetic,
}

/// <summary>
/// Lexical scope chain storing declarations, hoisted symbols, and parent linkage
/// without mutating <see cref="Syntax.SyntaxNode"/>s.
/// </summary>
public sealed class Scope {
  private readonly Dictionary<string, Symbol> _symbols = new(StringComparer.Ordinal);
  private readonly List<Symbol> _hoistedDeclarations = [];
  private ImmutableArray<Symbol>? _frozenHoisted;

  public Scope(ScopeKind kind, Scope? parent = null, Symbol? owner = null) {
    Kind = kind;
    Parent = parent;
    Owner = owner;
  }

  public ScopeKind Kind { get; }

  public Scope? Parent { get; private set; }

  public Symbol? Owner { get; private set; }

  internal void Reparent(Scope? newParent, Symbol? newOwner = null) {
    Parent = newParent;
    if (newOwner is not null) {
      Owner = newOwner;
    }
  }

  public ImmutableArray<Symbol> HoistedDeclarations =>
      _frozenHoisted ?? [.. _hoistedDeclarations];

  public IReadOnlyDictionary<string, Symbol> DeclaredSymbols => _symbols;

  /// <summary>
  /// Attempts to declare <paramref name="symbol"/> in this scope only. If an identifier with
  /// the same name already exists in this scope, keeps the first declaration and returns
  /// <see langword="false"/> with <paramref name="existing"/> set.
  /// </summary>
  public bool TryDeclare(Symbol symbol, out Symbol? existing) {
    ArgumentNullException.ThrowIfNull(symbol);
    if (_symbols.TryGetValue(symbol.Name, out existing)) {
      return false;
    }

    _symbols.Add(symbol.Name, symbol);
    existing = null;
    return true;
  }

  /// <summary>
  /// Attempts to declare a hoisted symbol in this scope, recording it in
  /// <see cref="HoistedDeclarations"/> on success.
  /// </summary>
  public bool TryDeclareHoisted(Symbol symbol, out Symbol? existing) {
    if (!TryDeclare(symbol, out existing)) {
      return false;
    }

    _hoistedDeclarations.Add(symbol);
    _frozenHoisted = null;
    return true;
  }

  public bool TryLookupLocal(string name, [NotNullWhen(true)] out Symbol? symbol) {
    ArgumentNullException.ThrowIfNull(name);
    return _symbols.TryGetValue(name, out symbol);
  }

  public bool TryLookup(string name, [NotNullWhen(true)] out Symbol? symbol) =>
      TryLookupWithScope(name, out symbol, out _);

  internal bool TryLookupWithScope(
      string name,
      [NotNullWhen(true)] out Symbol? symbol,
      [NotNullWhen(true)] out Scope? declaringScope) {
    ArgumentNullException.ThrowIfNull(name);
    for (Scope? current = this; current is not null; current = current.Parent) {
      if (current._symbols.TryGetValue(name, out symbol)) {
        declaringScope = current;
        return true;
      }
    }

    symbol = null;
    declaringScope = null;
    return false;
  }

  /// <summary>
  /// Removes all non-hoisted bindings declared in this scope while preserving Phase-1 hoisted
  /// declarations, so Phase 3a fixed-point rounds and Phase 3b can re-check a callable body cleanly.
  /// </summary>
  internal void ResetNonHoisted() {
    if (_hoistedDeclarations.Count == 0) {
      _symbols.Clear();
      return;
    }

    _symbols.Clear();
    foreach (Symbol hoisted in _hoistedDeclarations) {
      _symbols[hoisted.Name] = hoisted;
    }
  }

  internal void Freeze() {
    _frozenHoisted ??= [.. _hoistedDeclarations];
  }
}
