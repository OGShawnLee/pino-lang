using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.IR.Lowering;

/// <summary>
/// Golden IR lowering tests for the three Section 1.1 reference functions in
/// <c>056-re-ir-design/design.md</c> (<c>sum_vec</c>, <c>area</c>, and <c>Log::record</c>).
/// </summary>
public sealed class IrLoweringGoldenTests {
  [Fact]
  public void SumVec_LowersToExpectedPreRcIrAndVerifies() {
    const string Source = """
      fn sum_vec(xs []int) int {
        var total = 0
        for x in xs {
          total += x
        }
        return total
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "sum_vec");

    Assert.Equal(ModuleStateEffects.Pure, fn.ModuleStateEffects);
    string printed = IrPrinter.PrintFunction(module, fn);

    const string Expected = """
      fn sum_vec(@owned %xs: []int) -> int [pure] {
        slots: $total: int [mut], $snap: []int, $i: int [mut], $len: int
        bb0:
          %t0 = const.i64 0
          store $total, %t0
          store $snap, %xs
          %t1 = load $snap
          %t2 = vec.len %t1
          store $len, %t2
          %t3 = const.i64 0
          store $i, %t3
          br bb1
        bb1:
          %t4 = load $i
          %t5 = load $len
          %t6 = cmp.lt.i64 %t4, %t5
          br_if %t6, bb2, bb4
        bb2:
          %t7 = load $snap
          %t8 = load $i
          %x = vec.get.unchecked %t7, %t8
          %t10 = load $total
          %t11 = add.i64 %t10, %x
          store $total, %t11
          br bb3
        bb3:
          %t12 = load $i
          %t14 = const.i64 1
          %t13 = add.i64 %t12, %t14
          store $i, %t13
          br bb1
        bb4:
          %t15 = load $total
          ret %t15
      }

      """;

    Assert.Equal(NormalizeLf(Expected), printed);
  }

  [Fact]
  public void Area_LowersToExpectedPreRcIrAndVerifies() {
    const string Source = """
      union Shape {
        Circle(r float)
        Rect(w float, h float)
      }

      fn area(s Shape) float {
        return match s {
          when Shape::Circle(r) -> 3.14159 * r * r
          when Shape::Rect(w, h) -> w * h
        }
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "area");

    Assert.Equal(ModuleStateEffects.Pure, fn.ModuleStateEffects);
    string printed = IrPrinter.PrintFunction(module, fn);

    const string Expected = """
      fn area(@owned %s: Shape) -> float [pure] {
        slots: $res: float [mut]
        bb0:
          %tag = union.tag %s
          switch %tag, [0 -> bb1, 1 -> bb2]
        bb1:
          %r = union.field %s, 0, 0
          %t2 = const.f64 3.14159
          %t3 = mul.f64 %t2, %r
          %t4 = mul.f64 %t3, %r
          store $res, %t4
          br bb3
        bb2:
          %w = union.field %s, 1, 0
          %h = union.field %s, 1, 1
          %t7 = mul.f64 %w, %h
          store $res, %t7
          br bb3
        bb3:
          %t8 = load $res
          ret %t8
      }

      """;

    Assert.Equal(NormalizeLf(Expected), printed);
  }

  [Fact]
  public void LogRecord_LowersToExpectedPreRcIrAndVerifies() {
    const string Source = """
      struct Log {
        mut entries []string

        mut fn record(msg string) {
          self:entries:push(msg)
        }
      }
      """;

    IrModule module = LowerAndVerify(Source);
    IrFunction fn = Assert.Single(module.Functions, static f => f.Name == "Log::record");

    Assert.Equal(ModuleStateEffects.Pure, fn.ModuleStateEffects);
    string printed = IrPrinter.PrintFunction(module, fn);

    const string Expected = """
      fn Log::record(@mut %self: ptr<Log>, @owned %msg: string) -> void [pure] {
        bb0:
          %t0 = field_addr %self, Log.entries
          ensure_unique %t0
          vec.push %t0, %msg
          ret.void
      }

      """;

    Assert.Equal(NormalizeLf(Expected), printed);
  }

  private static IrModule LowerAndVerify(string source) {
    var parsed = Parser.Parse(new SourceText(source, "golden.pino"));
    Assert.Empty(parsed.Diagnostics);
    Compilation compilation = Compilation.Create(parsed);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(verification.IsValid, string.Join("\n", verification.Errors));
    return module;
  }

  private static string NormalizeLf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
