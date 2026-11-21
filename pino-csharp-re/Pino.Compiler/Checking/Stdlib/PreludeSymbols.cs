using System.Collections.Frozen;
using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Checking;

/// <summary>
/// Deeply immutable well-known prelude symbols parsed from <see cref="PreludeSource"/>
/// together with global intrinsic <see cref="FunctionSymbol"/>s in the prelude scope.
/// </summary>
public sealed class PreludeSymbols {
  private static readonly Lazy<PreludeSymbols> s_instance = new(Build);

  public static PreludeSymbols Instance => s_instance.Value;

  private PreludeSymbols(
      Scope scope,
      UnionSymbol optionUnion,
      UnionVariantSymbol optionSomeVariant,
      UnionVariantSymbol optionNoneVariant,
      UnionSymbol resultUnion,
      UnionVariantSymbol resultSuccessVariant,
      UnionVariantSymbol resultFailureVariant,
      UnionSymbol ioErrorUnion,
      UnionVariantSymbol ioErrorNotFoundVariant,
      UnionVariantSymbol ioErrorPermissionDeniedVariant,
      UnionVariantSymbol ioErrorAlreadyExistsVariant,
      UnionVariantSymbol ioErrorGremlinVariant,
      FrozenDictionary<string, FunctionSymbol> globalFunctions) {
    Scope = scope;
    OptionUnion = optionUnion;
    OptionSomeVariant = optionSomeVariant;
    OptionNoneVariant = optionNoneVariant;
    ResultUnion = resultUnion;
    ResultSuccessVariant = resultSuccessVariant;
    ResultFailureVariant = resultFailureVariant;
    IOErrorUnion = ioErrorUnion;
    IOErrorNotFoundVariant = ioErrorNotFoundVariant;
    IOErrorPermissionDeniedVariant = ioErrorPermissionDeniedVariant;
    IOErrorAlreadyExistsVariant = ioErrorAlreadyExistsVariant;
    IOErrorGremlinVariant = ioErrorGremlinVariant;
    GlobalFunctions = globalFunctions;
  }

  public Scope Scope { get; }

  public UnionSymbol OptionUnion { get; }

  public UnionVariantSymbol OptionSomeVariant { get; }

  public UnionVariantSymbol OptionNoneVariant { get; }

  public UnionSymbol ResultUnion { get; }

  public UnionVariantSymbol ResultSuccessVariant { get; }

  public UnionVariantSymbol ResultFailureVariant { get; }

  public UnionSymbol IOErrorUnion { get; }

  public UnionVariantSymbol IOErrorNotFoundVariant { get; }

  public UnionVariantSymbol IOErrorPermissionDeniedVariant { get; }

  public UnionVariantSymbol IOErrorAlreadyExistsVariant { get; }

  public UnionVariantSymbol IOErrorGremlinVariant { get; }

  public FrozenDictionary<string, FunctionSymbol> GlobalFunctions { get; }

