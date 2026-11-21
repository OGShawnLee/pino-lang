using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Pino.Compiler.Checking;

public enum StdlibReceiverKind : byte {
  Global,
  String,
  Vector,
  Map,
  Set,
  Regex,
}

public enum IntrinsicParamKind : byte {
  Fixed,
  AnyValue,
  Numeric,
}

public abstract record StdlibTypeSpec {
  private protected StdlibTypeSpec() { }

  public abstract TypeSymbol Resolve(
      TypeInterner types,
      PreludeSymbols prelude,
      ReadOnlySpan<TypeSymbol> receiverArgs,
      ReadOnlySpan<TypeSymbol> methodArgs);

  public abstract string Format(
      IReadOnlyList<string> receiverParamNames,
      IReadOnlyList<string> methodParamNames);

  public sealed record Primitive(PrimitiveKind Kind) : StdlibTypeSpec {
    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.FromPrimitiveKind(Kind);

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) =>
        PrimitiveType.FromKind(Kind).DisplayName;
  }

  public sealed record Void : StdlibTypeSpec {
    public static Void Instance { get; } = new();
    private Void() { }

    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.Void;

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) => "void";
  }

  public sealed record Never : StdlibTypeSpec {
    public static Never Instance { get; } = new();
    private Never() { }

    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.Never;

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) => "never";
  }

  public sealed record AnyValue : StdlibTypeSpec {
    public static AnyValue Instance { get; } = new();
    private AnyValue() { }

    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.Error;

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) => "any";
  }

  public sealed record Numeric : StdlibTypeSpec {
    public static Numeric Instance { get; } = new();
    private Numeric() { }

    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.Float;

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) => "int|float";
  }

  public sealed record ReceiverTypeParam(int Index) : StdlibTypeSpec {
    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        receiverArgs[Index];

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) =>
        receiverParamNames[Index];
  }

  public sealed record MethodTypeParam(int Index) : StdlibTypeSpec {
    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        methodArgs[Index];

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) =>
        methodParamNames[Index];
  }

  public sealed record Vector(StdlibTypeSpec Element) : StdlibTypeSpec {
    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.GetVector(Element.Resolve(types, prelude, receiverArgs, methodArgs));

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) =>
        $"[]{Element.Format(receiverParamNames, methodParamNames)}";
  }

  public sealed record Option(StdlibTypeSpec Element) : StdlibTypeSpec {
    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.GetUnion(prelude.OptionUnion, [Element.Resolve(types, prelude, receiverArgs, methodArgs)]);

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) =>
        $"Option[{Element.Format(receiverParamNames, methodParamNames)}]";
  }

  public sealed record Result(StdlibTypeSpec Success, StdlibTypeSpec Failure) : StdlibTypeSpec {
    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.GetUnion(
            prelude.ResultUnion,
            [
              Success.Resolve(types, prelude, receiverArgs, methodArgs),
              Failure.Resolve(types, prelude, receiverArgs, methodArgs),
            ]);

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) =>
        $"Result[{Success.Format(receiverParamNames, methodParamNames)}, {Failure.Format(receiverParamNames, methodParamNames)}]";
  }

  public sealed record IOError : StdlibTypeSpec {
    public static IOError Instance { get; } = new();
    private IOError() { }

    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) =>
        types.GetUnion(prelude.IOErrorUnion);

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) => "IOError";
  }

  public sealed record Function(
      ImmutableArray<StdlibTypeSpec> ParameterTypes,
      StdlibTypeSpec ReturnType) : StdlibTypeSpec {
    public override TypeSymbol Resolve(
        TypeInterner types,
        PreludeSymbols prelude,
        ReadOnlySpan<TypeSymbol> receiverArgs,
        ReadOnlySpan<TypeSymbol> methodArgs) {
      var resolvedParams = ImmutableArray.CreateBuilder<TypeSymbol>(ParameterTypes.Length);
      foreach (StdlibTypeSpec param in ParameterTypes) {
        resolvedParams.Add(param.Resolve(types, prelude, receiverArgs, methodArgs));
      }
      TypeSymbol ret = ReturnType.Resolve(types, prelude, receiverArgs, methodArgs);
      return types.GetFunction(resolvedParams.MoveToImmutable(), ret);
    }

    public override string Format(
        IReadOnlyList<string> receiverParamNames,
        IReadOnlyList<string> methodParamNames) {
      string ps = string.Join(
          ", ",
          ParameterTypes.Select(p => p.Format(receiverParamNames, methodParamNames)));
      string ret = ReturnType.Format(receiverParamNames, methodParamNames);
      return $"fn ({ps}) -> {ret}";
    }
  }
}

