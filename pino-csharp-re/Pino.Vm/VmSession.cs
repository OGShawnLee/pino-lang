using System.Collections.Immutable;

using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.IR.Passes;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Vm;

/// <summary>The outcome of one REPL submission on <see cref="VmSession"/>.</summary>
public sealed class VmSessionResult {
  public bool Succeeded { get; init; }

  public int ExitCode { get; init; }

  public string? RenderedDiagnostics { get; init; }

  public string? PanicMessage { get; init; }

  public bool PrintedValue { get; init; }

  public long Steps { get; init; }
}

/// <summary>
/// Persistent interactive REPL session backed by a single live <see cref="PinoVm"/> instance.
/// Each submission is lowered into a new <c>__repl_chunk_N()</c> function referencing persistent
/// global slots, RC-processed incrementally, verified, and executed once without replaying earlier
/// chunks. <see cref="Reset"/> and <see cref="Shutdown"/> release all live RC globals cleanly.
/// </summary>
public sealed class VmSession : IDisposable {
  private const string ReplFileName = "<repl>";
  private readonly IVmHost _host;
  private readonly DiagnosticRenderOptions _diagOptions;
  private readonly long _maxSteps;
  private readonly bool _verifyRc;

  private readonly List<string> _committedDeclarations = [];
  private readonly Dictionary<string, IrGlobalId> _existingGlobals = new(StringComparer.Ordinal);
  private readonly Dictionary<string, IrFuncId> _existingFunctions = new(StringComparer.Ordinal);
  private readonly Dictionary<string, IrFuncId> _existingMethods = new(StringComparer.Ordinal);

  private IrModule _module;
  private PinoVm _vm;
  private int _committedItemCount;
  private int _chunkCounter;
  private bool _shutdown;

  public VmSession(
      IVmHost host,
      DiagnosticRenderOptions? diagnosticOptions = null,
      long maxSteps = 0,
      bool verifyRc = false) {
    ArgumentNullException.ThrowIfNull(host);
    _host = host;
    _diagOptions = diagnosticOptions ?? new DiagnosticRenderOptions { Color = false, Voice = DiagnosticVoice.Pino };
    _maxSteps = maxSteps;
    _verifyRc = verifyRc;
    (_module, _vm) = CreateFreshVm();
  }

  /// <summary>The number of committed chunks executed in the current session.</summary>
  public int ChunkCount => _chunkCounter;

  /// <summary>The underlying live VM instance (for inspecting globals or RC state in tests).</summary>
  public PinoVm Vm => _vm;

  /// <summary>
  /// Drops all live RC-managed globals cleanly via <c>__pino_shutdown()</c> and returns the
  /// shutdown result (including <see cref="PinoVmResult.LeakReport"/> when <c>verifyRc</c> is enabled).
  /// </summary>
  public PinoVmResult Shutdown() {
    if (_shutdown) {
      return new PinoVmResult { ExitCode = 0, Reason = PinoVmStopReason.Completed };
    }

    _shutdown = true;
    return _vm.Shutdown();
  }

  /// <summary>Drops all live globals cleanly and resets the session to an empty state.</summary>
  public void Reset() {
    if (!_shutdown) {
      _ = _vm.Shutdown();
    }

    _committedDeclarations.Clear();
    _existingGlobals.Clear();
    _existingFunctions.Clear();
    _existingMethods.Clear();
    _committedItemCount = 0;
    _chunkCounter = 0;
    _shutdown = false;
    (_module, _vm) = CreateFreshVm();
  }

  public void Dispose() {
    _ = Shutdown();
  }

