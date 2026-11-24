using System.Globalization;
using System.Text;

using Pino.Compiler.IR;
using Pino.Vm.Regex;

namespace Pino.Vm;

/// <summary>The <c>call.rt</c> runtime operations: I/O, parsing, strings, regex, files, time and random numbers.</summary>
public sealed partial class PinoVm {
  private PinoValue ExecRuntime(CompiledFunc func, PinoValue[] regs, CallRuntime call) {
    PinoValue Arg(int index) => Val(func, regs, call.Arguments[index]);
    PinoVmString StrArg(int index) => UseObject((PinoVmString) Arg(index).Obj!, "string access");
    IrTypeId destType = call.Dest is { } dest ? DestType(func, dest) : IrTypeId.Invalid;

    switch (call.Op) {
      case IrRuntimeOp.PrintlnN: {
          var sb = new StringBuilder();
          for (int i = 0; i < call.Arguments.Length; i++) {
            if (i > 0) {
              sb.Append(' ');
            }

            sb.Append(StrArg(i).ToString());
          }

          sb.Append('\n');
          _host.Out.Write(sb.ToString());
          return default;
        }

      case IrRuntimeOp.Eprintln:
        _host.Err.Write(StrArg(0).ToString() + "\n");
        _host.Err.Flush();
        return default;

      case IrRuntimeOp.Exit:
        throw new PinoVmExit((int) Arg(0).Raw);

      case IrRuntimeOp.Readline: {
          _host.Out.Write(StrArg(0).ToString());
          _host.Out.Flush();
          string? line = _host.In.ReadLine();
          return line is null
              ? MakeUnion(destType, "Failure", NewStringValue("There is no more input to read."))
              : MakeUnion(destType, "Success", NewStringValue(line));
        }

      case IrRuntimeOp.ParseInt: {
          string text = StrArg(0).ToString();
          return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
              ? MakeUnion(destType, "Success", PinoValue.FromInt(parsed))
              : MakeUnion(destType, "Failure", NewStringValue($"Cannot convert '{text}' to an int."));
        }

      case IrRuntimeOp.ParseFloat: {
          string text = StrArg(0).ToString();
          return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
              ? MakeUnion(destType, "Success", PinoValue.FromFloat(parsed))
              : MakeUnion(destType, "Failure", NewStringValue($"Cannot convert '{text}' to a float."));
        }

      case IrRuntimeOp.StrFirstRune: {
          PinoVmString s = StrArg(0);
          return PinoValue.FromRune(s.ByteLength == 0 ? 0 : s.DecodeRuneAt(0, out _));
        }

      case IrRuntimeOp.ToStr:
        return NewStringValue(Format(Arg(0), call.Arguments.Length > 0 ? func.OperandType(call.Arguments[0]) : IrTypeId.Invalid, quoteStrings: false));

      case IrRuntimeOp.IfaceTypeName: {
          var box = UseObject((PinoVmIfaceBox) Arg(0).Obj!, "type name lookup");
          IrVTable vtable = _module.VTables[box.VTableId];
          return NewStringValue(vtable.TypeName ?? _types.GetType(vtable.ConcreteStructType).Name);
        }

      case IrRuntimeOp.RandFloat:
        return PinoValue.FromFloat(_host.Random.NextDouble());

      case IrRuntimeOp.RandInt: {
          long limit = Arg(0).Raw;
          return PinoValue.FromInt(limit <= 0 ? 0 : _host.Random.NextInt64(limit));
        }

      case IrRuntimeOp.Time:
        return PinoValue.FromFloat(_host.ClockMs());

      case IrRuntimeOp.Sleep:
        _host.Sleep((int) Math.Clamp(Arg(0).Raw, 0, int.MaxValue));
        return default;

      case IrRuntimeOp.Clear:
        _host.Clear();
        return default;

      case IrRuntimeOp.RegexCompile: {
          PinoVmString patternStr = StrArg(0);
          PinoRegex regex;
          try {
            regex = PinoRegex.Compile(patternStr.Span);
          } catch (PinoRegexParseException ex) {
            throw new PinoVmPanic(ex.Message);
          }

          PinoVmString ownedPattern = Alloc(new PinoVmString(patternStr.Span.ToArray(), 0, patternStr.ByteLength), _types.String);
          var rx = new PinoVmRegex(ownedPattern, regex);
          Alloc(rx, destType);
          return PinoValue.FromObject(rx);
        }

      case IrRuntimeOp.ReadFile:
        return ExecReadFile(StrArg(0).ToString(), destType);

      case IrRuntimeOp.WriteFile:
        return ExecWriteFile(StrArg(0).ToString(), StrArg(1).ToString(), destType);

      case IrRuntimeOp.FileExists:
        return PinoValue.FromBool(_host.Fs.Exists(StrArg(0).ToString()));

      case IrRuntimeOp.StrLower:
        return NewStringValue(StrArg(0).ToString().ToLowerInvariant());

      case IrRuntimeOp.StrUpper:
        return NewStringValue(StrArg(0).ToString().ToUpperInvariant());

      case IrRuntimeOp.StrTrim:
        return TrimSlice(StrArg(0), trimStart: true, trimEnd: true);

      case IrRuntimeOp.StrTrimStart:
        return TrimSlice(StrArg(0), trimStart: true, trimEnd: false);

      case IrRuntimeOp.StrTrimEnd:
        return TrimSlice(StrArg(0), trimStart: false, trimEnd: true);

      case IrRuntimeOp.StrClone:
      case IrRuntimeOp.StrToCString: {
          PinoVmString s = StrArg(0);
          return PinoValue.FromObject(Alloc(new PinoVmString(s.Span.ToArray(), 0, s.ByteLength), _types.String));
        }

      case IrRuntimeOp.StrContains:
        return PinoValue.FromBool(StrArg(0).Span.IndexOf(StrArg(1).Span) >= 0);

      case IrRuntimeOp.StrStartsWith:
        return PinoValue.FromBool(StrArg(0).Span.StartsWith(StrArg(1).Span));

      case IrRuntimeOp.StrEndsWith:
        return PinoValue.FromBool(StrArg(0).Span.EndsWith(StrArg(1).Span));

      case IrRuntimeOp.StrIndexOf: {
          PinoVmString s = StrArg(0);
          int at = s.Span.IndexOf(StrArg(1).Span);
          return PinoValue.FromInt(at < 0 ? -1 : RunesBefore(s, at));
        }

      case IrRuntimeOp.StrLastIndexOf: {
          PinoVmString s = StrArg(0);
          PinoVmString sub = StrArg(1);
          if (sub.ByteLength == 0) {
            return PinoValue.FromInt(s.RuneCount);
          }

          int at = s.Span.LastIndexOf(sub.Span);
          return PinoValue.FromInt(at < 0 ? -1 : RunesBefore(s, at));
        }

      case IrRuntimeOp.StrSplit:
        return SplitString(StrArg(0), StrArg(1), destType);

      case IrRuntimeOp.StrReplace: {
          string source = StrArg(0).ToString();
          string oldText = StrArg(1).ToString();
          string newText = StrArg(2).ToString();
          return NewStringValue(oldText.Length == 0 ? source : source.Replace(oldText, newText, StringComparison.Ordinal));
        }

      case IrRuntimeOp.StrSubstring: {
          PinoVmString s = StrArg(0);
          long start = Arg(1).Raw;
          long length = Arg(2).Raw;
          int runes = s.RuneCount;
          if (start < 0 || length < 0 || start + length > runes) {
            throw new PinoVmPanic(string.Create(
                CultureInfo.InvariantCulture,
                $"Substring starting at {start} with length {length} is out of range for a string of length {runes}."));
          }

          int byteStart = s.ByteOffsetOfRune((int) start);
          int byteEnd = s.ByteOffsetOfRune((int) (start + length));
          return PinoValue.FromObject(Alloc(s.Slice(byteStart, byteEnd - byteStart), _types.String));
        }

      case IrRuntimeOp.RegexPattern: {
          var rx = UseObject((PinoVmRegex) Arg(0).Obj!, "regex access");
          _rc.Inc(rx.Pattern);
          return PinoValue.FromObject(rx.Pattern);
        }

      case IrRuntimeOp.RegexHasMatch:
        return PinoValue.FromBool(RegexArg(Arg(0)).Regex.HasMatch(StrArg(1).Span));

      case IrRuntimeOp.RegexMatchPrefix: {
          PinoVmString text = StrArg(1);
          return RegexArg(Arg(0)).Regex.MatchPrefix(text.Span, out int end)
              ? PinoValue.FromObject(Alloc(text.Slice(0, end), _types.String))
              : NewStringValue(string.Empty);
        }

      case IrRuntimeOp.RegexFind: {
          PinoVmString text = StrArg(1);
          return RegexArg(Arg(0)).Regex.Find(text.Span, out int start, out int end)
              ? PinoValue.FromObject(Alloc(text.Slice(start, end - start), _types.String))
              : NewStringValue(string.Empty);
        }

      case IrRuntimeOp.RegexFindAll: {
          PinoVmString text = StrArg(1);
          List<(int Start, int End)> matches = RegexArg(Arg(0)).Regex.FindAll(text.Span);
          var vec = new PinoVmVec(matches.Count);
          foreach ((int start, int end) in matches) {
            vec.Add(PinoValue.FromObject(Alloc(text.Slice(start, end - start), _types.String)));
          }

          Alloc(vec, destType);
          return PinoValue.FromObject(vec);
        }

      case IrRuntimeOp.RegexReplace: {
          PinoVmString text = StrArg(1);
          PinoVmString repl = StrArg(2);
          byte[] replaced = RegexArg(Arg(0)).Regex.Replace(text.Span, repl.Span);
          return PinoValue.FromObject(Alloc(new PinoVmString(replaced, 0, replaced.Length), _types.String));
        }

      default:
        throw new PinoVmInternalException($"The VM has no implementation for the runtime operation '{call.Op}'.");
    }
  }

