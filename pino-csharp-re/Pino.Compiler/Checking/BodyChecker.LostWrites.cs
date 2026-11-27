using System.Collections.Immutable;

using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

internal sealed partial class BodyChecker {
  private sealed class LostWriteCandidate(
      VariableDeclarationSyntax declaration,
      LocalSymbol local,
      ExpressionSyntax sourceExpression) {
    public VariableDeclarationSyntax Declaration { get; } = declaration;
    public LocalSymbol Local { get; } = local;
    public ExpressionSyntax SourceExpression { get; } = sourceExpression;
    public SyntaxNode? FirstMutationNode { get; set; }
    public SourceSpan? FirstMutationSpan { get; set; }
    public int FirstReassignStart { get; set; } = int.MaxValue;
    public List<SyntaxNode> Escapes { get; } = [];
    public bool EscapesForever { get; set; }
  }

  /// <summary>
  /// Emits <c>PN3552</c> warnings when a mutable local (<c>var</c> or <c>val mut</c>) copied from a
  /// field or index expression is mutated in place and never written back or allowed to escape.
  /// </summary>
  private void CheckLostWrites() {
    var capturedSymbols = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
    foreach (CaptureList? captureList in _state.CapturesByCallableId) {
      if (captureList is null) {
        continue;
      }

      foreach (CaptureEntry entry in captureList.Entries) {
        capturedSymbols.Add(entry.Symbol);
      }
    }

    var orderedCandidates = new List<LostWriteCandidate>();
    var candidatesBySymbol = new Dictionary<Symbol, LostWriteCandidate>(ReferenceEqualityComparer.Instance);
    CollectLostWriteEvents(
        _state.Tree.Root,
        callableDepth: 0,
        capturedSymbols,
        orderedCandidates,
        candidatesBySymbol);

    foreach (LostWriteCandidate candidate in orderedCandidates) {
      if (candidate.FirstMutationSpan is not { } mutSpan || candidate.FirstMutationNode is not { } mutNode) {
        continue;
      }

      if (candidate.EscapesForever || candidate.FirstReassignStart < mutSpan.Start) {
        continue;
      }

      if (HasEscapeAfterOrInLoop(candidate.Escapes, mutNode, mutSpan)) {
        continue;
      }

      string localName = candidate.Local.Name;
      string sourceSnippet = FormatCopySourceSnippet(candidate.SourceExpression);
      ImmutableArray<RelatedLocation> related = [
        new RelatedLocation(
            candidate.Declaration.NameToken.Span,
            $"'{localName}' is copied from '{sourceSnippet}' here",
            _state.FilePath),
      ];
      _rootDiagnostics.ReportWarning(
          DiagnosticCodes.LostWrite,
          mutSpan,
          $"Changes to '{localName}' do not update '{sourceSnippet}', because '{localName}' is a separate copy.",
          related,
          help: $"mutate '{sourceSnippet}' directly, assign '{localName}' back to '{sourceSnippet}', or pass it to a 'mut' function");
    }
  }

