using System.Collections.Immutable;

using Pino.Compiler.Checking;

namespace Pino.Compiler.IR;

public sealed record IrParam(
    string Name,
    IrTypeId Type,
    IrParamOwnership Ownership);

public sealed record IrLocalSlot(
    IrSlotId Id,
    string Name,
    IrTypeId Type,
    bool IsMutSlot = false,
    bool IsBoxedCell = false);

public sealed record IrTemp(
    IrTempId Id,
    IrTypeId Type,
    string? Name = null);

public sealed class IrBasicBlock(IrBlockId id) {
  public IrBlockId Id { get; } = id;

  public List<IrInstruction> Instructions { get; } = [];

  public IrTerminator? Terminator { get; set; }
}

public sealed class IrFunction(
    IrFuncId id,
    string name,
    ImmutableArray<IrParam> parameters,
    IrTypeId returnType,
    ModuleStateEffects moduleStateEffects = default) {
  public IrFuncId Id { get; } = id;

  public string Name { get; } = name;

  public ImmutableArray<IrParam> Parameters { get; } = parameters.IsDefault ? [] : parameters;

  public IrTypeId ReturnType { get; } = returnType;

  public ModuleStateEffects ModuleStateEffects { get; set; } = moduleStateEffects;

  public List<IrLocalSlot> Slots { get; } = [];

  public List<IrTemp> Temps { get; } = [];

  public List<IrBasicBlock> Blocks { get; } = [];

  public IrSlotId AddSlot(string name, IrTypeId type, bool isMutSlot = false, bool isBoxedCell = false) {
    var id = new IrSlotId(Slots.Count);
    Slots.Add(new IrLocalSlot(id, name, type, isMutSlot, isBoxedCell));
    return id;
  }

  public IrTempId AddTemp(IrTypeId type, string? name = null) {
    var id = new IrTempId(Temps.Count);
    Temps.Add(new IrTemp(id, type, name));
    return id;
  }

  public IrBasicBlock AddBlock() {
    var block = new IrBasicBlock(new IrBlockId(Blocks.Count));
    Blocks.Add(block);
    return block;
  }
}

public sealed record IrGlobal(
    IrGlobalId Id,
    string Name,
    IrTypeId Type,
    bool IsMutable);

public sealed record IrVTable(
    IrVTableId Id,
    IrTypeId InterfaceType,
    IrTypeId ConcreteStructType,
    ImmutableArray<IrFuncId> MethodFuncIds,
    ImmutableArray<int> PropertyFieldIndices = default,
    IrFuncId? DropFuncId = null,
    IrFuncId? CloneFuncId = null,
    IrFuncId? EqFuncId = null,
    IrFuncId? ToStrFuncId = null);

public sealed class IrModule {
  private readonly Dictionary<string, int> _stringInterner = new(StringComparer.Ordinal);

  public IrModule(IrTypeTable? types = null, bool isProgramMode = false) {
    Types = types ?? new IrTypeTable();
    IsProgramMode = isProgramMode;
  }

  public IrTypeTable Types { get; }

  public List<IrGlobal> Globals { get; } = [];

  public List<IrFunction> Functions { get; } = [];

  public List<IrVTable> VTables { get; } = [];

  public List<string> StringPool { get; } = [];

  public List<IrFuncId> InitFuncIds { get; } = [];

  public IrFuncId? EntryFuncId { get; set; }

  public IrFuncId? ShutdownFuncId { get; set; }

  public bool IsProgramMode { get; set; }

  public IrGlobalId AddGlobal(string name, IrTypeId type, bool isMutable) {
    var id = new IrGlobalId(Globals.Count);
    Globals.Add(new IrGlobal(id, name, type, isMutable));
    return id;
  }

  public IrFunction AddFunction(
      string name,
      ImmutableArray<IrParam> parameters,
      IrTypeId returnType,
      ModuleStateEffects moduleStateEffects = default) {
    var fn = new IrFunction(new IrFuncId(Functions.Count), name, parameters, returnType, moduleStateEffects);
    Functions.Add(fn);
    return fn;
  }

  public IrVTableId AddVTable(
      IrTypeId interfaceType,
      IrTypeId concreteStructType,
      ImmutableArray<IrFuncId> methodFuncIds,
      ImmutableArray<int> propertyFieldIndices = default,
      IrFuncId? dropFuncId = null,
      IrFuncId? cloneFuncId = null,
      IrFuncId? eqFuncId = null,
      IrFuncId? toStrFuncId = null) {
    var id = new IrVTableId(VTables.Count);
    VTables.Add(new IrVTable(
        id,
        interfaceType,
        concreteStructType,
        methodFuncIds.IsDefault ? [] : methodFuncIds,
        propertyFieldIndices.IsDefault ? [] : propertyFieldIndices,
        dropFuncId,
        cloneFuncId,
        eqFuncId,
        toStrFuncId));
    return id;
  }

  public int InternString(string value) {
    ArgumentNullException.ThrowIfNull(value);
    if (_stringInterner.TryGetValue(value, out int existing)) {
      return existing;
    }

    int id = StringPool.Count;
    StringPool.Add(value);
    _stringInterner[value] = id;
    return id;
  }
}