  private PinoValue NewStringValue(string text) => PinoValue.FromObject(NewString(text));

  private PinoVmRegex RegexArg(PinoValue value) => UseObject((PinoVmRegex) value.Obj!, "regex access");

  private static int RunesBefore(PinoVmString s, int bytePos) {
    int count = 0;
    for (int i = 0; i < bytePos; i++) {
      if ((s.Bytes[s.Offset + i] & 0xC0) != 0x80) {
        count++;
      }
    }

    return count;
  }

  private PinoValue TrimSlice(PinoVmString s, bool trimStart, bool trimEnd) {
    int start = 0;
    int end = s.ByteLength;
    if (trimStart) {
      while (start < end) {
        int cp = s.DecodeRuneAt(start, out int width);
        if (!Rune.IsWhiteSpace(new Rune(Rune.IsValid(cp) ? cp : 0xFFFD))) {
          break;
        }

        start += width;
      }
    }

    if (trimEnd) {
      while (end > start) {
        int back = end - 1;
        while (back > start && (s.Bytes[s.Offset + back] & 0xC0) == 0x80) {
          back--;
        }

        int cp = s.DecodeRuneAt(back, out _);
        if (!Rune.IsWhiteSpace(new Rune(Rune.IsValid(cp) ? cp : 0xFFFD))) {
          break;
        }

        end = back;
      }
    }

    return PinoValue.FromObject(Alloc(s.Slice(start, end - start), _types.String));
  }

