using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using Pino.Compiler.Text;

namespace Pino.Compiler.Checking;

public sealed record VirtualStdlibEntry(
    IntrinsicId Id,
    StdlibReceiverKind Receiver,
    string MemberName,
    SourceSpan DeclarationSpan,
    SourceSpan NameSpan,
    string SignatureText,
    string Documentation);

/// <summary>
/// Cached, read-only virtual <c>stdlib.pino</c> buffer and source spans projected from
/// <see cref="StdlibCatalog"/> for LSP hover and go-to-definition.
/// </summary>
public sealed class VirtualStdlibStub {
  public const string VirtualPath = "<stdlib>";

  private static readonly Lazy<VirtualStdlibStub> s_instance = new(Build);

  public static VirtualStdlibStub Instance => s_instance.Value;

  private readonly FrozenDictionary<IntrinsicId, VirtualStdlibEntry> _byId;
  private readonly FrozenDictionary<(StdlibReceiverKind Receiver, string MemberName), VirtualStdlibEntry> _byMember;

  private VirtualStdlibStub(
      SourceText source,
      ImmutableArray<VirtualStdlibEntry> entries,
      FrozenDictionary<IntrinsicId, VirtualStdlibEntry> byId,
      FrozenDictionary<(StdlibReceiverKind Receiver, string MemberName), VirtualStdlibEntry> byMember) {
    Source = source;
    Entries = entries;
    _byId = byId;
    _byMember = byMember;
  }

  public SourceText Source { get; }

  public string Content => Source.Text;

  public ImmutableArray<VirtualStdlibEntry> Entries { get; }

  public bool TryGetEntry(IntrinsicId id, [NotNullWhen(true)] out VirtualStdlibEntry? entry) =>
      _byId.TryGetValue(id, out entry);

  public bool TryGetEntry(
      StdlibReceiverKind receiver,
      string memberName,
      [NotNullWhen(true)] out VirtualStdlibEntry? entry) {
    ArgumentNullException.ThrowIfNull(memberName);
    return _byMember.TryGetValue((receiver, memberName), out entry);
  }

  private static VirtualStdlibStub Build() {
    var sb = new StringBuilder(4096);
    var entries = ImmutableArray.CreateBuilder<VirtualStdlibEntry>();
    var byId = new Dictionary<IntrinsicId, VirtualStdlibEntry>();
    var byMember = new Dictionary<(StdlibReceiverKind, string), VirtualStdlibEntry>();

    sb.Append("# Virtual standard library declarations for LSP navigation.\n");

    AppendSection(sb, entries, byId, byMember, "Globals", StdlibCatalog.Globals.Values, []);
    AppendSection(sb, entries, byId, byMember, "string", StdlibCatalog.StringMembers.Values, []);
    AppendSection(sb, entries, byId, byMember, "[]T", StdlibCatalog.VectorMembers.Values, ["T"]);
    AppendSection(sb, entries, byId, byMember, "map[K, V]", StdlibCatalog.MapMembers.Values, ["K", "V"]);
    AppendSection(sb, entries, byId, byMember, "set[T]", StdlibCatalog.SetMembers.Values, ["T"]);
    AppendSection(sb, entries, byId, byMember, "regex", StdlibCatalog.RegexMembers.Values, []);

    var source = new SourceText(sb.ToString(), VirtualPath);
    return new VirtualStdlibStub(
        source,
        entries.ToImmutable(),
        byId.ToFrozenDictionary(),
        byMember.ToFrozenDictionary());
  }

  private static void AppendSection(
      StringBuilder sb,
      ImmutableArray<VirtualStdlibEntry>.Builder entries,
      Dictionary<IntrinsicId, VirtualStdlibEntry> byId,
      Dictionary<(StdlibReceiverKind, string), VirtualStdlibEntry> byMember,
      string sectionTitle,
      IEnumerable<IntrinsicDescriptor> descriptors,
      IReadOnlyList<string> receiverParamNames) {
    sb.Append('\n');
    sb.Append("# --- ").Append(sectionTitle).Append(" ---\n");

    foreach (IntrinsicDescriptor descriptor in descriptors) {
      sb.Append("# ").Append(descriptor.Documentation).Append('\n');

      for (int s = 0; s < descriptor.Signatures.Length; s++) {
        IntrinsicSignature signature = descriptor.Signatures[s];
        string sigText = FormatSignature(descriptor, signature, receiverParamNames);
        int declStart = sb.Length;
        sb.Append(sigText);
        int declEnd = sb.Length;
        sb.Append('\n');

        int nameIndex = FindMemberNameIndex(sigText, descriptor);
        SourceSpan declSpan = SourceSpan.FromBounds(declStart, declEnd);
        SourceSpan nameSpan = nameIndex >= 0
            ? new SourceSpan(declStart + nameIndex, descriptor.Name.Length)
            : declSpan;

        var entry = new VirtualStdlibEntry(
            signature.Id,
            descriptor.Receiver,
            descriptor.Name,
            declSpan,
            nameSpan,
            sigText,
            descriptor.Documentation);

        entries.Add(entry);
        byId.TryAdd(signature.Id, entry);
        byMember.TryAdd((descriptor.Receiver, descriptor.Name), entry);
      }
    }
  }

  private static string FormatSignature(
      IntrinsicDescriptor descriptor,
      IntrinsicSignature signature,
      IReadOnlyList<string> receiverParamNames) {
    string ret = signature.ReturnType.Format(receiverParamNames, signature.MethodTypeParameterNames);
    if (descriptor.IsProperty) {
      return $"{descriptor.Name} {ret}";
    }

    string typeParamsPrefix = signature.MethodTypeParameterNames.IsEmpty
        ? string.Empty
        : $"@@Generic[{string.Join(", ", signature.MethodTypeParameterNames)}] ";

    string prefix = descriptor.Receiver == StdlibReceiverKind.Global ? "pub fn " : "fn ";
    string paramList = string.Join(
        ", ",
        signature.Parameters.Select(p => {
          string typeText = p.TypeSpec.Format(receiverParamNames, signature.MethodTypeParameterNames);
          return p.IsVariadic ? $"{p.Name}... {typeText}" : $"{p.Name} {typeText}";
        }));

    return $"{typeParamsPrefix}{prefix}{descriptor.Name}({paramList}) -> {ret}";
  }

  private static int FindMemberNameIndex(string sigText, IntrinsicDescriptor descriptor) {
    if (descriptor.IsProperty) {
      return sigText.StartsWith(descriptor.Name, StringComparison.Ordinal) ? 0 : -1;
    }

    int fnIdx = sigText.IndexOf("fn ", StringComparison.Ordinal);
    if (fnIdx < 0) {
      return -1;
    }

    int candidate = fnIdx + 3;
    return sigText.AsSpan(candidate).StartsWith(descriptor.Name.AsSpan(), StringComparison.Ordinal)
        ? candidate
        : -1;
  }
}
