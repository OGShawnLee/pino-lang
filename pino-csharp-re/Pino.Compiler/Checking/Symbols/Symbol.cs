using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

public enum SymbolVisibility : byte {
  Private,
  Public,
  Testable,
}

public enum LocalKind : byte {
  Variable,
  LoopVariable,
  PatternBinding,
  RecoveryErr,
  ImplicitIt,
}

public enum ReturnInferenceState : byte {
  Explicit,
  Unchecked,
  InProgress,
  Resolved,
}

public abstract class Symbol {
  private protected Symbol(
      string name,
      SymbolVisibility visibility,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null) {
    Name = name;
    Visibility = visibility;
    DeclaringNodeId = declaringNodeId;
    NameSpan = nameSpan;
    FilePath = filePath;
    ContainingSymbol = containingSymbol;
  }

  public string Name { get; }

  public SymbolVisibility Visibility { get; }

  public int DeclaringNodeId { get; }

  public SourceSpan NameSpan { get; }

  public string? FilePath { get; }

  public Symbol? ContainingSymbol { get; }

  public override string ToString() => Name;
}

public sealed class ModuleSymbol : Symbol {
  private FrozenDictionary<string, Symbol> _exports = FrozenDictionary<string, Symbol>.Empty;
  private ImmutableArray<Symbol> _declarations = [];

  public ModuleSymbol(
      string name,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      FrozenDictionary<string, Symbol>? exports = null,
      ImmutableArray<Symbol> declarations = default)
      : base(name, SymbolVisibility.Public, declaringNodeId, nameSpan, filePath) {
    if (exports is not null) {
      _exports = exports;
    }

    if (!declarations.IsDefault) {
      _declarations = declarations;
    }
  }

  public FrozenDictionary<string, Symbol> Exports => _exports;

  public ImmutableArray<Symbol> Declarations => _declarations;

  public bool TryGetExport(string name, [NotNullWhen(true)] out Symbol? symbol) =>
      _exports.TryGetValue(name, out symbol);

  internal void SetMembers(FrozenDictionary<string, Symbol> exports, ImmutableArray<Symbol> declarations) {
    _exports = exports;
    _declarations = declarations;
  }
}

public sealed class LocalSymbol : Symbol {
  public LocalSymbol(
      string name,
      TypeSymbol type,
      bool isMutable,
      LocalKind kind,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null,
      SymbolVisibility visibility = SymbolVisibility.Private)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingSymbol) {
    Type = type;
    IsMutable = isMutable;
    Kind = kind;
  }

  public TypeSymbol Type { get; }

  public bool IsMutable { get; }

  public LocalKind Kind { get; }

  internal ConstantValue? ConstantValue { get; set; }
}

public sealed class ParameterSymbol : Symbol {
  public ParameterSymbol(
      string name,
      TypeSymbol type,
      int ordinal,
      bool hasDefault,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null)
      : base(name, SymbolVisibility.Private, declaringNodeId, nameSpan, filePath, containingSymbol) {
    Type = type;
    Ordinal = ordinal;
    HasDefault = hasDefault;
  }

  public TypeSymbol Type { get; internal set; }

  public int Ordinal { get; }

  public bool HasDefault { get; }
}

public sealed class FieldSymbol : Symbol {
  public FieldSymbol(
      string name,
      TypeSymbol type,
      SymbolVisibility visibility,
      int ordinal,
      StructSymbol containingStruct,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      ImmutableArray<FieldSymbol> embeddedChain = default)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingStruct) {
    Type = type;
    Ordinal = ordinal;
    ContainingStruct = containingStruct;
    EmbeddedChain = embeddedChain.IsDefault ? [] : embeddedChain;
  }

  public TypeSymbol Type { get; internal set; }

  public int Ordinal { get; }

  public StructSymbol ContainingStruct { get; }

  public ImmutableArray<FieldSymbol> EmbeddedChain { get; }

  public bool IsPromotedFromEmbedded => !EmbeddedChain.IsEmpty;
}