  private PinoValue SplitString(PinoVmString s, PinoVmString separator, IrTypeId vectorType) {
    var vec = new PinoVmVec(4);
    if (separator.ByteLength == 0) {
      vec.Add(PinoValue.FromObject(Alloc(s.Slice(0, s.ByteLength), _types.String)));
    } else {
      int pos = 0;
      while (true) {
        int at = s.Span[pos..].IndexOf(separator.Span);
        if (at < 0) {
          vec.Add(PinoValue.FromObject(Alloc(s.Slice(pos, s.ByteLength - pos), _types.String)));
          break;
        }

        vec.Add(PinoValue.FromObject(Alloc(s.Slice(pos, at), _types.String)));
        pos += at + separator.ByteLength;
      }
    }

    Alloc(vec, vectorType);
    return PinoValue.FromObject(vec);
  }

  private IrTypeId VariantFieldType(IrTypeId unionType, string variantName, int fieldIndex) {
    var union = (IrUnionType) _types.GetType(unionType);
    foreach (IrUnionVariant variant in union.Variants) {
      if (string.Equals(variant.Name, variantName, StringComparison.Ordinal)) {
        return variant.Fields[fieldIndex].Type;
      }
    }

    throw new PinoVmInternalException($"Union '{union.Name}' has no variant '{variantName}'.");
  }

  private PinoValue MakeIoError(IrTypeId resultType, VmFileError error, string path) {
    IrTypeId ioErrorType = VariantFieldType(resultType, "Failure", 0);
    (string variant, string payload) = error.Kind switch {
      VmFileErrorKind.NotFound => ("NotFound", path),
      VmFileErrorKind.PermissionDenied => ("PermissionDenied", string.IsNullOrEmpty(error.Message) ? path : error.Message),
      VmFileErrorKind.AlreadyExists => ("AlreadyExists", path),
      _ => ("Gremlin", error.Message),
    };
    PinoValue ioError = MakeUnion(ioErrorType, variant, NewStringValue(payload));
    return MakeUnion(resultType, "Failure", ioError);
  }

  private PinoValue ExecReadFile(string path, IrTypeId resultType) =>
      _host.Fs.TryReadAllText(path, out string content, out VmFileError error)
          ? MakeUnion(resultType, "Success", NewStringValue(content))
          : MakeIoError(resultType, error, path);

  private PinoValue ExecWriteFile(string path, string content, IrTypeId resultType) =>
      _host.Fs.TryWriteAllText(path, content, out VmFileError error)
          ? MakeUnion(resultType, "Success", NewStringValue(path))
          : MakeIoError(resultType, error, path);
}
