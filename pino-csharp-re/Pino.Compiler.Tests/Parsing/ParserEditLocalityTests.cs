using System.Collections.Immutable;

using Pino.Compiler.Lexing;
using Pino.Compiler.Parsing;
using Pino.Compiler.Syntax;
using Pino.Compiler.Text;

using Xunit.Abstractions;

namespace Pino.Compiler.Tests.Parsing;

/// <summary>Verifies design A3 edit locality across zero-diagnostic corpus files and Parser.pino.</summary>
public sealed class ParserEditLocalityTests(ITestOutputHelper output) {
  private const int EditSeed = 20261120;
  private const int EditsPerKindDefault = 2;
  private const int EditsPerKindParserPino = 15; // 15 * 4 = 60 >= 50

  private enum EditKind {
    DeleteToken,
    InsertOpenBrace,
    InsertOpenParen,
    InsertErrorChar,
  }

  private static readonly string s_repositoryRoot = ParserTestHelpers.FindRepositoryRoot();

  [Fact]
  public void SingleEdit_PreservesAllColumn1DeclarationsFromKOnward() {
    var random = new Random(EditSeed);

    int filesTested = 0;
    int editsApplied = 0;
    int editsSkippedNoK = 0;
    int editsVerified = 0;
    int parserPinoEdits = 0;

    foreach (string relativePath in ParserTestHelpers.GetCorpusFiles()) {
      string fullPath = Path.Combine(s_repositoryRoot, relativePath);
      string originalText = File.ReadAllText(fullPath);
      ParseResult original = Parser.Parse(new SourceText(originalText, relativePath));

      bool isSelfHostParser = string.Equals(
          relativePath,
          "projects/pino-compiler/modules/Parser.pino",
          StringComparison.OrdinalIgnoreCase);

      if (!isSelfHostParser && !original.Diagnostics.IsEmpty) {
        continue;
      }

      filesTested++;
      int perKind = isSelfHostParser ? EditsPerKindParserPino : EditsPerKindDefault;

      List<Token> deletableTokens = CollectDeletableTokens(original.Lex.Tokens);
      List<int> lineInsertOffsets = CollectLineStartInsertOffsets(original.Lex);

      foreach (EditKind kind in Enum.GetValues<EditKind>()) {
        for (int n = 0; n < perKind; n++) {
          if (!TryCreateEdit(
                  kind,
                  originalText,
                  deletableTokens,
                  lineInsertOffsets,
                  random,
                  out string editedText,
                  out int editStart,
                  out int editEnd,
                  out string editDescription)) {
            continue;
          }

          editsApplied++;
          if (isSelfHostParser) {
            parserPinoEdits++;
          }

          int kIndex = FindKIndex(original, editStart, editEnd);
          if (kIndex < 0) {
            editsSkippedNoK++;
            continue;
          }

          ParseResult edited = Parser.Parse(new SourceText(editedText, relativePath));
          ParserTestHelpers.AssertFileInvariants(edited, expectNoDiagnostics: false);

          ImmutableArray<SyntaxNode> origItems = original.Root.Items;
          ImmutableArray<SyntaxNode> editItems = edited.Root.Items;
          int tailCount = origItems.Length - kIndex;

          if (editItems.Length < tailCount) {
            Assert.Fail(
                $"Edit locality failed in '{relativePath}' ({editDescription}, K={origItems[kIndex].GetType().Name}@offset {origItems[kIndex].Span.Start}): "
                + $"edited parse has {editItems.Length} items, fewer than required tail count {tailCount}.");
          }

          for (int offsetFromEnd = 1; offsetFromEnd <= tailCount; offsetFromEnd++) {
            SyntaxNode origItem = origItems[^offsetFromEnd];
            SyntaxNode editItem = editItems[^offsetFromEnd];
            if (!SyntaxEquivalence.AreEquivalent(
                    origItem,
                    original.Lex.Source,
                    editItem,
                    edited.Lex.Source,
                    out string? diff)) {
              (int origLine, int origCol) = original.Lex.Source.GetLineColumn(origItem.Span.Start);
              (int editLine, int editCol) = original.Lex.Source.GetLineColumn(editStart);
              Assert.Fail(
                  $"Edit locality failed in '{relativePath}' at {editLine}:{editCol} ({editDescription}), "
                  + $"K item #{origItems.Length - offsetFromEnd} ({origItem.GetType().Name} at {origLine}:{origCol}): {diff}");
            }
          }

          editsVerified++;
        }
      }
    }

    Assert.True(filesTested > 400, $"Expected >400 eligible files, tested {filesTested}.");
    Assert.True(parserPinoEdits >= 50, $"Expected >=50 edits on Parser.pino, applied {parserPinoEdits}.");
    Assert.True(editsVerified > 0, "Expected at least one verified edit with K.");

    output.WriteLine(
        $"Edit locality: files={filesTested}, applied={editsApplied} (Parser.pino={parserPinoEdits}), skippedNoK={editsSkippedNoK}, verified={editsVerified}.");
  }