  /// <summary>
  /// Returns <see langword="true"/> when <paramref name="source"/> has an unclosed
  /// <c>{</c>, <c>(</c>, or <c>[</c> outside comments and string/rune literals.
  /// </summary>
  public static bool NeedsMoreInput(string source) {
    if (string.IsNullOrEmpty(source)) {
      return false;
    }

    int braces = 0;
    int parens = 0;
    int brackets = 0;
    int i = 0;
    int length = source.Length;

    while (i < length) {
      char c = source[i];

      if (c == '/' && i + 1 < length && source[i + 1] == '/') {
        i += 2;
        while (i < length && source[i] != '\n') {
          i++;
        }

        continue;
      }

      if (c == '"' && i + 2 < length && source[i + 1] == '"' && source[i + 2] == '"') {
        i += 3;
        while (i + 2 < length) {
          if (source[i] == '"' && source[i + 1] == '"' && source[i + 2] == '"') {
            i += 3;
            break;
          }

          i++;
        }

        continue;
      }

      if (c == '"') {
        i++;
        while (i < length && source[i] != '"') {
          if (source[i] == '\\' && i + 1 < length) {
            i += 2;
          } else {
            i++;
          }
        }

        if (i < length) {
          i++;
        }

        continue;
      }

      if (c == '\'') {
        i++;
        while (i < length && source[i] != '\'') {
          if (source[i] == '\\' && i + 1 < length) {
            i += 2;
          } else {
            i++;
          }
        }

        if (i < length) {
          i++;
        }

        continue;
      }

      switch (c) {
        case '{':
          braces++;
          break;
        case '}':
          if (braces > 0) {
            braces--;
          }

          break;
        case '(':
          parens++;
          break;
        case ')':
          if (parens > 0) {
            parens--;
          }

          break;
        case '[':
          brackets++;
          break;
        case ']':
          if (brackets > 0) {
            brackets--;
          }

          break;
      }

      i++;
    }

    return braces > 0 || parens > 0 || brackets > 0;
  }

