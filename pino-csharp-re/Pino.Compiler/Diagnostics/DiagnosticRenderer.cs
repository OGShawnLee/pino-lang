using System.Globalization;
using System.Text;

using Pino.Compiler.Text;

namespace Pino.Compiler.Diagnostics;

public static class DiagnosticRenderer {
  private const string Reset = "\x1b[0m";
  private const string Bold = "\x1b[1m";
  private const string BlueBold = "\x1b[1;34m";
  private const string CyanBold = "\x1b[1;36m";
  private const string MagentaBold = "\x1b[1;35m";
  private const string RedBold = "\x1b[1;31m";
  private const string YellowBold = "\x1b[1;33m";
  private const string GreenBold = "\x1b[1;32m";

  public static string Render(
      Diagnostic diagnostic,
      SourceText source,
      DiagnosticRenderOptions? options = null) {
    ArgumentNullException.ThrowIfNull(diagnostic);
    ArgumentNullException.ThrowIfNull(source);

    options ??= new DiagnosticRenderOptions();
    var sb = new StringBuilder();
    RenderInto(sb, diagnostic, source, options);
    return sb.ToString();
  }

  public static string RenderAll(
      IEnumerable<Diagnostic> diagnostics,
      SourceText source,
      DiagnosticRenderOptions? options = null) {
    ArgumentNullException.ThrowIfNull(diagnostics);
    ArgumentNullException.ThrowIfNull(source);

    options ??= new DiagnosticRenderOptions();
    var sb = new StringBuilder();
    bool first = true;

    foreach (Diagnostic diagnostic in diagnostics
        .OrderBy(static d => d.Span.Start)
        .ThenBy(static d => d.Code, StringComparer.Ordinal)) {
      if (!first) {
        sb.Append('\n');
      }

      first = false;
      RenderInto(sb, diagnostic, source, options);
    }

    return sb.ToString();
  }