public sealed class FunctionSymbol : Symbol {
  public FunctionSymbol(
      string name,
      SymbolVisibility visibility,
      ImmutableArray<TypeParameterSymbol> typeParameters,
      ImmutableArray<ParameterSymbol> parameters,
      TypeSymbol returnType,
      ReturnInferenceState returnState,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null,
      bool isPrelude = false,
      IntrinsicId? intrinsic = null)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingSymbol) {
    TypeParameters = typeParameters.IsDefault ? [] : typeParameters;
    Parameters = parameters.IsDefault ? [] : parameters;
    ReturnType = returnType;
    ReturnState = returnState;
    IsPrelude = isPrelude;
    Intrinsic = intrinsic;
  }

  public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }

  public ImmutableArray<ParameterSymbol> Parameters { get; internal set; }

  public TypeSymbol ReturnType { get; internal set; }

  public ReturnInferenceState ReturnState { get; internal set; }

  public bool IsPrelude { get; }

  public IntrinsicId? Intrinsic { get; }
}

public sealed class MethodSymbol : Symbol {
  public MethodSymbol(
      string name,
      SymbolVisibility visibility,
      bool isStatic,
      Symbol containingType,
      ImmutableArray<TypeParameterSymbol> typeParameters,
      ImmutableArray<ParameterSymbol> parameters,
      TypeSymbol returnType,
      ReturnInferenceState returnState,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      IntrinsicId? intrinsic = null,
      ImmutableArray<FieldSymbol> embeddedChain = default,
      MethodSymbol? promotedFrom = null)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingType) {
    IsStatic = isStatic;
    ContainingType = containingType;
    TypeParameters = typeParameters.IsDefault ? [] : typeParameters;
    Parameters = parameters.IsDefault ? [] : parameters;
    ReturnType = returnType;
    ReturnState = returnState;
    Intrinsic = intrinsic;
    EmbeddedChain = embeddedChain.IsDefault ? [] : embeddedChain;
    PromotedFrom = promotedFrom;
  }

  public bool IsStatic { get; }

  public Symbol ContainingType { get; }

  public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }

  public ImmutableArray<ParameterSymbol> Parameters { get; internal set; }

  public TypeSymbol ReturnType { get; internal set; }

  public ReturnInferenceState ReturnState { get; internal set; }

  public IntrinsicId? Intrinsic { get; }

  public ImmutableArray<FieldSymbol> EmbeddedChain { get; }

  public bool IsPromotedFromEmbedded => !EmbeddedChain.IsEmpty;

  internal MethodSymbol? PromotedFrom { get; }
}

public sealed class StructSymbol : Symbol {
  public StructSymbol(
      string name,
      SymbolVisibility visibility,
      ImmutableArray<TypeParameterSymbol> typeParameters,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null,
      ImmutableArray<FieldSymbol> fields = default,
      ImmutableArray<StructSymbol> embeddedStructs = default,
      ImmutableArray<MethodSymbol> methods = default)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingSymbol) {
    TypeParameters = typeParameters.IsDefault ? [] : typeParameters;
    Fields = fields.IsDefault ? [] : fields;
    AllFields = Fields;
    EmbeddedStructs = embeddedStructs.IsDefault ? [] : embeddedStructs;
    Methods = methods.IsDefault ? [] : methods;
  }

  public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }

  public ImmutableArray<FieldSymbol> Fields { get; internal set; }

  public ImmutableArray<FieldSymbol> AllFields { get; internal set; }

  public ImmutableArray<StructSymbol> EmbeddedStructs { get; internal set; }

  public ImmutableArray<MethodSymbol> Methods { get; internal set; }

  public Scope? MemberScope { get; internal set; }
}

public sealed class EnumMemberSymbol : Symbol {
  public EnumMemberSymbol(
      string name,
      int ordinal,
      EnumSymbol containingEnum,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath)
      : base(name, SymbolVisibility.Public, declaringNodeId, nameSpan, filePath, containingEnum) {
    Ordinal = ordinal;
    ContainingEnum = containingEnum;
  }

  public int Ordinal { get; }

  public EnumSymbol ContainingEnum { get; }
}