  private static PreludeSymbols Build() {
    ParseResult parsed = PreludeSource.Parsed;
    if (!parsed.Diagnostics.IsEmpty) {
      throw new InvalidOperationException(
          $"Embedded prelude failed to parse cleanly: {parsed.Diagnostics[0].Message}");
    }

    var scope = new Scope(ScopeKind.Prelude);
    var unionsByName = new Dictionary<string, UnionSymbol>(StringComparer.Ordinal);

    foreach (SyntaxNode item in parsed.Root.Items) {
      if (item is UnionDeclarationSyntax unionSyntax) {
        UnionSymbol union = BuildUnionSymbol(parsed.Lex, unionSyntax);
        unionsByName.Add(union.Name, union);
        scope.TryDeclareHoisted(union, out _);
      }
    }

    UnionSymbol optionUnion = unionsByName["Option"];
    UnionSymbol resultUnion = unionsByName["Result"];
    UnionSymbol ioErrorUnion = unionsByName["IOError"];

    VirtualStdlibStub stub = VirtualStdlibStub.Instance;
    var globalFunctions = new Dictionary<string, FunctionSymbol>(StringComparer.Ordinal);

    foreach (IntrinsicDescriptor descriptor in StdlibCatalog.Globals.Values) {
      IntrinsicSignature primary = descriptor.Signatures[0];
      var parameters = ImmutableArray.CreateBuilder<ParameterSymbol>(primary.Parameters.Length);
      for (int i = 0; i < primary.Parameters.Length; i++) {
        IntrinsicParameter p = primary.Parameters[i];
        TypeSymbol paramType = ResolveStubParamType(p, optionUnion, resultUnion, ioErrorUnion);
        parameters.Add(new ParameterSymbol(
            p.Name,
            paramType,
            i,
            hasDefault: false,
            declaringNodeId: -1,
            nameSpan: default,
            filePath: VirtualStdlibStub.VirtualPath));
      }

      TypeSymbol returnType = ResolveStubReturnType(primary.ReturnType, optionUnion, resultUnion, ioErrorUnion);
      Text.SourceSpan nameSpan = stub.TryGetEntry(StdlibReceiverKind.Global, descriptor.Name, out VirtualStdlibEntry? entry)
          ? entry.NameSpan
          : default;

      var fnSymbol = new FunctionSymbol(
          descriptor.Name,
          SymbolVisibility.Public,
          typeParameters: [],
          parameters.MoveToImmutable(),
          returnType,
          ReturnInferenceState.Explicit,
          declaringNodeId: -1,
          nameSpan: nameSpan,
          filePath: VirtualStdlibStub.VirtualPath,
          containingSymbol: null,
          isPrelude: true,
          intrinsic: primary.Id);

      globalFunctions.Add(descriptor.Name, fnSymbol);
      scope.TryDeclareHoisted(fnSymbol, out _);
    }

    scope.Freeze();

    return new PreludeSymbols(
        scope,
        optionUnion,
        FindVariant(optionUnion, "Some"),
        FindVariant(optionUnion, "None"),
        resultUnion,
        FindVariant(resultUnion, "Success"),
        FindVariant(resultUnion, "Failure"),
        ioErrorUnion,
        FindVariant(ioErrorUnion, "NotFound"),
        FindVariant(ioErrorUnion, "PermissionDenied"),
        FindVariant(ioErrorUnion, "AlreadyExists"),
        FindVariant(ioErrorUnion, "Gremlin"),
        globalFunctions.ToFrozenDictionary(StringComparer.Ordinal));
  }

  private static UnionSymbol BuildUnionSymbol(LexResult lex, UnionDeclarationSyntax syntax) {
    string unionName = lex.GetText(syntax.NameToken).ToString();
    var typeParams = ImmutableArray.CreateBuilder<TypeParameterSymbol>();
    var typeParamMap = new Dictionary<string, TypeParameterSymbol>(StringComparer.Ordinal);

    if (syntax.GenericDecorator is { } generic) {
      for (int i = 0; i < generic.Parameters.Length; i++) {
        GenericParameterSyntax gp = generic.Parameters[i];
        string gpName = lex.GetText(gp.NameToken).ToString();
        var tp = new TypeParameterSymbol(gpName, i, gp.Id, gp.NameToken.Span, PreludeSource.VirtualPath);
        typeParams.Add(tp);
        typeParamMap.Add(gpName, tp);
      }
    }

    var union = new UnionSymbol(
        unionName,
        SymbolVisibility.Public,
        typeParams.ToImmutable(),
        usesNamedFields: false,
        syntax.Id,
        syntax.NameToken.Span,
        PreludeSource.VirtualPath,
        containingSymbol: null,
        isPrelude: true);

    foreach (TypeParameterSymbol tp in union.TypeParameters) {
      tp.Owner = union;
    }

    var variants = ImmutableArray.CreateBuilder<UnionVariantSymbol>();
    int tag = 0;
    foreach (SyntaxNode node in syntax.Variants) {
      if (node is not UnionVariantSyntax variantSyntax) {
        continue;
      }

      string variantName = lex.GetText(variantSyntax.NameToken).ToString();
      var variant = new UnionVariantSymbol(
          variantName,
          tag++,
          union,
          variantSyntax.Id,
          variantSyntax.NameToken.Span,
          PreludeSource.VirtualPath);

      var fields = ImmutableArray.CreateBuilder<UnionVariantField>(variantSyntax.Fields.Length);
      for (int f = 0; f < variantSyntax.Fields.Length; f++) {
        UnionVariantFieldSyntax fieldSyntax = variantSyntax.Fields[f];
        TypeSymbol fieldType = ResolvePreludeTypeSyntax(lex, fieldSyntax.Type, typeParamMap);
        fields.Add(new UnionVariantField(
            Name: null,
            fieldType,
            f,
            fieldSyntax.Id,
            fieldSyntax.Span));
      }

      variant.Fields = fields.MoveToImmutable();
      variants.Add(variant);
    }

    union.Variants = variants.ToImmutable();
    return union;
  }