  private static void RenderInto(
      StringBuilder sb,
      Diagnostic diagnostic,
      SourceText source,
      DiagnosticRenderOptions options) {
    bool color = options.Color;
    int tabWidth = Math.Max(1, options.TabWidth);
    int maxLineWidth = Math.Max(1, options.MaxLineWidth);

    string severityText = GetSeverityText(diagnostic.Severity);
    string severityColor = GetSeverityColor(diagnostic.Severity);

    // 1. Header: <severity>[<code>]: <message>
    if (color) {
      sb.Append(severityColor).Append(severityText).Append('[').Append(diagnostic.Code).Append(']').Append(Reset);
      sb.Append(": ");
      sb.Append(Bold).Append(diagnostic.Message).Append(Reset).Append('\n');
    } else {
      sb.Append(severityText).Append('[').Append(diagnostic.Code).Append("]: ").Append(diagnostic.Message).Append('\n');
    }

    int primaryStartOffset = Math.Clamp(diagnostic.Span.Start, 0, source.Length);
    int primaryEndOffset = Math.Clamp(diagnostic.Span.End, primaryStartOffset, source.Length);
    LinePosition primaryStartPos = source.GetLineColumn(primaryStartOffset);
    LinePosition primaryEndPos = primaryEndOffset == primaryStartOffset
        ? primaryStartPos
        : source.GetLineColumn(primaryEndOffset);

    var lineEntries = new SortedDictionary<int, LineEntry>();
    if (primaryStartPos.Line == primaryEndPos.Line) {
      GetOrCreateEntry(lineEntries, primaryStartPos.Line).PrimaryMark = new RawMark(
          primaryStartPos.Column - 1,
          primaryEndPos.Column - 1,
          IsEndOfMultiline: false);
    } else {
      GetOrCreateEntry(lineEntries, primaryStartPos.Line).PrimaryMark = new RawMark(
          primaryStartPos.Column - 1,
          int.MaxValue,
          IsEndOfMultiline: false);
      GetOrCreateEntry(lineEntries, primaryEndPos.Line).PrimaryMark = new RawMark(
          0,
          primaryEndPos.Column - 1,
          IsEndOfMultiline: true);
    }

    var otherFileNotes = new List<RelatedLocation>();
    foreach (RelatedLocation related in diagnostic.Related) {
      if (related.FilePath is null || string.Equals(related.FilePath, source.FilePath, StringComparison.Ordinal)) {
        int relStartOffset = Math.Clamp(related.Span.Start, 0, source.Length);
        int relEndOffset = Math.Clamp(related.Span.End, relStartOffset, source.Length);
        LinePosition relStartPos = source.GetLineColumn(relStartOffset);
        LinePosition relEndPos = relEndOffset == relStartOffset
            ? relStartPos
            : source.GetLineColumn(relEndOffset);
        int endCol = relEndPos.Line == relStartPos.Line ? relEndPos.Column - 1 : int.MaxValue;
        GetOrCreateEntry(lineEntries, relStartPos.Line).RelatedMarks.Add(
            new RelatedRawMark(relStartPos.Column - 1, endCol, related.Message));
      } else {
        otherFileNotes.Add(related);
      }
    }

    int maxLineNumber = lineEntries.Keys.Max();
    int gutterWidth = CountDigits(maxLineNumber);
    string gutter = new(' ', gutterWidth);
    string filePath = source.FilePath ?? "<input>";

    // 2. Location: G--> <path>:<line>:<column>
    sb.Append(gutter);
    if (color) {
      sb.Append(BlueBold).Append("-->").Append(Reset);
    } else {
      sb.Append("-->");
    }

    sb.Append(' ').Append(filePath).Append(':').Append(primaryStartPos.Line).Append(':').Append(primaryStartPos.Column).Append('\n');

    // 3. Snippet: empty gutter line G |
    AppendGutterPipe(sb, gutter, color);
    sb.Append('\n');

    int previousLine = 0;
    foreach ((int lineNumber, LineEntry entry) in lineEntries) {
      if (previousLine != 0 && lineNumber > previousLine + 1) {
        if (color) {
          sb.Append(BlueBold).Append("...").Append(Reset).Append('\n');
        } else {
          sb.Append("...\n");
        }
      }

      RenderSourceLine(sb, source, lineNumber, entry, gutterWidth, gutter, tabWidth, maxLineWidth, color, severityColor);
      previousLine = lineNumber;
    }

    // 4. Related locations in another file
    foreach (RelatedLocation other in otherFileNotes) {
      AppendEqualsPrefix(sb, gutter, color);
      sb.Append("note: ").Append(other.Message).Append(" (in ").Append(other.FilePath).Append(")\n");
    }

    // 5. Help
    if (diagnostic.Help is not null) {
      AppendEqualsPrefix(sb, gutter, color);
      if (color) {
        sb.Append(CyanBold).Append("help:").Append(Reset);
      } else {
        sb.Append("help:");
      }

      sb.Append(' ').Append(diagnostic.Help).Append('\n');
    }

    // 6. Pino
    if (options.Voice == DiagnosticVoice.Pino) {
      IReadOnlyDictionary<string, string> notes = options.Notes ?? PinoVoice.Notes;
      if (notes.TryGetValue(diagnostic.Code, out string? pinoNote)) {
        AppendEqualsPrefix(sb, gutter, color);
        if (color) {
          sb.Append(MagentaBold).Append("Pino:").Append(Reset);
        } else {
          sb.Append("Pino:");
        }

        sb.Append(' ').Append(pinoNote).Append('\n');
      }
    }
  }

