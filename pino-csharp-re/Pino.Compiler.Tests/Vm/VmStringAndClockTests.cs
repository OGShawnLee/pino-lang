using System.Diagnostics;
using System.Globalization;
using System.Text;

using Pino.Vm;

namespace Pino.Compiler.Tests.Vm;

/// <summary>
/// Unit and regression tests for <see cref="PinoVmString"/> ASCII/UTF-8 fast paths (<c>STRLIB-18</c>),
/// sub-millisecond VM <c>time()</c> (<c>BUILTIN-7</c>), and reusable call-frame/register stack safety.
/// </summary>
public sealed class VmStringAndClockTests {
  private static void AssertCleanRun(VmRun run) {
    Assert.Equal(0, run.Result.ExitCode);
    Assert.Null(run.Result.RcError);
    Assert.True(run.Result.LeakReport!.IsClean, run.Result.LeakReport.Format());
  }

  // ---------------------------------------------------------------------------
  // PinoVmString IsAscii invariants across construction, Slice, AppendInPlace
  // ---------------------------------------------------------------------------

  [Fact]
  public void PinoVmString_Construction_TracksIsAsciiAccurately() {
    PinoVmString empty = PinoVmString.FromString(string.Empty);
    PinoVmString ascii = PinoVmString.FromString("hello_world 123!");
    PinoVmString accented = PinoVmString.FromString("café");
    PinoVmString emoji = PinoVmString.FromString("a🌲bc");

    Assert.True(empty.IsAscii);
    Assert.Equal(0, empty.RuneCount);

    Assert.True(ascii.IsAscii);
    Assert.Equal(ascii.ByteLength, ascii.RuneCount);
    for (int i = 0; i < ascii.ByteLength; i++) {
      Assert.Equal(i, ascii.ByteOffsetOfRune(i));
      Assert.Equal(ascii.Bytes[i], ascii.DecodeRuneAt(i, out int w));
      Assert.Equal(1, w);
    }

    Assert.Equal(ascii.ByteLength, ascii.ByteOffsetOfRune(ascii.ByteLength));

    Assert.False(accented.IsAscii);
    Assert.Equal(5, accented.ByteLength);
    Assert.Equal(4, accented.RuneCount);
    Assert.Equal(3, accented.ByteOffsetOfRune(3));
    Assert.Equal('é', accented.DecodeRuneAt(3, out int accentWidth));
    Assert.Equal(2, accentWidth);

    Assert.False(emoji.IsAscii);
    Assert.Equal(7, emoji.ByteLength);
    Assert.Equal(4, emoji.RuneCount);
    Assert.Equal(1, emoji.ByteOffsetOfRune(1));
    Assert.Equal(5, emoji.ByteOffsetOfRune(2));
    Assert.Equal(0x1F332, emoji.DecodeRuneAt(1, out int emojiWidth));
    Assert.Equal(4, emojiWidth);
  }

  [Fact]
  public void PinoVmString_ByteConstructorsAndFromUtf8_RespectSliceBounds() {
    byte[] mixed = Encoding.UTF8.GetBytes("🌲helloé");
    // "🌲" is 4 bytes, "hello" is 5 bytes at offset 4, "é" is 2 bytes at offset 9.
    var whole = new PinoVmString(mixed, 0, mixed.Length);
    var asciiMiddle = new PinoVmString(mixed, 4, 5);
    var utf8Ascii = PinoVmString.FromUtf8("abc123"u8);
    var utf8Emoji = PinoVmString.FromUtf8("hi🌲"u8);

    Assert.False(whole.IsAscii);
    Assert.Equal(7, whole.RuneCount);

    Assert.True(asciiMiddle.IsAscii);
    Assert.Equal("hello", asciiMiddle.ToString());
    Assert.Equal(5, asciiMiddle.RuneCount);

    Assert.True(utf8Ascii.IsAscii);
    Assert.Equal(6, utf8Ascii.RuneCount);

    Assert.False(utf8Emoji.IsAscii);
    Assert.Equal(3, utf8Emoji.RuneCount);
  }