  private static TypeSymbol ResolvePreludeTypeSyntax(
      LexResult lex,
      TypeSyntax typeSyntax,
      Dictionary<string, TypeParameterSymbol> typeParams) {
    if (typeSyntax is NamedTypeSyntax named) {
      string name = lex.GetText(named.NameToken).ToString();
      if (typeParams.TryGetValue(name, out TypeParameterSymbol? tp)) {
        return tp.Type;
      }

      return name switch {
        "bool" => PrimitiveType.Bool,
        "int" => PrimitiveType.Int,
        "float" => PrimitiveType.Float,
        "rune" => PrimitiveType.Rune,
        "string" => PrimitiveType.String,
        "regex" => PrimitiveType.Regex,
        "void" => VoidType.Instance,
        _ => ErrorType.Instance,
      };
    }

    return ErrorType.Instance;
  }

  private static UnionVariantSymbol FindVariant(UnionSymbol union, string name) {
    foreach (UnionVariantSymbol variant in union.Variants) {
      if (string.Equals(variant.Name, name, StringComparison.Ordinal)) {
        return variant;
      }
    }

    throw new InvalidOperationException($"Prelude union '{union.Name}' is missing variant '{name}'.");
  }

  private static TypeSymbol ResolveStubParamType(
      IntrinsicParameter parameter,
      UnionSymbol optionUnion,
      UnionSymbol resultUnion,
      UnionSymbol ioErrorUnion) =>
      parameter.Kind == IntrinsicParamKind.Fixed
          ? ResolveStubReturnType(parameter.TypeSpec, optionUnion, resultUnion, ioErrorUnion)
          : ErrorType.Instance;

  private static TypeSymbol ResolveStubReturnType(
      StdlibTypeSpec spec,
      UnionSymbol optionUnion,
      UnionSymbol resultUnion,
      UnionSymbol ioErrorUnion) =>
      spec switch {
        StdlibTypeSpec.Primitive p => PrimitiveType.FromKind(p.Kind),
        StdlibTypeSpec.Void => VoidType.Instance,
        StdlibTypeSpec.Never => NeverType.Instance,
        StdlibTypeSpec.IOError => new UnionType(ioErrorUnion, []),
        StdlibTypeSpec.Option opt => new UnionType(
            optionUnion,
            [ResolveStubReturnType(opt.Element, optionUnion, resultUnion, ioErrorUnion)]),
        StdlibTypeSpec.Result res => new UnionType(
            resultUnion,
            [
              ResolveStubReturnType(res.Success, optionUnion, resultUnion, ioErrorUnion),
              ResolveStubReturnType(res.Failure, optionUnion, resultUnion, ioErrorUnion),
            ]),
        StdlibTypeSpec.Vector vec => new VectorType(
            ResolveStubReturnType(vec.Element, optionUnion, resultUnion, ioErrorUnion)),
        _ => ErrorType.Instance,
      };
}