  private static void RenderSourceLine(
      StringBuilder sb,
      SourceText source,
      int lineNumber,
      LineEntry entry,
      int gutterWidth,
      string gutter,
      int tabWidth,
      int maxLineWidth,
      bool color,
      string severityColor) {
    ReadOnlySpan<char> rawLine = GetLineText(source, lineNumber);
    NormalizedLine normalized = NormalizeLine(rawLine, tabWidth);

    DisplayMark? primaryDisplayMark = null;
    if (entry.PrimaryMark is { } rawPrimary) {
      int startUtf16 = Math.Clamp(rawPrimary.StartUtf16Col, 0, rawLine.Length);
      int endUtf16 = Math.Clamp(rawPrimary.EndUtf16Col, 0, rawLine.Length);
      if (rawPrimary.IsEndOfMultiline) {
        int firstNonWs = FindFirstNonWhitespaceIndex(rawLine);
        startUtf16 = Math.Min(firstNonWs, endUtf16);
      }

      int startDisplayCol = normalized.Utf16ToDisplayCol[startUtf16];
      int endDisplayCol = normalized.Utf16ToDisplayCol[Math.Max(startUtf16, endUtf16)];
      int width = Math.Max(1, endDisplayCol - startDisplayCol);
      primaryDisplayMark = new DisplayMark(startDisplayCol, width);
    }

    var relatedDisplayMarks = new List<(DisplayMark Mark, string Message)>(entry.RelatedMarks.Count);
    foreach (RelatedRawMark rel in entry.RelatedMarks) {
      int startUtf16 = Math.Clamp(rel.StartUtf16Col, 0, rawLine.Length);
      int endUtf16 = Math.Clamp(rel.EndUtf16Col, startUtf16, rawLine.Length);
      int startDisplayCol = normalized.Utf16ToDisplayCol[startUtf16];
      int endDisplayCol = normalized.Utf16ToDisplayCol[endUtf16];
      int width = Math.Max(1, endDisplayCol - startDisplayCol);
      relatedDisplayMarks.Add((new DisplayMark(startDisplayCol, width), rel.Message));
    }

    int windowStart = 0;
    int windowEnd = normalized.TotalWidth;
    bool hasPrefix = false;
    bool hasSuffix = false;

    if (normalized.TotalWidth > maxLineWidth) {
      int anchorCol = primaryDisplayMark?.StartCol
          ?? (relatedDisplayMarks.Count > 0 ? relatedDisplayMarks[0].Mark.StartCol : 0);
      windowStart = Math.Max(0, anchorCol - (maxLineWidth / 2));
      if (windowStart + maxLineWidth > normalized.TotalWidth) {
        windowStart = Math.Max(0, normalized.TotalWidth - maxLineWidth);
      }

      windowEnd = Math.Min(normalized.TotalWidth, windowStart + maxLineWidth);
      hasPrefix = windowStart > 0;
      hasSuffix = windowEnd < normalized.TotalWidth;
    }

    string lineNumStr = lineNumber.ToString(CultureInfo.InvariantCulture).PadLeft(gutterWidth);
    if (color) {
      sb.Append(BlueBold).Append(lineNumStr).Append(" |").Append(Reset).Append(' ');
    } else {
      sb.Append(lineNumStr).Append(" | ");
    }

    if (hasPrefix) {
      AppendEllipsis(sb, color);
    }

    AppendWindowedText(sb, normalized.Segments, windowStart, windowEnd);

    if (hasSuffix) {
      AppendEllipsis(sb, color);
    }

    sb.Append('\n');

    int prefixShift = hasPrefix ? 3 : 0;
    int markClipEnd = hasSuffix ? windowEnd : int.MaxValue;

    if (primaryDisplayMark is { } pMark) {
      AppendMarkLine(
          sb,
          gutter,
          pMark,
          windowStart,
          markClipEnd,
          prefixShift,
          '^',
          label: null,
          color,
          severityColor);
    }

    foreach ((DisplayMark rMark, string message) in relatedDisplayMarks) {
      AppendMarkLine(
          sb,
          gutter,
          rMark,
          windowStart,
          markClipEnd,
          prefixShift,
          '-',
          message,
          color,
          BlueBold);
    }
  }

  private static void AppendMarkLine(
      StringBuilder sb,
      string gutter,
      DisplayMark mark,
      int windowStart,
      int windowEnd,
      int prefixShift,
      char markChar,
      string? label,
      bool color,
      string markColor) {
    int markStart = mark.StartCol;
    int markEnd = mark.StartCol + mark.Width;
    int clippedStart = Math.Max(markStart, windowStart);
    int clippedEnd = Math.Min(markEnd, windowEnd);
    if (clippedStart >= clippedEnd) {
      return;
    }

    int spaces = (clippedStart - windowStart) + prefixShift;
    int count = clippedEnd - clippedStart;

    AppendGutterPipe(sb, gutter, color);
    sb.Append(' ');
    sb.Append(' ', spaces);

    if (color) {
      sb.Append(markColor);
      sb.Append(markChar, count);
      if (!string.IsNullOrEmpty(label)) {
        sb.Append(' ').Append(label);
      }

      sb.Append(Reset);
    } else {
      sb.Append(markChar, count);
      if (!string.IsNullOrEmpty(label)) {
        sb.Append(' ').Append(label);
      }
    }

    sb.Append('\n');
  }

  private static void AppendWindowedText(
      StringBuilder sb,
      List<DisplaySegment> segments,
      int windowStart,
      int windowEnd) {
    foreach (DisplaySegment seg in segments) {
      int segStart = seg.DisplayCol;
      int segEnd = seg.DisplayCol + seg.Width;
      if (segEnd <= windowStart) {
        continue;
      }

      if (segStart >= windowEnd) {
        break;
      }

      if (segStart < windowStart || segEnd > windowEnd) {
        int overlapStart = Math.Max(segStart, windowStart);
        int overlapEnd = Math.Min(segEnd, windowEnd);
        sb.Append(' ', overlapEnd - overlapStart);
      } else {
        sb.Append(seg.Text);
      }
    }
  }

  private static void AppendGutterPipe(StringBuilder sb, string gutter, bool color) {
    sb.Append(gutter).Append(' ');
    if (color) {
      sb.Append(BlueBold).Append('|').Append(Reset);
    } else {
      sb.Append('|');
    }
  }

