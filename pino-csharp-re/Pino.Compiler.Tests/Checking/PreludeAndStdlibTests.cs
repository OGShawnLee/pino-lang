using Pino.Compiler.Checking;
using Pino.Compiler.Syntax;

namespace Pino.Compiler.Tests.Checking;

public sealed class PreludeAndStdlibTests {
  [Fact]
  public void PreludeSource_ParsesWithZeroDiagnosticsAndProducesWellKnownUnions() {
    ParseResult parsed = PreludeSource.Parsed;
    Assert.Equal("<prelude>", PreludeSource.VirtualPath);
    Assert.Equal("<prelude>", parsed.Source.FilePath);
    Assert.Empty(parsed.Diagnostics);

    PreludeSymbols prelude = PreludeSymbols.Instance;
    Assert.Equal("Option", prelude.OptionUnion.Name);
    Assert.Single(prelude.OptionUnion.TypeParameters);
    Assert.Equal("Some", prelude.OptionSomeVariant.Name);
    Assert.Equal("None", prelude.OptionNoneVariant.Name);
    Assert.Single(prelude.OptionSomeVariant.Fields);
    Assert.Empty(prelude.OptionNoneVariant.Fields);

    Assert.Equal("Result", prelude.ResultUnion.Name);
    Assert.Equal(2, prelude.ResultUnion.TypeParameters.Length);
    Assert.Equal("Success", prelude.ResultSuccessVariant.Name);
    Assert.Equal("Failure", prelude.ResultFailureVariant.Name);
    Assert.Single(prelude.ResultSuccessVariant.Fields);
    Assert.Single(prelude.ResultFailureVariant.Fields);

    Assert.Equal("IOError", prelude.IOErrorUnion.Name);
    Assert.Empty(prelude.IOErrorUnion.TypeParameters);
    Assert.Equal(4, prelude.IOErrorUnion.Variants.Length);
    Assert.Equal("NotFound", prelude.IOErrorNotFoundVariant.Name);
    Assert.Equal("PermissionDenied", prelude.IOErrorPermissionDeniedVariant.Name);
    Assert.Equal("AlreadyExists", prelude.IOErrorAlreadyExistsVariant.Name);
    Assert.Equal("Gremlin", prelude.IOErrorGremlinVariant.Name);

    var types = new TypeInterner();
    TypeParameterSymbol optionT = prelude.OptionUnion.TypeParameters[0];
    Assert.Same(types.GetTypeParameter(optionT), prelude.OptionSomeVariant.Fields[0].Type);

    Assert.True(prelude.Scope.TryLookup("Option", out Symbol? optSym));
    Assert.Same(prelude.OptionUnion, optSym);
    Assert.True(prelude.Scope.TryLookup("Result", out Symbol? resSym));
    Assert.Same(prelude.ResultUnion, resSym);
    Assert.True(prelude.Scope.TryLookup("IOError", out Symbol? ioSym));
    Assert.Same(prelude.IOErrorUnion, ioSym);
  }

