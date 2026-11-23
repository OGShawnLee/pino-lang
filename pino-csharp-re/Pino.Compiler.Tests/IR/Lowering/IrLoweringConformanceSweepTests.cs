using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;
using Pino.Compiler.Text;

namespace Pino.Compiler.Tests.IR.Lowering;

/// <summary>
/// Sweeps all positive (<c>.out</c>) conformance cases and in-scope runtime-error (<c>.err</c>)
/// cases that do not depend on ticket-061 features (closures/function values, interfaces, user
/// generics, or the 19 callback intrinsics), verifying that <see cref="IrLowerer.Lower"/> succeeds
/// and <see cref="IrVerifier.Verify"/> reports 0 errors on every case.
/// </summary>
public sealed class IrLoweringConformanceSweepTests {
  private static readonly string s_repoRoot = ParserTestHelpers.FindRepositoryRoot();
  private static readonly string s_conformanceRoot = Path.Combine(s_repoRoot, "conformance");

  public static TheoryData<string> PositiveConformanceCases() {
    var data = new TheoryData<string>();
    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (File.Exists(Path.ChangeExtension(file, ".out"))) {
        data.Add(ToConformanceRelativePath(file));
      }
    }
    return data;
  }

  public static TheoryData<string> RuntimeErrConformanceCases() {
    var data = new TheoryData<string>();
    foreach (string rel in Checking.CheckerConformanceTests.RuntimeErrCases.Keys
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      data.Add(rel);
    }
    return data;
  }

  [Theory]
  [MemberData(nameof(PositiveConformanceCases))]
  public void PositiveConformanceCase_LowersAndVerifiesCleanlyWhenInTicket060Scope(string relativePath) {
    Compilation compilation = CompileConformanceFile(relativePath);
    if (UsesTicket061Features(compilation, out _)) {
      return;
    }

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(
        verification.IsValid,
        $"{relativePath} failed IrVerifier with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors)
        + "\n\n--- Printed IR ---\n"
        + IrPrinter.Print(module));
  }

  [Theory]
  [MemberData(nameof(RuntimeErrConformanceCases))]
  public void RuntimeErrConformanceCase_LowersAndVerifiesCleanlyWhenInTicket060Scope(string relativePath) {
    Compilation compilation = CompileConformanceFile(relativePath);
    if (UsesTicket061Features(compilation, out _)) {
      return;
    }

    IrModule module = IrLowerer.Lower(compilation);
    IrVerificationResult verification = IrVerifier.Verify(module);
    Assert.True(
        verification.IsValid,
        $"{relativePath} failed IrVerifier with {verification.Errors.Length} error(s):\n"
        + string.Join("\n", verification.Errors));
  }

  [Fact]
  public void ConformanceSweep_CoversLargeMajorityOfPositiveCases() {
    int total = 0;
    int lowered = 0;
    int deferred061 = 0;
    var failures = new List<string>();

    foreach (string file in Directory.EnumerateFiles(s_conformanceRoot, "*.pino", SearchOption.AllDirectories)
        .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)) {
      if (!File.Exists(Path.ChangeExtension(file, ".out"))) {
        continue;
      }

      total++;
      string rel = ToConformanceRelativePath(file);
      Compilation compilation = CompileConformanceFile(rel);
      if (UsesTicket061Features(compilation, out _)) {
        deferred061++;
        continue;
      }

      try {
        IrModule module = IrLowerer.Lower(compilation);
        IrVerificationResult verification = IrVerifier.Verify(module);
        if (!verification.IsValid) {
          failures.Add($"{rel}: IrVerifier errors: {string.Join("; ", verification.Errors)}");
        } else {
          lowered++;
        }
      } catch (Exception ex) {
        failures.Add($"{rel}: {ex.GetType().Name}: {ex.Message}");
      }
    }

    Assert.True(
        failures.Count == 0,
        $"Conformance sweep had {failures.Count} failure(s) (total={total}, lowered={lowered}, deferred061={deferred061}):\n"
        + string.Join("\n", failures.Take(40)));
    Assert.True(lowered > 200, $"Expected >200 lowered conformance cases, got {lowered} (total={total}, deferred061={deferred061}).");
  }

  private static Compilation CompileConformanceFile(string relativePath) {
    string fullPath = Path.Combine(s_conformanceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    string content = File.ReadAllText(fullPath);
    var parsed = Parser.Parse(new SourceText(content, fullPath));
    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Compilation compilation = Compilation.Create(parsed, FileSystemModuleSource.Instance);
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error));
    return compilation;
  }

  private static bool UsesTicket061Features(Compilation compilation, out string reason) {
    foreach (GenericInstantiation inst in compilation.GenericInstantiations) {
      if (inst.Definition is not UnionSymbol { Name: "Option" or "Result" }) {
        reason = $"user generic instantiation {inst.Definition.Name}";
        return true;
      }

      if (inst.TypeArguments.Any(ContainsTicket061Type)) {
        reason = $"generic instantiation {inst.Definition.Name} uses ticket-061 type arg";
        return true;
      }
    }

    foreach (SemanticModel model in compilation.SemanticModels) {
      if (ModelUsesTicket061Features(model, out reason)) {
        return true;
      }
    }

    reason = string.Empty;
    return false;
  }

  private static bool ModelUsesTicket061Features(SemanticModel model, out string reason) {
    var stack = new Stack<SyntaxNode>();
    stack.Push(model.Tree.Root);

    while (stack.Count > 0) {
      SyntaxNode node = stack.Pop();

      switch (node) {
        case InterfaceDeclarationSyntax:
          reason = "interface declaration";
          return true;

        case LambdaExpressionSyntax:
          reason = "lambda expression";
          return true;

        case FunctionDeclarationSyntax fnDecl:
          if (model.GetDeclaredSymbol(fnDecl) is FunctionSymbol fnSym) {
            if (!fnSym.TypeParameters.IsEmpty) {
              reason = "generic function declaration";
              return true;
            }

            if (model.GetCaptures(fnDecl) is { IsEmpty: false }) {
              reason = "capturing nested function";
              return true;
            }

            if (ContainsTicket061Type(fnSym.ReturnType)) {
              reason = "function return type uses ticket-061 type";
              return true;
            }

            foreach (ParameterSymbol p in fnSym.Parameters) {
              if (ContainsTicket061Type(p.Type)) {
                reason = "function parameter uses ticket-061 type";
                return true;
              }
            }
          }
          break;

        case StructDeclarationSyntax structDecl:
          if (model.GetDeclaredSymbol(structDecl) is StructSymbol st) {
            if (!st.TypeParameters.IsEmpty) {
              reason = "generic struct declaration";
              return true;
            }

            foreach (FieldSymbol f in st.Fields) {
              if (ContainsTicket061Type(f.Type)) {
                reason = "struct field uses ticket-061 type";
                return true;
              }
            }

            foreach (MethodSymbol m in st.Methods) {
              if (!m.TypeParameters.IsEmpty) {
                reason = "generic method declaration";
                return true;
              }

              if (ContainsTicket061Type(m.ReturnType) || m.Parameters.Any(p => ContainsTicket061Type(p.Type))) {
                reason = "method signature uses ticket-061 type";
                return true;
              }
            }
          }
          break;

        case UnionDeclarationSyntax unionDecl:
          if (model.GetDeclaredSymbol(unionDecl) is UnionSymbol us) {
            if (!us.TypeParameters.IsEmpty) {
              reason = "generic union declaration";
              return true;
            }

            foreach (UnionVariantSymbol v in us.Variants) {
              foreach (UnionVariantField f in v.Fields) {
                if (ContainsTicket061Type(f.Type)) {
                  reason = "union variant field uses ticket-061 type";
                  return true;
                }
              }
            }
          }
          break;

        case CallExpressionSyntax call:
          if (model.GetCallResolution(call) is { } ci) {
            if (ci.Kind is CallKind.IndirectFunctionValue or CallKind.BoundMethodValue
                or CallKind.CallableField or CallKind.IndexedElementCall) {
              reason = $"indirect call ({ci.Kind})";
              return true;
            }

            if (ci.Kind == CallKind.Intrinsic && ci.Intrinsic is { } intr && IsCallbackIntrinsic(intr)) {
              reason = $"callback intrinsic {intr}";
              return true;
            }
          } else if (call.Callee is MemberAccessExpressionSyntax memCall
              && model.GetType(memCall.Target) is VectorType or SetType
              && model.Tree.Lex.GetText(memCall.MemberToken).ToString() is "each" or "map" or "filter"
                  or "find" or "find_index" or "any" or "all" or "count") {
            reason = "callback intrinsic with built-in function argument";
            return true;
          }
          break;

        case ExpressionSyntax expr:
          if (model.GetType(expr) is { } exprType) {
            bool isDirectCallee = model.GetParent(expr) is CallExpressionSyntax parentCall
                && ReferenceEquals(parentCall.Callee, expr);
            if (!isDirectCallee && ContainsTicket061Type(exprType)) {
              reason = $"expression uses ticket-061 type {exprType.DisplayName}";
              return true;
            }
          }
          break;
      }

      foreach (SyntaxElement elem in node.GetChildren()) {
        if (elem.IsNode) {
          stack.Push(elem.AsNode());
        }
      }
    }

    reason = string.Empty;
    return false;
  }

  private static bool ContainsTicket061Type(TypeSymbol type) => type switch {
    InterfaceType or FunctionType or TypeParameterType => true,
    StructType st => !st.TypeArguments.IsEmpty,
    UnionType ut => !ut.TypeArguments.IsEmpty
        && (ut.Definition.Name is not ("Option" or "Result")
            || ut.TypeArguments.Any(ContainsTicket061Type)),
    VectorType v => ContainsTicket061Type(v.ElementType),
    MapType m => ContainsTicket061Type(m.KeyType) || ContainsTicket061Type(m.ValueType),
    SetType s => ContainsTicket061Type(s.ElementType),
    PoolType p => ContainsTicket061Type(p.ElementType),
    HandleType h => ContainsTicket061Type(h.ElementType),
    TupleType t => t.Fields.Any(f => ContainsTicket061Type(f.Type)),
    _ => false,
  };

  private static bool IsCallbackIntrinsic(IntrinsicId id) => id is
      IntrinsicId.VecEach1 or IntrinsicId.VecEach2
      or IntrinsicId.VecMap1 or IntrinsicId.VecMap2
      or IntrinsicId.VecFilter1 or IntrinsicId.VecFilter2
      or IntrinsicId.VecFind1 or IntrinsicId.VecFind2
      or IntrinsicId.VecFindIndex1 or IntrinsicId.VecFindIndex2
      or IntrinsicId.VecAny1 or IntrinsicId.VecAny2
      or IntrinsicId.VecAll1 or IntrinsicId.VecAll2
      or IntrinsicId.VecCount1 or IntrinsicId.VecCount2
      or IntrinsicId.SetAny or IntrinsicId.SetAll or IntrinsicId.SetEach;

  private static string ToConformanceRelativePath(string fullPath) =>
      Path.GetRelativePath(s_conformanceRoot, fullPath).Replace('\\', '/');
}
