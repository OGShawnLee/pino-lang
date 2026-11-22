using System.Collections.Immutable;

using Pino.Compiler.Syntax;

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

/// <summary>
/// Identifies one <c>mut</c> access in a call expression: either the mutating method receiver
/// (<see cref="IsReceiver"/> is <see langword="true"/>, <see cref="ArgumentIndex"/> is <c>-1</c>)
/// or a zero-based <c>mut</c> call argument.
/// </summary>
public readonly record struct MutAccessTarget(int ArgumentIndex) {
  public static MutAccessTarget Receiver => new(-1);

  public bool IsReceiver => ArgumentIndex < 0;

  public static MutAccessTarget Argument(int index) => new(index);
}

/// <summary>
/// A pair of index steps at the same depth of a shared place-path prefix whose equality could not
/// be proven statically under D-184 (FN-34).
/// </summary>
/// <remarks>
/// IR obligation (D-184 / FN-34): at run time, compare the resolved slot addresses selected by
/// <see cref="First"/> and <see cref="Second"/> at this prefix depth, not index or key values,
/// and not only final leaf addresses (for example, in <c>f(mut enemies[i], mut enemies[j]:hp)</c>,
/// compare <c>&amp;enemies[i]</c> with <c>&amp;enemies[j]</c>).
/// </remarks>
public readonly record struct RuntimeDisjointnessStepPair(
    IndexExpressionSyntax First,
    IndexExpressionSyntax Second);

/// <summary>
/// Records a pair of <c>mut</c> accesses in one call that share a root binding and require a
/// run-time slot-exclusivity check (D-184 / FN-34, FN-35).
/// </summary>
/// <remarks>
/// IR obligation (D-184 / FN-34): after all call arguments are evaluated and their slots resolved
/// (with any absent map key in <c>mut m[k]</c> panicking first per MAP-E1), and before entering
/// the callee, compare the resolved slot addresses for every pair in <see cref="UnknownSteps"/>
/// (always comparing resolved slot addresses at each unknown step, never index or key values).
/// The two places alias if and only if every pair in <see cref="UnknownSteps"/> resolves to the
/// same slot address; if they alias, panic (ERR-3, exit code 101 per ERR-5) with the FN-34 message.
/// </remarks>
public sealed record RuntimeDisjointnessCheck(
    Symbol RootSymbol,
    MutAccessTarget FirstAccess,
    MutAccessTarget SecondAccess,
    ImmutableArray<RuntimeDisjointnessStepPair> UnknownSteps);