  [Fact]
  public void StdlibCatalog_RegistersAllGlobalsWithExpectedSignatures() {
    var types = new TypeInterner();
    PreludeSymbols prelude = PreludeSymbols.Instance;

    string[] expectedGlobals = [
      "println", "readline", "int", "float", "rune", "str", "type",
      "rand", "time", "sleep", "clear", "panic", "regex",
      "read_file", "write_file", "file_exists",
    ];
    Assert.Equal(expectedGlobals.Length, StdlibCatalog.Globals.Count);

    foreach (string name in expectedGlobals) {
      Assert.True(StdlibCatalog.TryGetGlobal(name, out IntrinsicDescriptor? desc), $"Missing global '{name}'.");
      Assert.NotNull(desc);
      Assert.True(prelude.Scope.TryLookup(name, out Symbol? sym));
      Assert.IsType<FunctionSymbol>(sym);
    }

    Assert.True(StdlibCatalog.TryGetGlobal("println", out IntrinsicDescriptor? println));
    Assert.Single(println!.Signatures);
    Assert.Equal(IntrinsicId.Println, println.Signatures[0].Id);
    Assert.True(println.Signatures[0].Parameters[0].IsVariadic);
    Assert.Same(types.Void, println.Signatures[0].ReturnType.Resolve(types, prelude, [], []));

    Assert.True(StdlibCatalog.TryGetGlobal("panic", out IntrinsicDescriptor? panic));
    Assert.Equal(IntrinsicId.Panic, panic!.Signatures[0].Id);
    Assert.Same(types.Never, panic.Signatures[0].ReturnType.Resolve(types, prelude, [], []));

    Assert.True(StdlibCatalog.TryGetGlobal("readline", out IntrinsicDescriptor? readline));
    Assert.Equal(2, readline!.Signatures.Length);
    Assert.Equal(IntrinsicId.ReadlineNoPrompt, readline.Signatures[0].Id);
    Assert.Equal(IntrinsicId.ReadlineWithPrompt, readline.Signatures[1].Id);
    UnionType expectedReadlineRet = types.GetUnion(prelude.ResultUnion, [types.String, types.String]);
    Assert.Same(expectedReadlineRet, readline.Signatures[0].ReturnType.Resolve(types, prelude, [], []));

    Assert.True(StdlibCatalog.TryGetGlobal("int", out IntrinsicDescriptor? intFn));
    Assert.Equal(IntrinsicId.IntFromNumber, intFn!.Signatures[0].Id);
    Assert.Equal(IntrinsicParamKind.Numeric, intFn.Signatures[0].Parameters[0].Kind);
    Assert.Same(types.Int, intFn.Signatures[0].ReturnType.Resolve(types, prelude, [], []));
    Assert.Equal(IntrinsicId.IntFromString, intFn.Signatures[1].Id);
    Assert.Same(
        types.GetUnion(prelude.ResultUnion, [types.Int, types.String]),
        intFn.Signatures[1].ReturnType.Resolve(types, prelude, [], []));

    Assert.True(StdlibCatalog.TryGetGlobal("float", out IntrinsicDescriptor? floatFn));
    Assert.Equal(IntrinsicId.FloatFromNumber, floatFn!.Signatures[0].Id);
    Assert.Equal(IntrinsicId.FloatFromString, floatFn.Signatures[1].Id);
    Assert.Same(
        types.GetUnion(prelude.ResultUnion, [types.Float, types.String]),
        floatFn.Signatures[1].ReturnType.Resolve(types, prelude, [], []));

    Assert.True(StdlibCatalog.TryGetGlobal("rune", out IntrinsicDescriptor? runeFn));
    Assert.Equal(IntrinsicId.RuneFromInt, runeFn!.Signatures[0].Id);
    Assert.Equal(IntrinsicId.RuneFromString, runeFn.Signatures[1].Id);

    Assert.True(StdlibCatalog.TryGetGlobal("rand", out IntrinsicDescriptor? randFn));
    Assert.Equal(IntrinsicId.RandFloat, randFn!.Signatures[0].Id);
    Assert.Same(types.Float, randFn.Signatures[0].ReturnType.Resolve(types, prelude, [], []));
    Assert.Equal(IntrinsicId.RandInt, randFn.Signatures[1].Id);
    Assert.Same(types.Int, randFn.Signatures[1].ReturnType.Resolve(types, prelude, [], []));

    Assert.True(StdlibCatalog.TryGetGlobal("regex", out IntrinsicDescriptor? regexFn));
    Assert.Equal(IntrinsicId.RegexCompile, regexFn!.Signatures[0].Id);
    Assert.Same(types.Regex, regexFn.Signatures[0].ReturnType.Resolve(types, prelude, [], []));

    UnionType expectedIOResult = types.GetUnion(
        prelude.ResultUnion,
        [types.String, types.GetUnion(prelude.IOErrorUnion)]);
    Assert.True(StdlibCatalog.TryGetGlobal("read_file", out IntrinsicDescriptor? readFile));
    Assert.Same(expectedIOResult, readFile!.Signatures[0].ReturnType.Resolve(types, prelude, [], []));
    Assert.True(StdlibCatalog.TryGetGlobal("write_file", out IntrinsicDescriptor? writeFile));
    Assert.Same(expectedIOResult, writeFile!.Signatures[0].ReturnType.Resolve(types, prelude, [], []));
    Assert.True(StdlibCatalog.TryGetGlobal("file_exists", out IntrinsicDescriptor? fileExists));
    Assert.Same(types.Bool, fileExists!.Signatures[0].ReturnType.Resolve(types, prelude, [], []));
  }