  /// <summary>
  /// Parses, checks, lowers into <c>__repl_chunk_N</c>, RC-processes, verifies, and executes
  /// <paramref name="input"/> once against the session's live globals.
  /// Compile errors and runtime panics are written to the host's stderr without corrupting prior state.
  /// </summary>
  public VmSessionResult Submit(string input) {
    ArgumentNullException.ThrowIfNull(input);

    string normalized = input.ReplaceLineEndings("\n");
    if (string.IsNullOrWhiteSpace(normalized)) {
      return new VmSessionResult { Succeeded = true, ExitCode = 0 };
    }

    if (_shutdown) {
      Reset();
    }

    string prefix = _committedDeclarations.Count == 0
        ? string.Empty
        : string.Join("\n", _committedDeclarations) + "\n";
    int chunkStartOffset = prefix.Length;
    string checkCode = prefix + normalized;
    var checkSource = new SourceText(checkCode, ReplFileName);
    ParseResult checkParsed = Parser.Parse(checkSource);
    Compilation checkCompilation = Compilation.Create(checkParsed, FileSystemModuleSource.Instance);

    List<Diagnostic> newDiagnostics = CollectChunkDiagnostics(checkCompilation.RootModel.Diagnostics, chunkStartOffset);
    bool hasErrors = false;
    foreach (Diagnostic diag in checkCompilation.RootModel.Diagnostics) {
      if (diag.Severity == DiagnosticSeverity.Error) {
        hasErrors = true;
        if (!newDiagnostics.Contains(diag)) {
          newDiagnostics.Add(diag);
        }
      }
    }

    string? renderedDiagnostics = newDiagnostics.Count > 0
        ? RenderChunkDiagnostics(newDiagnostics, checkSource, normalized, chunkStartOffset)
        : null;

    if (renderedDiagnostics is not null) {
      _host.Err.Write(renderedDiagnostics);
      _host.Err.Flush();
    }

    if (hasErrors) {
      return new VmSessionResult {
        Succeeded = false,
        ExitCode = 1,
        RenderedDiagnostics = renderedDiagnostics,
      };
    }

    bool shouldAutoPrint = ShouldAutoPrintExpression(normalized, checkCompilation, chunkStartOffset);
    Compilation execCompilation = checkCompilation;
    SourceText execSource = checkSource;
    bool printedValue = false;

    if (shouldAutoPrint) {
      string wrapped = "println(\n" + normalized + "\n)";
      string wrappedCode = prefix + wrapped;
      var wrappedSource = new SourceText(wrappedCode, ReplFileName);
      ParseResult wrappedParsed = Parser.Parse(wrappedSource);
      Compilation wrappedCompilation = Compilation.Create(wrappedParsed, FileSystemModuleSource.Instance);
      if (!HasErrors(wrappedCompilation.RootModel.Diagnostics)) {
        execCompilation = wrappedCompilation;
        execSource = wrappedSource;
        printedValue = true;
      }
    }

    int oldGlobalCount = _module.Globals.Count;
    int oldFuncCount = _module.Functions.Count;
    int nextChunkNumber = _chunkCounter + 1;

    IrFuncId chunkFuncId = IrLowerer.LowerReplChunk(
        execCompilation,
        _module,
        _committedItemCount,
        nextChunkNumber,
        _existingGlobals,
        _existingFunctions,
        _existingMethods);

    RcInsertPass.RunIncremental(_module, oldFuncCount, chunkFuncId, _existingGlobals.Values);

    IrVerificationResult verification = IrVerifier.VerifyAfterRc(_module);
    if (!verification.IsValid) {
      RollbackFailedChunk(oldGlobalCount, oldFuncCount);
      string err = "pino: the REPL chunk could not be run because its IR failed verification: "
          + string.Join("; ", verification.Errors.Take(3)) + "\n";
      _host.Err.Write(err);
      _host.Err.Flush();
      return new VmSessionResult {
        Succeeded = false,
        ExitCode = 1,
        RenderedDiagnostics = err,
      };
    }

    PinoVmResult vmResult = _vm.RunFunction(chunkFuncId, execSource);
    _host.Out.Flush();
    _host.Err.Flush();

    if (vmResult.ExitCode != 0) {
      _vm.DropGlobalsFrom(oldGlobalCount);
      RollbackFailedChunk(oldGlobalCount, oldFuncCount);
      return new VmSessionResult {
        Succeeded = false,
        ExitCode = vmResult.ExitCode,
        RenderedDiagnostics = renderedDiagnostics,
        PanicMessage = vmResult.PanicMessage,
        Steps = vmResult.Steps,
      };
    }

    _chunkCounter = nextChunkNumber;

    // Record newly declared globals, functions, and methods so subsequent chunks bind to them directly.
    for (int i = oldGlobalCount; i < _module.Globals.Count; i++) {
      IrGlobal g = _module.Globals[i];
      _existingGlobals[g.Name] = g.Id;
    }

    for (int i = oldFuncCount; i < _module.Functions.Count; i++) {
      IrFunction fn = _module.Functions[i];
      if (fn.Id == chunkFuncId || fn.Id == _module.ShutdownFuncId) {
        continue;
      }

      if (fn.Name.Contains("::", StringComparison.Ordinal)) {
        _existingMethods[fn.Name] = fn.Id;
      } else if (!fn.Name.StartsWith("__", StringComparison.Ordinal)) {
        _existingFunctions[fn.Name] = fn.Id;
      }
    }

    // Only keep chunks that introduce declarations (variables, functions, types, imports) in the checking prefix.
    if (!printedValue && ContainsTopLevelDeclaration(checkCompilation.RootTree.Root.Items, _committedItemCount)) {
      _committedDeclarations.Add(normalized);
      _committedItemCount = checkCompilation.RootTree.Root.Items.Length;
    }

    return new VmSessionResult {
      Succeeded = true,
      ExitCode = 0,
      RenderedDiagnostics = renderedDiagnostics,
      PrintedValue = printedValue,
      Steps = vmResult.Steps,
    };
  }

  private (IrModule Module, PinoVm Vm) CreateFreshVm() {
    var types = new IrTypeTable { MatchNominalTypesByName = true };
    var module = new IrModule(types, isProgramMode: false);
    var vm = new PinoVm(
        module,
        _host,
        new PinoVmOptions {
          VerifyRc = _verifyRc,
          MaxSteps = _maxSteps,
        });
    return (module, vm);
  }

  private void RollbackFailedChunk(int oldGlobalCount, int oldFuncCount) {
    if (_module.Globals.Count > oldGlobalCount) {
      _module.Globals.RemoveRange(oldGlobalCount, _module.Globals.Count - oldGlobalCount);
      RcInsertPass.RefreshShutdown(_module);
      _vm.SyncWithModule();
    }

    _ = oldFuncCount;
  }