  [Fact]
  public void PinoVmString_Slice_PreservesAndRefinesIsAscii() {
    PinoVmString ascii = PinoVmString.FromString("abcdefgh");
    PinoVmString asciiSlice = ascii.Slice(2, 4);
    Assert.Same(ascii.Bytes, asciiSlice.Bytes);
    Assert.True(asciiSlice.IsAscii);
    Assert.Equal("cdef", asciiSlice.ToString());
    Assert.Equal(4, asciiSlice.RuneCount);
    Assert.Equal(2, asciiSlice.ByteOffsetOfRune(2));

    PinoVmString emoji = PinoVmString.FromString("a🌲bcé");
    PinoVmString emptySlice = emoji.Slice(1, 0);
    PinoVmString asciiSub = emoji.Slice(5, 2); // "bc"
    PinoVmString nonAsciiSub = emoji.Slice(1, 5); // "🌲b"

    Assert.True(emptySlice.IsAscii);
    Assert.Equal(0, emptySlice.RuneCount);

    Assert.True(asciiSub.IsAscii);
    Assert.Equal("bc", asciiSub.ToString());
    Assert.Equal(2, asciiSub.RuneCount);
    Assert.Equal(1, asciiSub.ByteOffsetOfRune(1));

    Assert.False(nonAsciiSub.IsAscii);
    Assert.Equal("🌲b", nonAsciiSub.ToString());
    Assert.Equal(2, nonAsciiSub.RuneCount);
    Assert.Equal(4, nonAsciiSub.ByteOffsetOfRune(1));
    Assert.Equal(0x1F332, nonAsciiSub.DecodeRuneAt(0, out int w));
    Assert.Equal(4, w);
  }

  [Fact]
  public void PinoVmString_AppendInPlace_UpdatesIsAsciiAccurately() {
    PinoVmString s = PinoVmString.FromString("abc");
    Assert.True(s.IsAscii);

    s.AppendInPlace(PinoVmString.FromString("def"));
    Assert.True(s.IsAscii);
    Assert.Equal("abcdef", s.ToString());
    Assert.Equal(6, s.RuneCount);
    Assert.Equal(4, s.ByteOffsetOfRune(4));

    s.AppendInPlace("123"u8);
    Assert.True(s.IsAscii);
    Assert.Equal(9, s.RuneCount);

    // Appending accented UTF-8 clears IsAscii.
    s.AppendInPlace(PinoVmString.FromString("é"));
    Assert.False(s.IsAscii);
    Assert.Equal(11, s.ByteLength);
    Assert.Equal(10, s.RuneCount);
    Assert.Equal(9, s.ByteOffsetOfRune(9));

    // Subsequent ASCII append keeps IsAscii false and rune indexing exact.
    s.AppendInPlace(PinoVmString.FromString("z"));
    Assert.False(s.IsAscii);
    Assert.Equal(12, s.ByteLength);
    Assert.Equal(11, s.RuneCount);
    Assert.Equal(11, s.ByteOffsetOfRune(10));
    Assert.Equal('z', s.DecodeRuneAt(11, out int w));
    Assert.Equal(1, w);

    // Raw UTF-8 span with 4-byte emoji clears IsAscii on an ASCII string.
    PinoVmString s2 = PinoVmString.FromString("ok");
    Assert.True(s2.IsAscii);
    s2.AppendInPlace("🌲!"u8);
    Assert.False(s2.IsAscii);
    Assert.Equal(7, s2.ByteLength);
    Assert.Equal(4, s2.RuneCount);
    Assert.Equal(2, s2.ByteOffsetOfRune(2));
    Assert.Equal(6, s2.ByteOffsetOfRune(3));
    Assert.Equal(0x1F332, s2.DecodeRuneAt(2, out int ew));
    Assert.Equal(4, ew);
  }

  [Fact]
  public void Strings_MixedAsciiAccentedAndEmojiOperations_VerifyRcClean() {
    const string Source = """
        var s = "hello"
        s = s + " world"
        println(s:len, s[6], s:substring(6, 5), s:index_of("world"))

        s = s + " café 🌲!"
        println(s:len, s[12], s[15], s[17], s[18])
        println(s:substring(12, 7))
        println(s:substring(17, 2))
        println(s:index_of("🌲"), s:last_index_of("!"))

        val ascii_part = s:substring(0, 5)
        println(ascii_part:len, ascii_part[4], ascii_part:substring(1, 3))

        var runes = 0
        for i, c in s {
          if i == 17 and c == '🌲' {
            runes += 100
          } else {
            runes += 1
          }
        }
        println(runes)

        val r1 = "ab" + 'é'
        val r2 = '🌲' + "xy"
        println(r1:len, r1[2], r2:len, r2[0], r2[2])
        """;

    VmRun run = VmTestHarness.Run(Source, verifyRc: true);

    Assert.Equal(
        "11 w world 6\n"
        + "19 c é 🌲 !\n"
        + "café 🌲!\n"
        + "🌲!\n"
        + "17 18\n"
        + "5 o ell\n"
        + "118\n"
        + "3 é 3 🌲 y",
        VmTestHarness.Normalize(run.Stdout));
    AssertCleanRun(run);
  }