  private static void AppendEqualsPrefix(StringBuilder sb, string gutter, bool color) {
    sb.Append(gutter).Append(' ');
    if (color) {
      sb.Append(BlueBold).Append('=').Append(Reset);
    } else {
      sb.Append('=');
    }

    sb.Append(' ');
  }

  private static void AppendEllipsis(StringBuilder sb, bool color) {
    if (color) {
      sb.Append(BlueBold).Append("...").Append(Reset);
    } else {
      sb.Append("...");
    }
  }

  private static ReadOnlySpan<char> GetLineText(SourceText source, int lineNumber) {
    int lineStart = source.GetOffset(new LinePosition(lineNumber, 1));
    int lineEnd;
    if (lineNumber < source.LineCount) {
      lineEnd = source.GetOffset(new LinePosition(lineNumber + 1, 1));
      if (lineEnd > lineStart && source.Text[lineEnd - 1] == '\n') {
        lineEnd--;
      }

      if (lineEnd > lineStart && source.Text[lineEnd - 1] == '\r') {
        lineEnd--;
      }
    } else {
      lineEnd = source.Length;
    }

    return source.AsSpan(SourceSpan.FromBounds(lineStart, lineEnd));
  }

  private static NormalizedLine NormalizeLine(ReadOnlySpan<char> rawLine, int tabWidth) {
    int[] colMap = new int[rawLine.Length + 1];
    var segments = new List<DisplaySegment>(rawLine.Length);
    int displayCol = 0;
    int i = 0;

    while (i < rawLine.Length) {
      colMap[i] = displayCol;
      char ch = rawLine[i];
      if (ch == '\t') {
        int spaces = tabWidth - (displayCol % tabWidth);
        segments.Add(new DisplaySegment(new string(' ', spaces), displayCol, spaces));
        displayCol += spaces;
        i++;
      } else if (ch < 0x20 || ch == 0x7F) {
        segments.Add(new DisplaySegment("\uFFFD", displayCol, 1));
        displayCol += 1;
        i++;
      } else {
        Rune.DecodeFromUtf16(rawLine[i..], out Rune rune, out int charsConsumed);
        int width = DisplayWidth.GetWidth(rune);
        if (charsConsumed == 2) {
          colMap[i + 1] = displayCol;
        }

        string text = rawLine.Slice(i, charsConsumed).ToString();
        segments.Add(new DisplaySegment(text, displayCol, width));
        displayCol += width;
        i += charsConsumed;
      }
    }

    colMap[rawLine.Length] = displayCol;
    return new NormalizedLine(segments, colMap, displayCol);
  }

  private static int FindFirstNonWhitespaceIndex(ReadOnlySpan<char> rawLine) {
    for (int i = 0; i < rawLine.Length; i++) {
      if (!char.IsWhiteSpace(rawLine[i])) {
        return i;
      }
    }

    return 0;
  }

  private static LineEntry GetOrCreateEntry(SortedDictionary<int, LineEntry> map, int lineNumber) {
    if (!map.TryGetValue(lineNumber, out LineEntry? entry)) {
      entry = new LineEntry();
      map[lineNumber] = entry;
    }

    return entry;
  }

  private static int CountDigits(int value) {
    int digits = 1;
    while (value >= 10) {
      value /= 10;
      digits++;
    }

    return digits;
  }

  private static string GetSeverityText(DiagnosticSeverity severity) => severity switch {
    DiagnosticSeverity.Error => "error",
    DiagnosticSeverity.Warning => "warning",
    DiagnosticSeverity.Info => "info",
    DiagnosticSeverity.Hint => "hint",
    _ => "error",
  };

  private static string GetSeverityColor(DiagnosticSeverity severity) => severity switch {
    DiagnosticSeverity.Error => RedBold,
    DiagnosticSeverity.Warning => YellowBold,
    DiagnosticSeverity.Info => CyanBold,
    DiagnosticSeverity.Hint => GreenBold,
    _ => RedBold,
  };

  private readonly record struct RawMark(int StartUtf16Col, int EndUtf16Col, bool IsEndOfMultiline);
  private readonly record struct RelatedRawMark(int StartUtf16Col, int EndUtf16Col, string Message);
  private readonly record struct DisplayMark(int StartCol, int Width);
  private readonly record struct DisplaySegment(string Text, int DisplayCol, int Width);

  private sealed class LineEntry {
    public RawMark? PrimaryMark { get; set; }
    public List<RelatedRawMark> RelatedMarks { get; } = [];
  }

  private readonly record struct NormalizedLine(
      List<DisplaySegment> Segments,
      int[] Utf16ToDisplayCol,
      int TotalWidth);
}
