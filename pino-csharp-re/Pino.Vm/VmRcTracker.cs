using System.Globalization;
using System.Text;

using Pino.Compiler.IR;
using Pino.Compiler.Text;

namespace Pino.Vm;

public enum VmRcViolationKind {
  UseAfterFree,
  DoubleFree,
  NegativeCount,
  Leak,
}

/// <summary>Raised the moment the shadow-count validator sees an RC invariant break (design §4.3).</summary>
public sealed class PinoVmRcException(VmRcViolationKind kind, string message) : Exception(message) {
  public VmRcViolationKind Kind { get; } = kind;
}

public sealed record VmRcLeak(
    long AllocId,
    string KindName,
    string TypeName,
    int ShadowRc,
    SourceSpan AllocSpan,
    string? AllocFunction,
    bool IsCycleLeak);

/// <summary>The result of the exit-time leak check. Cycle leaks (<c>CYCLE-LEAK</c>) are reported separately.</summary>
public sealed class VmRcLeakReport(IReadOnlyList<VmRcLeak> leaks, IReadOnlyList<VmRcLeak> cycleLeaks) {
  public IReadOnlyList<VmRcLeak> Leaks { get; } = leaks;

  public IReadOnlyList<VmRcLeak> CycleLeaks { get; } = cycleLeaks;

  /// <summary>True when no non-cycle object leaked.</summary>
  public bool IsClean => Leaks.Count == 0;

  public string Format() {
    var sb = new StringBuilder();
    foreach (VmRcLeak leak in Leaks) {
      AppendLeak(sb, "LEAK", leak);
    }

    foreach (VmRcLeak leak in CycleLeaks) {
      AppendLeak(sb, "CYCLE-LEAK", leak);
    }

    return sb.ToString();
  }

  private static void AppendLeak(StringBuilder sb, string label, VmRcLeak leak) {
    sb.Append(label)
        .Append(": ")
        .Append(leak.KindName)
        .Append(' ')
        .Append(leak.TypeName)
        .Append(" (alloc #")
        .Append(leak.AllocId.ToString(CultureInfo.InvariantCulture))
        .Append(") still has a shadow count of ")
        .Append(leak.ShadowRc.ToString(CultureInfo.InvariantCulture))
        .Append(", allocated in ")
        .Append(leak.AllocFunction ?? "<unknown>")
        .Append(" at offset ")
        .Append(leak.AllocSpan.Start.ToString(CultureInfo.InvariantCulture))
        .Append('\n');
  }
}

/// <summary>
/// The Step 1 shadow-count RC validator. The shadow count is the real reference count of every VM object (it decides
/// copy-on-write uniqueness); when <see cref="VerifyRc"/> is on, every violation also fails immediately with a plain
/// report, and live objects are registered so leaks can be listed after <c>__pino_shutdown()</c>.
/// </summary>
public sealed class VmRcTracker {
  private readonly Dictionary<long, PinoVmHeapObject> _live = [];
  private readonly IrTypeTable? _types;
  private long _nextAllocId = 1;

  public VmRcTracker(IrTypeTable? types, bool verifyRc) {
    _types = types;
    VerifyRc = verifyRc;
  }

  public bool VerifyRc { get; }

  public long AllocationCount => _nextAllocId - 1;

  public int LiveObjectCount => _live.Count;

  /// <summary>Registers a freshly allocated object with shadow count <c>1</c> (or an arena/immortal count).</summary>
  public T Register<T>(T obj, IrTypeId typeId, SourceSpan span, string? function, AllocRegionKind region = AllocRegionKind.Heap)
      where T : PinoVmHeapObject {
    obj.AllocId = _nextAllocId++;
    obj.TypeId = typeId;
    obj.AllocSpan = span;
    obj.AllocFunction = function;
    if (_types is not null && typeId.IsValid && _types.TryGetType(typeId, out IrType? type)) {
      obj.IsCycleCandidate = type.IsCycleCapable;
    }

    if (region == AllocRegionKind.Arena) {
      obj.IsArena = true;
      obj.ShadowRc = 0;
    } else {
      obj.ShadowRc = 1;
      if (VerifyRc) {
        _live[obj.AllocId] = obj;
      }
    }

    return obj;
  }

  /// <summary>Makes <paramref name="obj"/> an immortal singleton (literals, empty singletons).</summary>
  public T MakeImmortal<T>(T obj, IrTypeId typeId) where T : PinoVmHeapObject {
    obj.AllocId = _nextAllocId++;
    obj.TypeId = typeId;
    obj.ShadowRc = PinoVmHeapObject.ImmortalRc;
    return obj;
  }