public sealed class EnumSymbol : Symbol {
  public EnumSymbol(
      string name,
      SymbolVisibility visibility,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null,
      ImmutableArray<EnumMemberSymbol> members = default)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingSymbol) =>
      Members = members.IsDefault ? [] : members;

  public ImmutableArray<EnumMemberSymbol> Members { get; internal set; }
}

public sealed record UnionVariantField(
    string? Name,
    TypeSymbol Type,
    int Ordinal,
    int DeclaringNodeId,
    SourceSpan NameSpan);

public sealed class UnionVariantSymbol : Symbol {
  public UnionVariantSymbol(
      string name,
      int tagOrdinal,
      UnionSymbol containingUnion,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      ImmutableArray<UnionVariantField> fields = default)
      : base(name, SymbolVisibility.Public, declaringNodeId, nameSpan, filePath, containingUnion) {
    TagOrdinal = tagOrdinal;
    ContainingUnion = containingUnion;
    Fields = fields.IsDefault ? [] : fields;
  }

  public int TagOrdinal { get; }

  public ImmutableArray<UnionVariantField> Fields { get; internal set; }

  public UnionSymbol ContainingUnion { get; }
}

public sealed class UnionSymbol : Symbol {
  public UnionSymbol(
      string name,
      SymbolVisibility visibility,
      ImmutableArray<TypeParameterSymbol> typeParameters,
      bool usesNamedFields,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null,
      bool isPrelude = false,
      ImmutableArray<UnionVariantSymbol> variants = default)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingSymbol) {
    TypeParameters = typeParameters.IsDefault ? [] : typeParameters;
    UsesNamedFields = usesNamedFields;
    IsPrelude = isPrelude;
    Variants = variants.IsDefault ? [] : variants;
  }

  public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }

  public bool UsesNamedFields { get; internal set; }

  public bool IsPrelude { get; }

  public ImmutableArray<UnionVariantSymbol> Variants { get; internal set; }
}

public sealed record InterfaceProperty(
    string Name,
    TypeSymbol Type,
    int DeclaringNodeId,
    SourceSpan NameSpan);

public sealed class InterfaceSymbol : Symbol {
  public InterfaceSymbol(
      string name,
      SymbolVisibility visibility,
      ImmutableArray<TypeParameterSymbol> typeParameters,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? containingSymbol = null,
      ImmutableArray<InterfaceProperty> properties = default,
      ImmutableArray<MethodSymbol> methods = default)
      : base(name, visibility, declaringNodeId, nameSpan, filePath, containingSymbol) {
    TypeParameters = typeParameters.IsDefault ? [] : typeParameters;
    Properties = properties.IsDefault ? [] : properties;
    Methods = methods.IsDefault ? [] : methods;
  }

  public ImmutableArray<TypeParameterSymbol> TypeParameters { get; }

  public ImmutableArray<InterfaceProperty> Properties { get; internal set; }

  public ImmutableArray<MethodSymbol> Methods { get; internal set; }
}

public sealed class TypeParameterSymbol : Symbol {
  public TypeParameterSymbol(
      string name,
      int ordinal,
      int declaringNodeId,
      SourceSpan nameSpan,
      string? filePath,
      Symbol? owner = null,
      InterfaceType? constraint = null)
      : base(name, SymbolVisibility.Private, declaringNodeId, nameSpan, filePath, owner) {
    Ordinal = ordinal;
    Owner = owner;
    Constraint = constraint;
    Type = new TypeParameterType(this);
  }

  public int Ordinal { get; }

  public InterfaceType? Constraint { get; internal set; }

  public Symbol? Owner { get; internal set; }

  public TypeParameterType Type { get; }
}

public sealed class ErrorSymbol : Symbol {
  public static ErrorSymbol Instance { get; } = new("<error>");

  public ErrorSymbol(
      string name,
      int declaringNodeId = -1,
      SourceSpan nameSpan = default,
      string? filePath = null)
      : base(name, SymbolVisibility.Private, declaringNodeId, nameSpan, filePath) { }
}