  private void CollectLostWriteEvents(
      SyntaxNode node,
      int callableDepth,
      HashSet<Symbol> capturedSymbols,
      List<LostWriteCandidate> orderedCandidates,
      Dictionary<Symbol, LostWriteCandidate> candidatesBySymbol) {
    if (node is VariableDeclarationSyntax varDecl
        && (uint) varDecl.Id < (uint) _state.DeclaredSymbols.Length
        && _state.DeclaredSymbols[varDecl.Id] is LocalSymbol { Kind: LocalKind.Variable, IsMutable: true } local
        && IsMutableCopyValueType(local.Type)
        && TryGetCopySourceExpression(varDecl.Initializer, out ExpressionSyntax? sourceExpr)) {
      var candidate = new LostWriteCandidate(varDecl, local, sourceExpr);
      if (capturedSymbols.Contains(local) || local.Visibility != SymbolVisibility.Private) {
        candidate.EscapesForever = true;
      }
      orderedCandidates.Add(candidate);
      candidatesBySymbol[local] = candidate;
    }

    if ((uint) node.Id < (uint) _state.MutationPlaces.Length
        && _state.MutationPlaces[node.Id] is { } mutInfo
        && candidatesBySymbol.TryGetValue(mutInfo.RootSymbol, out LostWriteCandidate? mutCandidate)) {
      if (mutInfo.Kind == MutationKind.DirectAssign) {
        if (node.Span.Start < mutCandidate.FirstReassignStart) {
          mutCandidate.FirstReassignStart = node.Span.Start;
        }
      } else if (mutInfo.Kind is MutationKind.FieldWrite
          or MutationKind.IndexWrite
          or MutationKind.MutatingCall
          or MutationKind.MutArgument) {
        SourceSpan mutSpan = node is ArgumentSyntax arg ? ArgumentSpanWithoutComma(arg) : node.Span;
        if (mutCandidate.FirstMutationSpan is null || mutSpan.Start < mutCandidate.FirstMutationSpan.Value.Start) {
          mutCandidate.FirstMutationSpan = mutSpan;
          mutCandidate.FirstMutationNode = node;
        }
      }
    }

    switch (node) {
      case IdentifierExpressionSyntax id: {
          if ((uint) id.Id < (uint) _state.ReferencedSymbols.Length
              && _state.ReferencedSymbols[id.Id] is { } sym
              && candidatesBySymbol.TryGetValue(sym, out LostWriteCandidate? candidate)) {
            if (callableDepth > 0 && sym.ContainingSymbol is ModuleSymbol) {
              candidate.EscapesForever = true;
            } else if (IsEscapingExpressionContext(id)) {
              candidate.Escapes.Add(id);
            }
          }
          break;
        }

      case StructFieldInitSyntax fieldInit
          when fieldInit.Value is null
              && fieldInit.ColonToken is null
              && (uint) fieldInit.Id < (uint) _state.ShorthandValueSymbols.Length
              && _state.ShorthandValueSymbols[fieldInit.Id] is { } shorthandSym
              && candidatesBySymbol.TryGetValue(shorthandSym, out LostWriteCandidate? structCandidate):
        structCandidate.Escapes.Add(fieldInit);
        break;

      case TupleFieldSyntax tupleField
          when tupleField.Value is null
              && tupleField.ColonToken is null
              && (uint) tupleField.Id < (uint) _state.ReferencedSymbols.Length
              && _state.ReferencedSymbols[tupleField.Id] is { } tupleSym
              && candidatesBySymbol.TryGetValue(tupleSym, out LostWriteCandidate? tupleCandidate):
        tupleCandidate.Escapes.Add(tupleField);
        break;
    }

    int childCallableDepth = node is FunctionDeclarationSyntax
        or MethodDeclarationSyntax
        or TestDeclarationSyntax
        or LambdaExpressionSyntax
        ? callableDepth + 1
        : callableDepth;

    foreach (SyntaxElement elem in node.GetChildren()) {
      if (elem.IsNode) {
        CollectLostWriteEvents(elem.AsNode(), childCallableDepth, capturedSymbols, orderedCandidates, candidatesBySymbol);
      }
    }
  }

  private static bool IsMutableCopyValueType(TypeSymbol type) =>
      type is StructType or VectorType or MapType or SetType;

  private bool TryGetCopySourceExpression(ExpressionSyntax initializer, out ExpressionSyntax sourceExpr) {
    ExpressionSyntax unwrapped = UnwrapParens(initializer);
    if (unwrapped is MemberAccessExpressionSyntax member) {
      Symbol? referenced = (uint) member.Id < (uint) _state.ReferencedSymbols.Length
          ? _state.ReferencedSymbols[member.Id]
          : null;
      if (referenced is FieldSymbol) {
        sourceExpr = member;
        return true;
      }

      TypeSymbol? targetType = GetRecordedNodeType(member.Target);
      if (referenced is null && targetType is InterfaceType or TypeParameterType) {
        sourceExpr = member;
        return true;
      }
    } else if (unwrapped is IndexExpressionSyntax index) {
      TypeSymbol? targetType = GetRecordedNodeType(index.Target);
      if (targetType is VectorType or MapType or PoolType) {
        sourceExpr = index;
        return true;
      }
    }

    sourceExpr = initializer;
    return false;
  }