  [Fact]
  public void StdlibCatalog_RegistersAllBuiltInTypeMembersAndExcludesToStringAndExtend() {
    var types = new TypeInterner();
    PreludeSymbols prelude = PreludeSymbols.Instance;

    // string members
    string[] expectedString = [
      "len", "length", "lower", "upper", "trim", "trim_start", "trim_end",
      "to_owned", "clone", "to_cstring", "cstr", "contains", "starts_with",
      "ends_with", "index_of", "last_index_of", "split", "replace", "substring",
    ];
    Assert.Equal(expectedString.Length, StdlibCatalog.StringMembers.Count);
    foreach (string name in expectedString) {
      Assert.True(StdlibCatalog.TryGetMember(types.String, name, out _), $"Missing string:{name}");
    }

    // []T members
    VectorType intVec = types.GetVector(types.Int);
    string[] expectedVec = [
      "len", "length", "cap", "capacity", "push", "add", "pop", "swap",
      "each", "map", "filter", "find", "find_index", "any", "all", "count", "join",
    ];
    Assert.Equal(expectedVec.Length, StdlibCatalog.VectorMembers.Count);
    foreach (string name in expectedVec) {
      Assert.True(StdlibCatalog.TryGetMember(intVec, name, out _), $"Missing []T:{name}");
    }

    Assert.True(StdlibCatalog.TryGetMember(intVec, "push", out IntrinsicDescriptor? vecPush));
    Assert.True(vecPush!.MutatesReceiver);
    Assert.Equal(IntrinsicId.VecPush, vecPush.Signatures[0].Id);
    Assert.Same(types.Void, vecPush.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));

