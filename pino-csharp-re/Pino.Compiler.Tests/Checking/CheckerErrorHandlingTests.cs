using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.Syntax;
using Pino.Compiler.Tests.Parsing;

namespace Pino.Compiler.Tests.Checking;

public sealed class CheckerErrorHandlingTests {
  [Fact]
  public void BubbleOperator_ResultOptionAndResultVoid() {
    const string Code = """
      union AppError {
        DatabaseError(int)
        NetworkError(string)
      }

      fn step(n int) Result[int, AppError] {
        if n < 0 {
          return Result::Failure(AppError::DatabaseError(500))
        }
        return Result::Success(n + 1)
      }

      fn run_steps(n int) Result[int, AppError] {
        val a = step(n)?
        val b = step(a)?
        return Result::Success(b * 2)
      }

      fn find_num(flag bool) Option[int] {
        if flag {
          return Option::Some(10)
        }
        return Option::None
      }

      fn use_opt(flag bool) Option[int] {
        val x = find_num(flag)?
        return Option::Some(x + 5)
      }

      fn save(ok bool) Result[void, string] {
        if ok {
          return Result::Success
        }
        return Result::Failure("disk full")
      }

      fn save_twice(ok bool) Result[void, string] {
        save(ok)?
        save(ok)?
        return Result::Success
      }
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Assert.Empty(compilation.Diagnostics);
    CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);

    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "a").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "b").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "x").Type);

    // Verify `save(ok)?` unwraps Result[void, string] to VoidType.
    BubbleExpressionSyntax[] bubbles = [.. CheckerTestSupport.All<BubbleExpressionSyntax>(compilation)];
    Assert.Contains(bubbles, b => compilation.RootModel.GetType(b)?.IsVoid == true);
  }

  [Fact]
  public void RecoveryBlocks_YieldReturnPanicAndPostfixPrecedence() {
    const string Code = """
      fn get_res(ok bool) Result[int, string] {
        if ok {
          return Result::Success(10)
        }
        return Result::Failure("boom")
      }

      fn get_opt(ok bool) Option[int] {
        if ok {
          return Option::Some(42)
        }
        return Option::None
      }

      fn recover_yield(ok bool) int {
        val a = get_res(ok) or {
          val msg string = err
          println(msg)
          yield 0
        }
        val b = get_opt(ok) or {
          val optMsg string = err
          println(optMsg)
          yield 1
        }
        val c = 10 + get_res(ok) or { yield 5 }
        return a + b + c
      }

      fn recover_return(ok bool) Result[int, int] {
        val v = get_res(ok) or {
          return Result::Failure(err:len)
        }
        return Result::Success(v)
      }

      fn recover_panic(ok bool) int {
        return get_res(ok) or {
          panic("fatal: " + err)
        }
      }

      fn recover_expected_lambda_in_yield() fn (int) -> int {
        val r Result[fn (int) -> int, string] = Result::Failure("none")
        val f fn (int) -> int = r or {
          yield fn (x) -> x + 1
        }
        return f
      }
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Assert.Empty(compilation.Diagnostics);
    CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);

    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "a").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "b").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "c").Type);
    Assert.Same(compilation.Types.Int, CheckerTestSupport.Local(compilation, "v").Type);
  }

  [Fact]
  public void ResultVoidSuccessParens_RejectedWithErrE8HelpText() {
    const string Code = """
      fn save() Result[void, string] {
        return Result::Success()
      }
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Diagnostic err = Assert.Single(CheckerTestSupport.CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.VariantArgumentMismatch, err.Code);
    Assert.Contains("write `Result::Success`", err.Message);
    Assert.Equal("write `Result::Success`", err.Help);
    CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);
  }

  [Fact]
  public void YieldInsideLambdaInsideOrBlock_RejectedWithPN3405() {
    const string Code = """
      fn get_res() Result[int, string] {
        return Result::Failure("e")
      }

      fn bad() int {
        val v = get_res() or {
          val f = fn () {
            yield 1
          }
          yield 0
        }
        return v
      }
      """;

    Compilation compilation = CheckerTestSupport.Compile(Code);
    Diagnostic err = Assert.Single(CheckerTestSupport.CheckerErrors(compilation));
    Assert.Equal(DiagnosticCodes.YieldOutsideValidContext, err.Code);
    Assert.Contains("'yield' is only allowed inside a recovery block", err.Message);
    CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);
  }

  [Fact]
  public void ConformanceErrorsCases_PositiveAndErrTextsMatchSpec() {
    string repoRoot = ParserTestHelpers.FindRepositoryRoot();

    string[] positiveCases = [
      "conformance/errors/bubble-chain.pino",
      "conformance/errors/bubble-option.pino",
      "conformance/errors/bubble-result-failure.pino",
      "conformance/errors/bubble-result-success.pino",
      "conformance/errors/main-bubble.pino",
      "conformance/errors/main-failure.pino",
      "conformance/errors/main-none.pino",
      "conformance/errors/main-success.pino",
      "conformance/errors/or-call-result.pino",
      "conformance/errors/or-failure-err-yield.pino",
      "conformance/errors/or-option.pino",
      "conformance/errors/or-panic.pino",
      "conformance/errors/or-postfix-precedence.pino",
      "conformance/errors/or-return.pino",
      "conformance/errors/or-success.pino",
      "conformance/errors/panic-as-value.pino",
      "conformance/errors/panic-exit-code.pino",
      "conformance/errors/panic-halts.pino",
      "conformance/errors/panic-nested.pino",
      "conformance/errors/prelude-result-option.pino",
      "conformance/errors/result-match.pino",
      "conformance/errors/result-void-success.pino",
      "conformance/errors/unified-error-union.pino",
    ];

    foreach (string relPath in positiveCases) {
      string code = File.ReadAllText(Path.Combine(repoRoot, relPath));
      Compilation compilation = CheckerTestSupport.Compile(code, relPath);
      Assert.True(
          compilation.Diagnostics.IsEmpty,
          $"{relPath} had diagnostics: {string.Join("; ", compilation.Diagnostics.Select(static d => $"{d.Code}: {d.Message}"))}");
      CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);
    }

    (string BasePath, string ExpectedCode)[] errCases = [
      ("conformance/errors/err-bubble-outside-function", DiagnosticCodes.BubbleOutsideCompatibleFunction),
      ("conformance/errors/err-bubble-incompatible-return", DiagnosticCodes.BubbleOutsideCompatibleFunction),
      ("conformance/errors/err-bubble-error-type-mismatch", DiagnosticCodes.BubbleErrorTypeMismatch),
      ("conformance/errors/err-bubble-second-call-mismatch", DiagnosticCodes.BubbleErrorTypeMismatch),
      ("conformance/errors/err-bubble-option-in-result", DiagnosticCodes.BubbleOutsideCompatibleFunction),
      ("conformance/errors/err-bubble-non-result", DiagnosticCodes.BubbleOnNonResultOrOption),
      ("conformance/errors/err-yield-outside-or", DiagnosticCodes.YieldOutsideValidContext),
      ("conformance/errors/err-yield-type-mismatch", DiagnosticCodes.YieldTypeMismatch),
      ("conformance/errors/err-result-void-success-parens", DiagnosticCodes.VariantArgumentMismatch),
    ];

    foreach ((string basePath, string expectedCode) in errCases) {
      string code = File.ReadAllText(Path.Combine(repoRoot, basePath + ".pino"));
      string expectedText = File.ReadAllText(Path.Combine(repoRoot, basePath + ".err")).Trim();
      Compilation compilation = CheckerTestSupport.Compile(code, basePath + ".pino");
      Diagnostic[] errors = CheckerTestSupport.CheckerErrors(compilation);
      Assert.NotEmpty(errors);
      Assert.Contains(errors, d => d.Code == expectedCode);
      Assert.True(
          errors.Any(d => d.Message.Contains(expectedText, StringComparison.Ordinal)),
          $"{basePath}.pino expected '{expectedText}', got: {string.Join(" | ", errors.Select(static d => d.Message))}");
      CheckerGenericsTests.AssertNoTypeVariablesEscape(compilation);
    }
  }
}