  private static List<Token> CollectDeletableTokens(ImmutableArray<Token> tokens) {
    var result = new List<Token>(tokens.Length);
    foreach (Token token in tokens) {
      if (token.Kind is not (TokenKind.EOF
          or TokenKind.StringStart
          or TokenKind.StringEnd
          or TokenKind.InterpStart
          or TokenKind.InterpEnd)) {
        result.Add(token);
      }
    }

    return result;
  }

  private static List<int> CollectLineStartInsertOffsets(LexResult lex) {
    var offsets = new List<int>();
    ImmutableArray<Token> tokens = lex.Tokens;
    for (int i = 0; i < tokens.Length; i++) {
      Token token = tokens[i];
      if (token.Kind == TokenKind.EOF) {
        continue;
      }

      if (i > 0 && (token.Flags & TokenFlags.LeadingNewline) == 0) {
        continue;
      }

      // Keep insertions out of string literals and interpolations.
      if (token.Kind is TokenKind.StringPart
          or TokenKind.StringEnd
          or TokenKind.InterpStart
          or TokenKind.InterpEnd) {
        continue;
      }

      offsets.Add(token.Span.Start);
    }

    return offsets;
  }

  private static bool TryCreateEdit(
      EditKind kind,
      string originalText,
      List<Token> deletableTokens,
      List<int> lineInsertOffsets,
      Random random,
      out string editedText,
      out int editStart,
      out int editEnd,
      out string description) {
    if (kind == EditKind.DeleteToken) {
      if (deletableTokens.Count == 0) {
        editedText = string.Empty;
        editStart = 0;
        editEnd = 0;
        description = string.Empty;
        return false;
      }

      Token target = deletableTokens[random.Next(deletableTokens.Count)];
      editStart = target.Span.Start;
      editEnd = target.Span.End;
      editedText = string.Concat(originalText.AsSpan(0, editStart), originalText.AsSpan(editEnd));
      description = $"delete {target.Kind} '{originalText[editStart..editEnd]}' at {editStart}..{editEnd}";
      return true;
    }

    if (lineInsertOffsets.Count == 0) {
      editedText = string.Empty;
      editStart = 0;
      editEnd = 0;
      description = string.Empty;
      return false;
    }

    int insertOffset = lineInsertOffsets[random.Next(lineInsertOffsets.Count)];
    string inserted = kind switch {
      EditKind.InsertOpenBrace => "{",
      EditKind.InsertOpenParen => "(",
      EditKind.InsertErrorChar => "~",
      _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    editStart = insertOffset;
    editEnd = insertOffset;
    editedText = string.Concat(originalText.AsSpan(0, insertOffset), inserted, originalText.AsSpan(insertOffset));
    description = $"insert '{inserted}' at {insertOffset}";
    return true;
  }

  private static int FindKIndex(ParseResult original, int editStart, int editEnd) {
    ImmutableArray<SyntaxNode> items = original.Root.Items;
    ImmutableArray<Token> tokens = original.Lex.Tokens;

    for (int i = 0; i < items.Length; i++) {
      SyntaxNode item = items[i];
      bool startsAfterEdit = editStart == editEnd
          ? item.Span.Start > editStart
          : item.Span.Start >= editEnd;
      if (!startsAfterEdit) {
        continue;
      }

      if (original.Lex.Source.GetLineColumn(item.Span.Start).Column != 1) {
        continue;
      }

      int tokenIdx = FindFirstTokenAtOrAfter(tokens, item.Span.Start);
      if (tokenIdx < 0) {
        continue;
      }

      if (IsA1TriggerAt(tokens, tokenIdx)) {
        return i;
      }
    }

    return -1;
  }

  private static int FindFirstTokenAtOrAfter(ImmutableArray<Token> tokens, int offset) {
    int lo = 0;
    int hi = tokens.Length;
    while (lo < hi) {
      int mid = lo + ((hi - lo) / 2);
      if (tokens[mid].Span.Start < offset) {
        lo = mid + 1;
      } else {
        hi = mid;
      }
    }

    return lo < tokens.Length ? lo : -1;
  }

  private static bool IsA1TriggerAt(ImmutableArray<Token> tokens, int index) {
    Token first = tokens[index];
    return first.Kind switch {
      TokenKind.FnKeyword => index + 1 < tokens.Length && tokens[index + 1].Kind == TokenKind.Identifier,
      TokenKind.StructKeyword => true,
      TokenKind.UnionKeyword => true,
      TokenKind.EnumKeyword => true,
      TokenKind.InterfaceKeyword => true,
      TokenKind.TestKeyword => true,
      TokenKind.ModuleKeyword => true,
      TokenKind.ImportKeyword => true,
      TokenKind.FromKeyword => true,
      TokenKind.PubKeyword => true,
      TokenKind.At => index + 1 < tokens.Length
          && tokens[index + 1].Kind == TokenKind.At
          && (tokens[index + 1].Flags & (TokenFlags.LeadingWhitespace | TokenFlags.LeadingNewline | TokenFlags.LeadingComment)) == 0
          && first.Span.End == tokens[index + 1].Span.Start,
      _ => false,
    };
  }
}