    Assert.True(StdlibCatalog.TryGetMember(intVec, "add", out IntrinsicDescriptor? vecAdd));
    Assert.True(vecAdd!.MutatesReceiver);
    Assert.Equal(IntrinsicId.VecPush, vecAdd.Signatures[0].Id);
    Assert.Same(types.Void, vecAdd.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));

    Assert.True(StdlibCatalog.TryGetMember(intVec, "swap", out IntrinsicDescriptor? vecSwap));
    Assert.True(vecSwap!.MutatesReceiver);
    Assert.Equal(IntrinsicId.VecSwap, vecSwap.Signatures[0].Id);
    Assert.Equal(2, vecSwap.Signatures[0].Parameters.Length);
    Assert.Same(types.Void, vecSwap.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));

    Assert.True(StdlibCatalog.TryGetMember(intVec, "map", out IntrinsicDescriptor? vecMap));
    Assert.Equal(2, vecMap!.Signatures.Length);
    Assert.Equal(IntrinsicId.VecMap1, vecMap.Signatures[0].Id);
    Assert.Equal(IntrinsicId.VecMap2, vecMap.Signatures[1].Id);
    Assert.Same(
        types.GetVector(types.String),
        vecMap.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], [types.String]));

    Assert.True(StdlibCatalog.TryGetMember(intVec, "join", out IntrinsicDescriptor? vecJoin));
    Assert.Equal(IntrinsicId.VecJoin, vecJoin!.Signatures[0].Id);
    Assert.Same(types.String, vecJoin.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));

    // map[K, V] members
    MapType mapType = types.GetMap(types.String, types.Int);
    string[] expectedMap = ["len", "length", "keys", "values", "remove"];
    Assert.Equal(expectedMap.Length, StdlibCatalog.MapMembers.Count);
    foreach (string name in expectedMap) {
      Assert.True(StdlibCatalog.TryGetMember(mapType, name, out _), $"Missing map:{name}");
    }
    Assert.True(StdlibCatalog.TryGetMember(mapType, "remove", out IntrinsicDescriptor? mapRemove));
    Assert.Same(
        types.GetUnion(prelude.OptionUnion, [types.Int]),
        mapRemove!.Signatures[0].ReturnType.Resolve(types, prelude, [types.String, types.Int], []));

    // set[T] members (D-175 / SETLIB-1..8)
    SetType setType = types.GetSet(types.Int);
    string[] expectedSet = ["len", "length", "add", "remove", "any", "all", "each"];
    Assert.Equal(expectedSet.Length, StdlibCatalog.SetMembers.Count);
    foreach (string name in expectedSet) {
      Assert.True(StdlibCatalog.TryGetMember(setType, name, out _), $"Missing set:{name}");
    }
    Assert.True(StdlibCatalog.TryGetMember(setType, "add", out IntrinsicDescriptor? setAdd));
    Assert.Single(setAdd!.Signatures);
    Assert.Same(types.Void, setAdd.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));
    Assert.True(StdlibCatalog.TryGetMember(setType, "remove", out IntrinsicDescriptor? setRemove));
    Assert.Same(types.Void, setRemove!.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));
    Assert.True(StdlibCatalog.TryGetMember(setType, "any", out IntrinsicDescriptor? setAny));
    Assert.Single(setAny!.Signatures);

    // Pool[T] members (D-185 / POOLLIB-1..8)
    PoolType poolType = types.GetPool(types.Int);
    HandleType handleType = types.GetHandle(types.Int);
    string[] expectedPool = ["len", "length", "add", "remove", "get", "handles", "clear"];
    Assert.Equal(expectedPool.Length, StdlibCatalog.PoolMembers.Count);
    foreach (string name in expectedPool) {
      Assert.True(StdlibCatalog.TryGetMember(poolType, name, out _), $"Missing Pool:{name}");
    }
    Assert.True(StdlibCatalog.TryGetMember(poolType, "add", out IntrinsicDescriptor? poolAdd));
    Assert.True(poolAdd!.MutatesReceiver);
    Assert.Equal(IntrinsicId.PoolAdd, poolAdd.Signatures[0].Id);
    Assert.Same(handleType, poolAdd.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));
    Assert.True(StdlibCatalog.TryGetMember(poolType, "remove", out IntrinsicDescriptor? poolRemove));
    Assert.True(poolRemove!.MutatesReceiver);
    Assert.Equal(IntrinsicId.PoolRemove, poolRemove.Signatures[0].Id);
    Assert.Same(
        types.GetUnion(prelude.OptionUnion, [types.Int]),
        poolRemove.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));
    Assert.True(StdlibCatalog.TryGetMember(poolType, "get", out IntrinsicDescriptor? poolGet));
    Assert.False(poolGet!.MutatesReceiver);
    Assert.Equal(IntrinsicId.PoolGet, poolGet.Signatures[0].Id);
    Assert.Same(
        types.GetUnion(prelude.OptionUnion, [types.Int]),
        poolGet.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));
    Assert.True(StdlibCatalog.TryGetMember(poolType, "handles", out IntrinsicDescriptor? poolHandles));
    Assert.False(poolHandles!.MutatesReceiver);
    Assert.Equal(IntrinsicId.PoolHandles, poolHandles.Signatures[0].Id);
    Assert.Same(
        types.GetVector(handleType),
        poolHandles.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));
    Assert.True(StdlibCatalog.TryGetMember(poolType, "clear", out IntrinsicDescriptor? poolClear));
    Assert.True(poolClear!.MutatesReceiver);
    Assert.Equal(IntrinsicId.PoolClear, poolClear.Signatures[0].Id);
    Assert.Same(types.Void, poolClear.Signatures[0].ReturnType.Resolve(types, prelude, [types.Int], []));

    // regex members
    string[] expectedRegex = ["pattern", "has_match", "match_prefix", "find", "find_all", "replace"];
    Assert.Equal(expectedRegex.Length, StdlibCatalog.RegexMembers.Count);
    foreach (string name in expectedRegex) {
      Assert.True(StdlibCatalog.TryGetMember(types.Regex, name, out _), $"Missing regex:{name}");
    }

    // Explicit exclusions: D-079 (no :to_string on built-in values) and no []T:extend
    Assert.False(StdlibCatalog.TryGetMember(types.Int, "to_string", out _));
    Assert.False(StdlibCatalog.TryGetMember(types.String, "to_string", out _));
    Assert.False(StdlibCatalog.TryGetMember(intVec, "extend", out _));

    // Every IntrinsicId value must be indexed in StdlibCatalog
    foreach (IntrinsicId id in Enum.GetValues<IntrinsicId>()) {
      Assert.True(StdlibCatalog.TryGetById(id, out _, out _), $"IntrinsicId.{id} is not registered in StdlibCatalog.");
    }
  }

  [Fact]
  public void VirtualStdlibStub_ProjectsValidSpansForAllEntries() {
    VirtualStdlibStub stub = VirtualStdlibStub.Instance;
    Assert.Equal("<stdlib>", VirtualStdlibStub.VirtualPath);
    Assert.NotEmpty(stub.Entries);

    foreach (VirtualStdlibEntry entry in stub.Entries) {
      Assert.True(entry.DeclarationSpan.Start >= 0);
      Assert.True(entry.DeclarationSpan.End <= stub.Content.Length);
      Assert.True(entry.DeclarationSpan.Contains(entry.NameSpan));
      Assert.Equal(entry.MemberName, stub.Source.AsSpan(entry.NameSpan).ToString());
      Assert.False(string.IsNullOrWhiteSpace(entry.Documentation));
    }

    foreach (IntrinsicId id in Enum.GetValues<IntrinsicId>()) {
      Assert.True(stub.TryGetEntry(id, out _), $"Missing VirtualStdlibStub entry for {id}.");
    }

    IntrinsicId[] expectedMutatingIds = [
      IntrinsicId.VecPush,
      IntrinsicId.VecPop,
      IntrinsicId.VecSwap,
      IntrinsicId.MapRemove,
      IntrinsicId.SetAdd,
      IntrinsicId.SetRemove,
      IntrinsicId.PoolAdd,
      IntrinsicId.PoolRemove,
      IntrinsicId.PoolClear,
    ];
    IntrinsicId[] actualMutatingIds = StdlibCatalog.AllDescriptors
        .Where(static d => d.MutatesReceiver)
        .SelectMany(static d => d.Signatures.Select(static s => s.Id))
        .Distinct()
        .OrderBy(static id => id)
        .ToArray();
    Assert.Equal(expectedMutatingIds.OrderBy(static id => id).ToArray(), actualMutatingIds);

    foreach (IntrinsicDescriptor mutDesc in StdlibCatalog.AllDescriptors.Where(static d => d.MutatesReceiver)) {
      Assert.True(stub.TryGetEntry(mutDesc.Receiver, mutDesc.Name, out VirtualStdlibEntry? mutEntry));
      Assert.StartsWith("mut fn ", mutEntry!.SignatureText, StringComparison.Ordinal);
      Assert.Equal(mutDesc.Name, stub.Source.AsSpan(mutEntry.NameSpan).ToString());
    }

    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Vector, "push", out VirtualStdlibEntry? vecPushEntry));
    Assert.Equal("mut fn push(item T) -> void", vecPushEntry!.SignatureText);
    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Vector, "add", out VirtualStdlibEntry? vecAddEntry));
    Assert.Equal("mut fn add(item T) -> void", vecAddEntry!.SignatureText);
    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Vector, "swap", out VirtualStdlibEntry? vecSwapEntry));
    Assert.Equal("mut fn swap(i int, j int) -> void", vecSwapEntry!.SignatureText);
    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Pool, "add", out VirtualStdlibEntry? poolAddEntry));
    Assert.Equal("mut fn add(value T) -> Handle[T]", poolAddEntry!.SignatureText);
    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Pool, "remove", out VirtualStdlibEntry? poolRemoveEntry));
    Assert.Equal("mut fn remove(handle Handle[T]) -> Option[T]", poolRemoveEntry!.SignatureText);
    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Pool, "get", out VirtualStdlibEntry? poolGetEntry));
    Assert.Equal("fn get(handle Handle[T]) -> Option[T]", poolGetEntry!.SignatureText);
    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Pool, "handles", out VirtualStdlibEntry? poolHandlesEntry));
    Assert.Equal("fn handles() -> []Handle[T]", poolHandlesEntry!.SignatureText);
    Assert.True(stub.TryGetEntry(StdlibReceiverKind.Pool, "clear", out VirtualStdlibEntry? poolClearEntry));
    Assert.Equal("mut fn clear() -> void", poolClearEntry!.SignatureText);
  }
}