  private SyntaxNode? ParentOf(SyntaxNode node) =>
      (uint) node.Id < (uint) _state.ParentsByNodeId.Length
          ? _state.ParentsByNodeId[node.Id]
          : null;

  private bool IsEscapingExpressionContext(SyntaxNode expr) {
    SyntaxNode current = expr;
    SyntaxNode? parent = ParentOf(current);
    while (parent is not null) {
      switch (parent) {
        case ParenthesizedExpressionSyntax:
          current = parent;
          parent = ParentOf(current);
          continue;

        case IfExpressionSyntax ifExpr
            when ReferenceEquals(current, ifExpr.ThenBranch) || ReferenceEquals(current, ifExpr.ElseBranch):
          current = parent;
          parent = ParentOf(current);
          continue;

        case AssignmentExpressionSyntax assign when ReferenceEquals(current, assign.Value):
        case VariableDeclarationSyntax varDecl when ReferenceEquals(current, varDecl.Initializer):
        case ReturnStatementSyntax retStmt when ReferenceEquals(current, retStmt.Expression):
        case YieldStatementSyntax yieldStmt when ReferenceEquals(current, yieldStmt.Expression):
        case StructFieldInitSyntax fieldInit when ReferenceEquals(current, fieldInit.Value):
        case TupleFieldSyntax tupleField when ReferenceEquals(current, tupleField.Value):
        case VectorLiteralExpressionSyntax:
        case SetLiteralExpressionSyntax:
        case MapEntrySyntax:
          return true;

        case ArgumentSyntax arg when ReferenceEquals(current, arg.Expression):
          return arg.MutKeyword is null;

        case VectorPropertySyntax vecProp when ReferenceEquals(current, vecProp.Value):
          return true;

        case LambdaExpressionSyntax lambda when ReferenceEquals(current, lambda.BodyExpression):
          return true;

        default:
          return false;
      }
    }

    return false;
  }

  private bool HasEscapeAfterOrInLoop(List<SyntaxNode> escapes, SyntaxNode mutationNode, SourceSpan mutationSpan) {
    if (escapes.Count == 0) {
      return false;
    }

    ForStatementSyntax? innermostMutLoop = FindEnclosingLoop(mutationNode);
    foreach (SyntaxNode escape in escapes) {
      if (escape.Span.Start > mutationSpan.Start) {
        return true;
      }

      if (innermostMutLoop is not null && SharesEnclosingLoop(escape, innermostMutLoop)) {
        return true;
      }
    }

    return false;
  }

  private ForStatementSyntax? FindEnclosingLoop(SyntaxNode node) {
    for (SyntaxNode? cur = ParentOf(node); cur is not null; cur = ParentOf(cur)) {
      if (cur is FunctionDeclarationSyntax or MethodDeclarationSyntax or TestDeclarationSyntax or LambdaExpressionSyntax) {
        return null;
      }

      if (cur is ForStatementSyntax loop) {
        return loop;
      }
    }

    return null;
  }

  private bool SharesEnclosingLoop(SyntaxNode escape, ForStatementSyntax targetLoop) {
    for (SyntaxNode? cur = ParentOf(escape); cur is not null; cur = ParentOf(cur)) {
      if (ReferenceEquals(cur, targetLoop)) {
        return true;
      }

      if (cur is FunctionDeclarationSyntax or MethodDeclarationSyntax or TestDeclarationSyntax or LambdaExpressionSyntax) {
        return false;
      }
    }

    return false;
  }

  private string FormatCopySourceSnippet(ExpressionSyntax sourceExpr) {
    SourceSpan span = sourceExpr.Span;
    if (span.Length <= 0 || span.End > _state.Tree.Source.Length) {
      return "the original value";
    }

    ReadOnlySpan<char> text = _state.Tree.Source.AsSpan(span);
    if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0 || text.Length > 60) {
      return "the original value";
    }

    return text.ToString();
  }
}