  /// <summary>Fails when <paramref name="obj"/> has already been freed (use-after-free on any access).</summary>
  public void CheckAccess(PinoVmHeapObject obj, string operation) {
    if (obj.IsDead && VerifyRc) {
      throw Violation(VmRcViolationKind.UseAfterFree, obj, $"{operation} used an object after it was freed");
    }
  }

  public void Inc(PinoVmHeapObject obj) {
    if (obj.IsDead) {
      if (VerifyRc) {
        throw Violation(VmRcViolationKind.UseAfterFree, obj, "rc_inc was applied to an object after it was freed");
      }

      return;
    }

    if (obj.IsImmortal || obj.IsArena) {
      return;
    }

    if (obj.ShadowRc < 0) {
      if (VerifyRc) {
        throw Violation(VmRcViolationKind.NegativeCount, obj, "rc_inc found a negative shadow count");
      }

      return;
    }

    obj.ShadowRc++;
  }

  /// <summary>Decrements the count; returns <see langword="true"/> when the object just died and its children must be dropped.</summary>
  public bool Dec(PinoVmHeapObject obj) {
    if (obj.IsDead) {
      if (VerifyRc) {
        throw Violation(VmRcViolationKind.DoubleFree, obj, "rc_dec was applied to an object that was already freed");
      }

      return false;
    }

    if (obj.IsImmortal || obj.IsArena) {
      return false;
    }

    if (obj.ShadowRc < 0) {
      if (VerifyRc) {
        throw Violation(VmRcViolationKind.NegativeCount, obj, "rc_dec found a negative shadow count");
      }

      return false;
    }

    obj.ShadowRc--;
    if (obj.ShadowRc == 0) {
      _live.Remove(obj.AllocId);
      return true;
    }

    return false;
  }

  /// <summary>Lists every non-immortal object that still has a positive shadow count, classifying cycle-reachable ones.</summary>
  public VmRcLeakReport ReportLeaks() {
    var leaked = new List<PinoVmHeapObject>();
    foreach (PinoVmHeapObject obj in _live.Values) {
      if (!obj.IsImmortal && !obj.IsArena && obj.ShadowRc > 0) {
        leaked.Add(obj);
      }
    }

    leaked.Sort(static (a, b) => a.AllocId.CompareTo(b.AllocId));

    var cycleSet = new HashSet<long>();
    var work = new Stack<PinoVmHeapObject>();
    foreach (PinoVmHeapObject obj in leaked) {
      if (obj.IsCycleCandidate && cycleSet.Add(obj.AllocId)) {
        work.Push(obj);
      }
    }

    var children = new List<PinoVmHeapObject>();
    while (work.Count > 0) {
      PinoVmHeapObject current = work.Pop();
      children.Clear();
      current.CollectChildren(children);
      foreach (PinoVmHeapObject child in children) {
        if (!child.IsImmortal && !child.IsArena && child.ShadowRc > 0 && cycleSet.Add(child.AllocId)) {
          work.Push(child);
        }
      }
    }

    var plain = new List<VmRcLeak>();
    var cycle = new List<VmRcLeak>();
    foreach (PinoVmHeapObject obj in leaked) {
      bool isCycle = cycleSet.Contains(obj.AllocId);
      var leak = new VmRcLeak(
          obj.AllocId,
          obj.KindName,
          TypeName(obj.TypeId),
          obj.ShadowRc,
          obj.AllocSpan,
          obj.AllocFunction,
          isCycle);
      (isCycle ? cycle : plain).Add(leak);
    }

    return new VmRcLeakReport(plain, cycle);
  }

  private PinoVmRcException Violation(VmRcViolationKind kind, PinoVmHeapObject obj, string what) {
    string label = kind switch {
      VmRcViolationKind.UseAfterFree => "use-after-free",
      VmRcViolationKind.DoubleFree => "double-free",
      VmRcViolationKind.NegativeCount => "negative count",
      _ => "leak",
    };
    string message = string.Create(
        CultureInfo.InvariantCulture,
        $"RC error ({label}): {what}: {obj.KindName} {TypeName(obj.TypeId)} (alloc #{obj.AllocId}, shadow count {obj.ShadowRc}, allocated in {obj.AllocFunction ?? "<unknown>"} at offset {obj.AllocSpan.Start}).");
    return new PinoVmRcException(kind, message);
  }

  private string TypeName(IrTypeId typeId) =>
      _types is not null && typeId.IsValid && _types.TryGetType(typeId, out IrType? type) ? type.Name : "<unknown type>";
}