public sealed record IntrinsicParameter(
    string Name,
    IntrinsicParamKind Kind,
    StdlibTypeSpec TypeSpec,
    bool IsVariadic = false);

public sealed record IntrinsicSignature(
    IntrinsicId Id,
    ImmutableArray<string> MethodTypeParameterNames,
    ImmutableArray<IntrinsicParameter> Parameters,
    StdlibTypeSpec ReturnType);

public sealed record IntrinsicDescriptor(
    StdlibReceiverKind Receiver,
    string Name,
    bool IsProperty,
    ImmutableArray<IntrinsicSignature> Signatures,
    string Documentation,
    ImmutableArray<string> RuleIds);

/// <summary>
/// Declarative standard-library catalog keyed by receiver kind, member name, and
/// <see cref="IntrinsicId"/>. No user overloading exists in Pino; stdlib arity and
/// callback variants are closed <see cref="IntrinsicSignature"/> tables.
/// </summary>
public static class StdlibCatalog {
  private static readonly StdlibTypeSpec s_bool = new StdlibTypeSpec.Primitive(PrimitiveKind.Bool);
  private static readonly StdlibTypeSpec s_int = new StdlibTypeSpec.Primitive(PrimitiveKind.Int);
  private static readonly StdlibTypeSpec s_float = new StdlibTypeSpec.Primitive(PrimitiveKind.Float);
  private static readonly StdlibTypeSpec s_rune = new StdlibTypeSpec.Primitive(PrimitiveKind.Rune);
  private static readonly StdlibTypeSpec s_string = new StdlibTypeSpec.Primitive(PrimitiveKind.String);
  private static readonly StdlibTypeSpec s_regex = new StdlibTypeSpec.Primitive(PrimitiveKind.Regex);
  private static readonly StdlibTypeSpec s_void = StdlibTypeSpec.Void.Instance;
  private static readonly StdlibTypeSpec s_never = StdlibTypeSpec.Never.Instance;
  private static readonly StdlibTypeSpec s_any = StdlibTypeSpec.AnyValue.Instance;
  private static readonly StdlibTypeSpec s_numeric = StdlibTypeSpec.Numeric.Instance;
  private static readonly StdlibTypeSpec s_ioError = StdlibTypeSpec.IOError.Instance;
  private static readonly StdlibTypeSpec s_t0 = new StdlibTypeSpec.ReceiverTypeParam(0);
  private static readonly StdlibTypeSpec s_t1 = new StdlibTypeSpec.ReceiverTypeParam(1);
  private static readonly StdlibTypeSpec s_r0 = new StdlibTypeSpec.MethodTypeParam(0);

  public static FrozenDictionary<string, IntrinsicDescriptor> Globals { get; }
  public static FrozenDictionary<string, IntrinsicDescriptor> StringMembers { get; }
  public static FrozenDictionary<string, IntrinsicDescriptor> VectorMembers { get; }
  public static FrozenDictionary<string, IntrinsicDescriptor> MapMembers { get; }
  public static FrozenDictionary<string, IntrinsicDescriptor> SetMembers { get; }
  public static FrozenDictionary<string, IntrinsicDescriptor> RegexMembers { get; }
  public static ImmutableArray<IntrinsicDescriptor> AllDescriptors { get; }

  private static readonly FrozenDictionary<IntrinsicId, (IntrinsicDescriptor Descriptor, IntrinsicSignature Signature)> s_byId;