  // ---------------------------------------------------------------------------
  // Linear-time ASCII substring regression test
  // ---------------------------------------------------------------------------

  [Fact]
  public void Substring_FiftyThousandCallsOver100KbAsciiString_IsLinearTime() {
    const string Source = """
        fn main {
          var chunk = "abcdefghij"
          var i = 0
          for i < 13 {
            chunk = chunk + chunk
            i += 1
          }
          # 10 * 2^13 = 81,920 bytes; extend past 100 KB (102,400 bytes)
          val text = chunk + chunk:substring(0, 20480)
          var total = 0
          var k = 0
          for k < 50000 {
            val pos = k * 2
            val sub = text:substring(pos, 2)
            total += sub:len
            k += 1
          }
          println(text:len, total)
        }
        """;

    var module = VmTestHarness.BuildPostRc(Source);

    // Warmup a tiny run to avoid first-call JIT skew, then time the 100 KB / 50k substring run.
    var sw = Stopwatch.StartNew();
    VmRun run = VmTestHarness.Execute(module, verifyRc: true, string.Empty, source: null);
    sw.Stop();

    TimeSpan limit = TestTimeScale.Apply(TimeSpan.FromSeconds(2));
    Assert.Equal("102400 100000", VmTestHarness.Normalize(run.Stdout));
    AssertCleanRun(run);
    Assert.True(
        sw.Elapsed < limit,
        $"Expected 50,000 ASCII substrings over 100 KB to finish in < {limit.TotalMilliseconds:F0} ms, but took {sw.Elapsed.TotalMilliseconds:F1} ms.");
  }

  // ---------------------------------------------------------------------------
  // Sub-millisecond VM time() (BUILTIN-7)
  // ---------------------------------------------------------------------------

  [Fact]
  public void UtcNowMs_AndPlaygroundTime_ReturnFractionalUnixEpochMilliseconds() {
    double beforeEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000.0;
    bool sawFractionalDirect = false;
    double sample = 0;
    for (int i = 0; i < 64; i++) {
      sample = IVmHost.UtcNowMs();
      if (sample != Math.Truncate(sample)) {
        sawFractionalDirect = true;
        break;
      }

      Thread.SpinWait(500);
    }

    double afterEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000.0;
    Assert.InRange(sample, beforeEpochMs, afterEpochMs);
    Assert.True(sawFractionalDirect, $"Expected IVmHost.UtcNowMs() to return fractional milliseconds, got {sample:R}.");

    // Verify end-to-end VM execution through PlaygroundRunner's host.
    const string Code = """
        var i = 0
        var last = 0.0
        var found = false
        for i < 200 {
          last = time()
          if last != float(int(last)) {
            found = true
            break
          }
          i += 1
        }
        println(found)
        println(last)
        """;

    PlaygroundResult result = PlaygroundRunner.Run(Code);
    Assert.True(result.Succeeded, result.StdErr);
    string[] lines = VmTestHarness.Normalize(result.StdOut).Split('\n');
    Assert.Equal(2, lines.Length);
    Assert.Equal("true", lines[0]);
    double vmTime = double.Parse(lines[1], CultureInfo.InvariantCulture);
    Assert.InRange(vmTime, beforeEpochMs, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 5000.0);
    Assert.NotEqual(Math.Truncate(vmTime), vmTime);
  }

  // ---------------------------------------------------------------------------
  // Reusable call-frame / register pool safety (mut PlaceRef, recursion, panics)
  // ---------------------------------------------------------------------------

  [Fact]
  public void CallFrameReuse_PreservesMutPlaceRefsRecursionAndPanicBacktraces() {
    const string Source = """
        fn bump_deep(mut acc int, depth int) int {
          if depth == 0 {
            acc += 1
            return acc
          }
          var local = depth * 2
          val r = bump_deep(mut acc, depth - 1)
          return r + local - local
        }

        var total = 0
        var i = 0
        for i < 200 {
          total = bump_deep(mut total, 20)
          i += 1
        }
        println(total)
        """;

    VmRun run = VmTestHarness.Run(Source, verifyRc: true);
    Assert.Equal("200", VmTestHarness.Normalize(run.Stdout));
    AssertCleanRun(run);
  }
}