  private static bool ContainsTopLevelDeclaration(ImmutableArray<SyntaxNode> items, int startIndex) {
    for (int i = startIndex; i < items.Length; i++) {
      if (items[i] is VariableDeclarationSyntax
          or TupleDestructuringStatementSyntax
          or FunctionDeclarationSyntax
          or StructDeclarationSyntax
          or EnumDeclarationSyntax
          or UnionDeclarationSyntax
          or InterfaceDeclarationSyntax
          or ImportStatementSyntax
          or FromImportStatementSyntax) {
        return true;
      }
    }

    return false;
  }

  private static bool ShouldAutoPrintExpression(string normalizedInput, Compilation compilation, int chunkStartOffset) {
    var snippetParsed = Parser.Parse(new SourceText(normalizedInput, ReplFileName));
    if (snippetParsed.Root.Items.Length != 1 || snippetParsed.Root.Items[0] is not ExpressionStatementSyntax) {
      return false;
    }

    ImmutableArray<SyntaxNode> items = compilation.RootTree.Root.Items;
    if (items.Length == 0) {
      return false;
    }

    SyntaxNode lastItem = items[^1];
    if (lastItem.Span.Start < chunkStartOffset
        || lastItem is not ExpressionStatementSyntax exprStmt
        || exprStmt.Expression is AssignmentExpressionSyntax) {
      return false;
    }

    TypeSymbol? type = compilation.RootModel.GetType(exprStmt.Expression);
    return type is not null
        && !type.IsVoid
        && !type.IsNever
        && !type.IsError
        && type is not TupleType;
  }

  private static List<Diagnostic> CollectChunkDiagnostics(ImmutableArray<Diagnostic> diagnostics, int chunkStartOffset) {
    var result = new List<Diagnostic>();
    foreach (Diagnostic diag in diagnostics) {
      if (diag.Span.Start >= chunkStartOffset) {
        result.Add(diag);
      }
    }

    return result;
  }

  private static bool HasErrors(ImmutableArray<Diagnostic> diagnostics) {
    foreach (Diagnostic diag in diagnostics) {
      if (diag.Severity == DiagnosticSeverity.Error) {
        return true;
      }
    }

    return false;
  }

  private string RenderChunkDiagnostics(
      List<Diagnostic> diagnostics,
      SourceText combinedSource,
      string normalizedInput,
      int chunkStartOffset) {
    if (chunkStartOffset == 0) {
      return DiagnosticRenderer.RenderAll(diagnostics, combinedSource, _diagOptions);
    }

    bool allInChunk = true;
    foreach (Diagnostic diag in diagnostics) {
      if (diag.Span.Start < chunkStartOffset) {
        allInChunk = false;
        break;
      }

      foreach (RelatedLocation related in diag.Related) {
        if ((related.FilePath is null || string.Equals(related.FilePath, ReplFileName, StringComparison.Ordinal))
            && related.Span.Start < chunkStartOffset) {
          allInChunk = false;
          break;
        }
      }

      if (!allInChunk) {
        break;
      }
    }

    if (!allInChunk) {
      return DiagnosticRenderer.RenderAll(diagnostics, combinedSource, _diagOptions);
    }

    var shifted = new List<Diagnostic>(diagnostics.Count);
    foreach (Diagnostic diag in diagnostics) {
      var shiftedRelated = ImmutableArray.CreateBuilder<RelatedLocation>(diag.Related.Length);
      foreach (RelatedLocation related in diag.Related) {
        if (related.FilePath is null || string.Equals(related.FilePath, ReplFileName, StringComparison.Ordinal)) {
          shiftedRelated.Add(related with {
            Span = new SourceSpan(related.Span.Start - chunkStartOffset, related.Span.Length),
          });
        } else {
          shiftedRelated.Add(related);
        }
      }

      shifted.Add(diag with {
        Span = new SourceSpan(diag.Span.Start - chunkStartOffset, diag.Span.Length),
        Related = shiftedRelated.MoveToImmutable(),
      });
    }

    return DiagnosticRenderer.RenderAll(shifted, new SourceText(normalizedInput, ReplFileName), _diagOptions);
  }
}
