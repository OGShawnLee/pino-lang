using System.Collections.Immutable;

namespace Pino.Compiler.Checking;

public enum ConversionKind : byte {
  None,
  IntToFloat,
  StructToInterface,
  NeverToTarget,
  ImplicitItLambda,
  StringInterpolationFormat,
}

public sealed record ConversionInfo(
    ConversionKind Kind,
    TypeSymbol TargetType,
    InterfaceType? TargetInterface = null,
    LocalSymbol? ImplicitItSymbol = null,
    FunctionType? ImplicitLambdaType = null);

public enum CallKind : byte {
  DirectFunction,
  IndirectFunctionValue,
  InstanceMethod,
  StaticMethod,
  BoundMethodValue,
  CallableField,
  UnionVariantConstructor,
  IndexedElementCall,
  Intrinsic,
}

public sealed record CallResolution(
    CallKind Kind,
    Symbol? TargetSymbol = null,
    IntrinsicId? Intrinsic = null,
    UnionType? ConstructedUnionType = null,
    bool HasImplicitSelf = false,
    Symbol? IndexSymbol = null);

public enum LoopKind : byte {
  Infinite,
  ConditionLoop,
  CountLoop,
  ForInVector,
  ForInString,
  ForInMap,
  ForInSet,
  ForInRange,
}

public sealed record LoopInfo(
    LoopKind Kind,
    LocalSymbol? ImplicitItSymbol = null,
    LocalSymbol? FirstBinding = null,
    LocalSymbol? SecondBinding = null);

public sealed record PatternBindingEntry(
    int FieldOrdinal,
    LocalSymbol Binding);

public sealed record PatternBindingInfo(
    UnionVariantSymbol Variant,
    ImmutableArray<PatternBindingEntry> Bindings);

public enum ConstantValueKind : byte {
  Int,
  Float,
  Bool,
  String,
  Rune,
}

public sealed record ConstantValue(
    ConstantValueKind Kind,
    long IntValue = 0,
    double FloatValue = 0.0,
    bool BoolValue = false,
    string? StringValue = null,
    int RuneValue = 0) {
  public bool IsZero => Kind switch {
    ConstantValueKind.Int => IntValue == 0,
    ConstantValueKind.Float => FloatValue == 0.0,
    _ => false,
  };

  public static ConstantValue FromInt(long value) => new(ConstantValueKind.Int, IntValue: value);

  public static ConstantValue FromFloat(double value) => new(ConstantValueKind.Float, FloatValue: value);

  public static ConstantValue FromBool(bool value) => new(ConstantValueKind.Bool, BoolValue: value);

  public static ConstantValue FromString(string value) => new(ConstantValueKind.String, StringValue: value);

  public static ConstantValue FromRune(int value) => new(ConstantValueKind.Rune, RuneValue: value);
}

public sealed record GenericInstantiation(
    Symbol Definition,
    ImmutableArray<TypeSymbol> TypeArguments);

public enum MutationKind : byte {
  DirectAssign,
  CompoundAssign,
  FieldWrite,
  IndexWrite,
  MutatingCall,
  MutArgument,
}

public enum MutationRootKind : byte {
  LocalVar,
  MutParameter,
  MutSelf,
  TopLevelVar,
  CapturedVar,
}

/// <summary>
/// Records a verified mutation place for downstream lowering and AD-003 escape/arena analysis.
/// Under AD-003 value semantics, <see cref="MutationRootKind.MutParameter"/>,
/// <see cref="MutationRootKind.MutSelf"/>, <see cref="MutationRootKind.TopLevelVar"/>, and
/// <see cref="MutationRootKind.CapturedVar"/> mark the non-return escape write points for future
/// arena/escape analysis, alongside <see cref="MutationRootKind.LocalVar"/> for local mutations.
/// </summary>
public sealed record MutationPlaceInfo(
    MutationKind Kind,
    Symbol RootSymbol,
    MutationRootKind RootKind,
    bool RequiresCoWCheck,
    bool PassByInOut);