  static StdlibCatalog() {
    var all = ImmutableArray.CreateBuilder<IntrinsicDescriptor>();

    Globals = BuildTable(
        all,
        [
          Global(
              "println",
              [Sig(IntrinsicId.Println, [VariadicAny("args")], s_void)],
              "Prints arguments separated by spaces followed by a newline.",
              ["BUILTIN-2", "BUILTIN-3", "BUILTIN-4", "BUILTIN-15"]),
          Global(
              "readline",
              [
                Sig(IntrinsicId.ReadlineNoPrompt, [], new StdlibTypeSpec.Result(s_string, s_string)),
                Sig(IntrinsicId.ReadlineWithPrompt, [Fixed("prompt", s_string)], new StdlibTypeSpec.Result(s_string, s_string)),
              ],
              "Optionally prints a prompt and reads one line from standard input.",
              ["BUILTIN-9", "D-030"]),
          Global(
              "int",
              [
                Sig(IntrinsicId.IntFromNumber, [Numeric("value")], s_int),
                Sig(IntrinsicId.IntFromString, [Fixed("value", s_string)], new StdlibTypeSpec.Result(s_int, s_string)),
              ],
              "Converts a number to int or parses a string as Result[int, string].",
              ["BUILTIN-16", "TYPE-11", "D-015", "D-038"]),
          Global(
              "float",
              [
                Sig(IntrinsicId.FloatFromNumber, [Numeric("value")], s_float),
                Sig(IntrinsicId.FloatFromString, [Fixed("value", s_string)], new StdlibTypeSpec.Result(s_float, s_string)),
              ],
              "Converts a number to float or parses a string as Result[float, string].",
              ["BUILTIN-16", "TYPE-11", "D-015", "D-038"]),
          Global(
              "rune",
              [
                Sig(IntrinsicId.RuneFromInt, [Fixed("value", s_int)], s_rune),
                Sig(IntrinsicId.RuneFromString, [Fixed("value", s_string)], s_rune),
              ],
              "Converts a code point or single-code-point string to rune.",
              ["RUNE-5", "RUNE-6"]),
          Global(
              "str",
              [Sig(IntrinsicId.Str, [Any("value")], s_string)],
              "Converts any value to its string representation.",
              ["STR-5", "BUILTIN-15"]),
          Global(
              "type",
              [Sig(IntrinsicId.Type, [Any("value")], s_string)],
              "Returns the type name of a value as a string.",
              ["TYPE-1"]),
          Global(
              "rand",
              [
                Sig(IntrinsicId.RandFloat, [], s_float),
                Sig(IntrinsicId.RandInt, [Fixed("limit", s_int)], s_int),
              ],
              "Returns a random float in [0.0, 1.0) or int in [0, limit).",
              ["BUILTIN-5", "BUILTIN-6"]),
          Global(
              "time",
              [Sig(IntrinsicId.Time, [], s_float)],
              "Returns milliseconds since the Unix epoch.",
              ["BUILTIN-7"]),
          Global(
              "sleep",
              [Sig(IntrinsicId.Sleep, [Fixed("ms", s_int)], s_void)],
              "Pauses the current thread for ms milliseconds.",
              ["BUILTIN-10"]),
          Global(
              "clear",
              [Sig(IntrinsicId.Clear, [], s_void)],
              "Clears the terminal screen.",
              ["BUILTIN-11"]),
          Global(
              "panic",
              [Sig(IntrinsicId.Panic, [Fixed("message", s_string)], s_never)],
              "Halts execution with exit code 101 and an error message.",
              ["ERR-3", "ERR-4", "ERR-5", "ERR-6"]),
          Global(
              "regex",
              [Sig(IntrinsicId.RegexCompile, [Fixed("pattern", s_string)], s_regex)],
              "Compiles a regular expression pattern.",
              ["BUILTIN-8", "RGX-1", "D-029"]),
          Global(
              "read_file",
              [Sig(IntrinsicId.ReadFile, [Fixed("path", s_string)], new StdlibTypeSpec.Result(s_string, s_ioError))],
              "Reads the full text contents of a file.",
              ["BUILTIN-12"]),
          Global(
              "write_file",
              [Sig(IntrinsicId.WriteFile, [Fixed("path", s_string), Fixed("content", s_string)], new StdlibTypeSpec.Result(s_string, s_ioError))],
              "Writes text content to a file.",
              ["BUILTIN-13"]),
          Global(
              "file_exists",
              [Sig(IntrinsicId.FileExists, [Fixed("path", s_string)], s_bool)],
              "Returns true when a file or directory exists at path.",
              ["BUILTIN-14"]),
        ]);

    StringMembers = BuildTable(
        all,
        [
          Prop(StdlibReceiverKind.String, "len", IntrinsicId.StrLen, s_int, "Number of Unicode code points (runes).", ["STRLIB-1"]),
          Prop(StdlibReceiverKind.String, "length", IntrinsicId.StrLen, s_int, "Alias of string:len.", ["STRLIB-1"]),
          Method(StdlibReceiverKind.String, "lower", [Sig(IntrinsicId.StrLower, [], s_string)], "Returns a lowercase copy.", ["STRLIB-2"]),
          Method(StdlibReceiverKind.String, "upper", [Sig(IntrinsicId.StrUpper, [], s_string)], "Returns an uppercase copy.", ["STRLIB-3"]),
          Method(StdlibReceiverKind.String, "trim", [Sig(IntrinsicId.StrTrim, [], s_string)], "Removes leading and trailing whitespace.", ["STRLIB-4"]),
          Method(StdlibReceiverKind.String, "trim_start", [Sig(IntrinsicId.StrTrimStart, [], s_string)], "Removes leading whitespace.", ["STRLIB-15"]),
          Method(StdlibReceiverKind.String, "trim_end", [Sig(IntrinsicId.StrTrimEnd, [], s_string)], "Removes trailing whitespace.", ["STRLIB-16"]),
          Method(StdlibReceiverKind.String, "to_owned", [Sig(IntrinsicId.StrClone, [], s_string)], "Returns an independent copy of the string.", ["STRLIB-9"]),
          Method(StdlibReceiverKind.String, "clone", [Sig(IntrinsicId.StrClone, [], s_string)], "Alias of string:to_owned().", ["STRLIB-9"]),
          Method(StdlibReceiverKind.String, "to_cstring", [Sig(IntrinsicId.StrToCString, [], s_string)], "Returns the string content for C interop.", ["STRLIB-10"]),
          Method(StdlibReceiverKind.String, "cstr", [Sig(IntrinsicId.StrToCString, [], s_string)], "Alias of string:to_cstring().", ["STRLIB-10"]),
          Method(StdlibReceiverKind.String, "contains", [Sig(IntrinsicId.StrContains, [Fixed("sub", s_string)], s_bool)], "Returns true if sub occurs in the string.", ["STRLIB-5"]),
          Method(StdlibReceiverKind.String, "starts_with", [Sig(IntrinsicId.StrStartsWith, [Fixed("prefix", s_string)], s_bool)], "Returns true if the string starts with prefix.", ["STRLIB-11"]),
          Method(StdlibReceiverKind.String, "ends_with", [Sig(IntrinsicId.StrEndsWith, [Fixed("suffix", s_string)], s_bool)], "Returns true if the string ends with suffix.", ["STRLIB-12"]),
          Method(StdlibReceiverKind.String, "index_of", [Sig(IntrinsicId.StrIndexOf, [Fixed("sub", s_string)], s_int)], "Returns first index of sub, or -1.", ["STRLIB-13"]),
          Method(StdlibReceiverKind.String, "last_index_of", [Sig(IntrinsicId.StrLastIndexOf, [Fixed("sub", s_string)], s_int)], "Returns last index of sub, or -1.", ["STRLIB-14"]),
          Method(StdlibReceiverKind.String, "split", [Sig(IntrinsicId.StrSplit, [Fixed("sep", s_string)], new StdlibTypeSpec.Vector(s_string))], "Splits the string by sep.", ["STRLIB-6"]),
          Method(StdlibReceiverKind.String, "replace", [Sig(IntrinsicId.StrReplace, [Fixed("old", s_string), Fixed("new", s_string)], s_string)], "Replaces all occurrences of old with new.", ["STRLIB-7"]),
          Method(StdlibReceiverKind.String, "substring", [Sig(IntrinsicId.StrSubstring, [Fixed("start", s_int), Fixed("len", s_int)], s_string)], "Returns a zero-copy slice starting at start of length len.", ["STRLIB-8", "STRLIB-18"]),
        ]);

    VectorMembers = BuildTable(
        all,
        [
          Prop(StdlibReceiverKind.Vector, "len", IntrinsicId.VecLen, s_int, "Number of elements.", ["VECLIB-1"]),
          Prop(StdlibReceiverKind.Vector, "length", IntrinsicId.VecLen, s_int, "Alias of vec:len.", ["VECLIB-2"]),
          Prop(StdlibReceiverKind.Vector, "cap", IntrinsicId.VecCap, s_int, "Allocated element capacity.", ["VECLIB-3"]),
          Prop(StdlibReceiverKind.Vector, "capacity", IntrinsicId.VecCap, s_int, "Alias of vec:cap.", ["VECLIB-4"]),
          Method(StdlibReceiverKind.Vector, "push", [Sig(IntrinsicId.VecPush, [Fixed("item", s_t0)], new StdlibTypeSpec.Vector(s_t0))], "Appends item in place and returns the vector.", ["VECLIB-5", "VECLIB-6"]),
          Method(StdlibReceiverKind.Vector, "add", [Sig(IntrinsicId.VecPush, [Fixed("item", s_t0)], new StdlibTypeSpec.Vector(s_t0))], "Alias of vec:push(item).", ["VECLIB-7"]),
          Method(StdlibReceiverKind.Vector, "pop", [Sig(IntrinsicId.VecPop, [], new StdlibTypeSpec.Option(s_t0))], "Removes and returns the last element as Option[T].", ["VECLIB-8", "VECLIB-9"]),
          Method(
              StdlibReceiverKind.Vector,
              "each",
              [
                Sig(IntrinsicId.VecEach1, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_void))], s_void),
                Sig(IntrinsicId.VecEach2, [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_void))], s_void),
              ],
              "Calls fn for each element in index order.",
              ["VECLIB-10", "VECLIB-11"]),
          Method(
              StdlibReceiverKind.Vector,
              "map",
              [
                GenericSig(IntrinsicId.VecMap1, ["R"], [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_r0))], new StdlibTypeSpec.Vector(s_r0)),
                GenericSig(IntrinsicId.VecMap2, ["R"], [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_r0))], new StdlibTypeSpec.Vector(s_r0)),
              ],
              "Maps each element with fn into a new vector.",
              ["VECLIB-12", "VECLIB-13"]),
          Method(
              StdlibReceiverKind.Vector,
              "filter",
              [
                Sig(IntrinsicId.VecFilter1, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], new StdlibTypeSpec.Vector(s_t0)),
                Sig(IntrinsicId.VecFilter2, [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_bool))], new StdlibTypeSpec.Vector(s_t0)),
              ],
              "Returns a new vector of elements matching fn.",
              ["VECLIB-14", "VECLIB-15"]),
          Method(
              StdlibReceiverKind.Vector,
              "find",
              [
                Sig(IntrinsicId.VecFind1, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], new StdlibTypeSpec.Option(s_t0)),
                Sig(IntrinsicId.VecFind2, [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_bool))], new StdlibTypeSpec.Option(s_t0)),
              ],
              "Returns the first element matching fn as Option[T].",
              ["VECLIB-16", "VECLIB-17", "VECLIB-23"]),
          Method(
              StdlibReceiverKind.Vector,
              "find_index",
              [
                Sig(IntrinsicId.VecFindIndex1, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], s_int),
                Sig(IntrinsicId.VecFindIndex2, [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_bool))], s_int),
              ],
              "Returns index of first element matching fn, or -1.",
              ["VECLIB-18", "VECLIB-19", "VECLIB-23"]),
          Method(
              StdlibReceiverKind.Vector,
              "any",
              [
                Sig(IntrinsicId.VecAny1, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], s_bool),
                Sig(IntrinsicId.VecAny2, [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_bool))], s_bool),
              ],
              "Returns true if at least one element matches fn.",
              ["VECLIB-20", "VECLIB-23"]),
          Method(
              StdlibReceiverKind.Vector,
              "all",
              [
                Sig(IntrinsicId.VecAll1, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], s_bool),
                Sig(IntrinsicId.VecAll2, [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_bool))], s_bool),
              ],
              "Returns true if every element matches fn.",
              ["VECLIB-21", "VECLIB-23"]),
          Method(
              StdlibReceiverKind.Vector,
              "count",
              [
                Sig(IntrinsicId.VecCount1, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], s_int),
                Sig(IntrinsicId.VecCount2, [Fixed("fn", new StdlibTypeSpec.Function([s_t0, s_int], s_bool))], s_int),
              ],
              "Counts elements matching fn.",
              ["VECLIB-22", "VECLIB-23", "VECLIB-24"]),
          Method(
              StdlibReceiverKind.Vector,
              "join",
              [Sig(IntrinsicId.VecJoin, [Fixed("separator", s_string)], s_string)],
              "Joins elements converted to strings using separator.",
              ["VECLIB-25", "D-065"]),
        ]);

    MapMembers = BuildTable(
        all,
        [
          Prop(StdlibReceiverKind.Map, "len", IntrinsicId.MapLen, s_int, "Number of entries.", ["MAPLIB-1"]),
          Prop(StdlibReceiverKind.Map, "length", IntrinsicId.MapLen, s_int, "Alias of map:len.", ["MAPLIB-2"]),
          Method(StdlibReceiverKind.Map, "keys", [Sig(IntrinsicId.MapKeys, [], new StdlibTypeSpec.Vector(s_t0))], "Returns a vector of all keys.", ["MAPLIB-3"]),
          Method(StdlibReceiverKind.Map, "values", [Sig(IntrinsicId.MapValues, [], new StdlibTypeSpec.Vector(s_t1))], "Returns a vector of all values in key order.", ["MAPLIB-4", "MAPLIB-5"]),
          Method(StdlibReceiverKind.Map, "remove", [Sig(IntrinsicId.MapRemove, [Fixed("key", s_t0)], new StdlibTypeSpec.Option(s_t1))], "Removes key and returns its previous value as Option[V].", ["MAPLIB-6", "MAPLIB-7"]),
        ]);

    SetMembers = BuildTable(
        all,
        [
          Prop(StdlibReceiverKind.Set, "len", IntrinsicId.SetLen, s_int, "Number of elements.", ["SETLIB-1"]),
          Prop(StdlibReceiverKind.Set, "length", IntrinsicId.SetLen, s_int, "Alias of set:len.", ["SETLIB-2"]),
          Method(StdlibReceiverKind.Set, "add", [Sig(IntrinsicId.SetAdd, [Fixed("element", s_t0)], s_void)], "Adds element to the set.", ["SETLIB-3", "D-175"]),
          Method(StdlibReceiverKind.Set, "remove", [Sig(IntrinsicId.SetRemove, [Fixed("element", s_t0)], s_void)], "Removes element from the set.", ["SETLIB-4", "D-175"]),
          Method(StdlibReceiverKind.Set, "any", [Sig(IntrinsicId.SetAny, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], s_bool)], "Returns true if predicate returns true for any element.", ["SETLIB-5", "D-175"]),
          Method(StdlibReceiverKind.Set, "all", [Sig(IntrinsicId.SetAll, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_bool))], s_bool)], "Returns true if predicate returns true for all elements.", ["SETLIB-6", "D-175"]),
          Method(StdlibReceiverKind.Set, "each", [Sig(IntrinsicId.SetEach, [Fixed("fn", new StdlibTypeSpec.Function([s_t0], s_void))], s_void)], "Calls fn once for each element.", ["SETLIB-7", "D-175"]),
        ]);

    RegexMembers = BuildTable(
        all,
        [
          Prop(StdlibReceiverKind.Regex, "pattern", IntrinsicId.RegexPattern, s_string, "Original pattern string.", ["RGX-2"]),
          Method(StdlibReceiverKind.Regex, "has_match", [Sig(IntrinsicId.RegexHasMatch, [Fixed("text", s_string)], s_bool)], "Returns true if the pattern matches anywhere in text.", ["RGX-3"]),
          Method(StdlibReceiverKind.Regex, "match_prefix", [Sig(IntrinsicId.RegexMatchPrefix, [Fixed("text", s_string)], s_string)], "Returns anchored prefix match or empty string.", ["RGX-4"]),
          Method(StdlibReceiverKind.Regex, "find", [Sig(IntrinsicId.RegexFind, [Fixed("text", s_string)], s_string)], "Returns first match in text or empty string.", ["RGX-5"]),
          Method(StdlibReceiverKind.Regex, "find_all", [Sig(IntrinsicId.RegexFindAll, [Fixed("text", s_string)], new StdlibTypeSpec.Vector(s_string))], "Returns all non-overlapping matches in text.", ["RGX-6"]),
          Method(StdlibReceiverKind.Regex, "replace", [Sig(IntrinsicId.RegexReplace, [Fixed("text", s_string), Fixed("repl", s_string)], s_string)], "Replaces every match in text with repl.", ["RGX-7"]),
        ]);

    AllDescriptors = all.ToImmutable();

    var byId = new Dictionary<IntrinsicId, (IntrinsicDescriptor, IntrinsicSignature)>();
    foreach (IntrinsicDescriptor descriptor in AllDescriptors) {
      foreach (IntrinsicSignature signature in descriptor.Signatures) {
        // Keep the primary descriptor name when an alias shares an IntrinsicId (e.g., len/length).
        byId.TryAdd(signature.Id, (descriptor, signature));
      }
    }
    s_byId = byId.ToFrozenDictionary();
  }

  public static bool TryGetGlobal(string name, [NotNullWhen(true)] out IntrinsicDescriptor? descriptor) {
    ArgumentNullException.ThrowIfNull(name);
    return Globals.TryGetValue(name, out descriptor);
  }

  public static bool TryGetMember(
      TypeSymbol receiverType,
      string memberName,
      [NotNullWhen(true)] out IntrinsicDescriptor? descriptor) {
    ArgumentNullException.ThrowIfNull(receiverType);
    ArgumentNullException.ThrowIfNull(memberName);

    return receiverType switch {
      PrimitiveType { Kind: PrimitiveKind.String } => StringMembers.TryGetValue(memberName, out descriptor),
      PrimitiveType { Kind: PrimitiveKind.Regex } => RegexMembers.TryGetValue(memberName, out descriptor),
      VectorType => VectorMembers.TryGetValue(memberName, out descriptor),
      MapType => MapMembers.TryGetValue(memberName, out descriptor),
      SetType => SetMembers.TryGetValue(memberName, out descriptor),
      _ => OutNull(out descriptor),
    };
  }

  public static bool TryGetById(
      IntrinsicId id,
      [NotNullWhen(true)] out IntrinsicDescriptor? descriptor,
      [NotNullWhen(true)] out IntrinsicSignature? signature) {
    if (s_byId.TryGetValue(id, out (IntrinsicDescriptor Descriptor, IntrinsicSignature Signature) entry)) {
      descriptor = entry.Descriptor;
      signature = entry.Signature;
      return true;
    }

    descriptor = null;
    signature = null;
    return false;
  }

  private static bool OutNull(out IntrinsicDescriptor? descriptor) {
    descriptor = null;
    return false;
  }

  private static FrozenDictionary<string, IntrinsicDescriptor> BuildTable(
      ImmutableArray<IntrinsicDescriptor>.Builder all,
      IntrinsicDescriptor[] entries) {
    var dict = new Dictionary<string, IntrinsicDescriptor>(entries.Length, StringComparer.Ordinal);
    foreach (IntrinsicDescriptor entry in entries) {
      dict.Add(entry.Name, entry);
      all.Add(entry);
    }
    return dict.ToFrozenDictionary(StringComparer.Ordinal);
  }

  private static IntrinsicDescriptor Global(
      string name,
      ImmutableArray<IntrinsicSignature> signatures,
      string documentation,
      ImmutableArray<string> ruleIds) =>
      new(StdlibReceiverKind.Global, name, false, signatures, documentation, ruleIds);

  private static IntrinsicDescriptor Prop(
      StdlibReceiverKind receiver,
      string name,
      IntrinsicId id,
      StdlibTypeSpec type,
      string documentation,
      ImmutableArray<string> ruleIds) =>
      new(receiver, name, true, [Sig(id, [], type)], documentation, ruleIds);

  private static IntrinsicDescriptor Method(
      StdlibReceiverKind receiver,
      string name,
      ImmutableArray<IntrinsicSignature> signatures,
      string documentation,
      ImmutableArray<string> ruleIds) =>
      new(receiver, name, false, signatures, documentation, ruleIds);

  private static IntrinsicSignature Sig(
      IntrinsicId id,
      ImmutableArray<IntrinsicParameter> parameters,
      StdlibTypeSpec returnType) =>
      new(id, [], parameters, returnType);

  private static IntrinsicSignature GenericSig(
      IntrinsicId id,
      ImmutableArray<string> methodTypeParameterNames,
      ImmutableArray<IntrinsicParameter> parameters,
      StdlibTypeSpec returnType) =>
      new(id, methodTypeParameterNames, parameters, returnType);

  private static IntrinsicParameter Fixed(string name, StdlibTypeSpec type) =>
      new(name, IntrinsicParamKind.Fixed, type);

  private static IntrinsicParameter Any(string name) =>
      new(name, IntrinsicParamKind.AnyValue, s_any);

  private static IntrinsicParameter VariadicAny(string name) =>
      new(name, IntrinsicParamKind.AnyValue, s_any, IsVariadic: true);

  private static IntrinsicParameter Numeric(string name) =>
      new(name, IntrinsicParamKind.Numeric, s_numeric);
}
