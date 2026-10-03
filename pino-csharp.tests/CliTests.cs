using System;
using System.IO;
using Xunit;
using pino_csharp;

namespace Pino.Tests;

public class CliTests {
  [Fact]
  public void TestInitProjectScaffolding() {
    var tempDir = Path.Combine(Path.GetTempPath(), "pino_test_init_" + Guid.NewGuid().ToString("N"));
    var originalOut = Console.Out;
    try {
      using var sw = new StringWriter();
      Console.SetOut(sw);
      Program.InitProject("my_app", tempDir);

      var projectDir = Path.Combine(tempDir, "my_app");
      Assert.True(Directory.Exists(projectDir));
      Assert.True(File.Exists(Path.Combine(projectDir, "main.pino")));
      Assert.True(File.Exists(Path.Combine(projectDir, "modules", "utils.pino")));
      Assert.True(File.Exists(Path.Combine(projectDir, "test", "main_test.pino")));
      Assert.True(File.Exists(Path.Combine(projectDir, ".gitignore")));

      // Verify that main.pino can be parsed and type checked
      var mainPath = Path.Combine(projectDir, "main.pino");
      var program = Parser.ParseFile(mainPath);
      var checker = new Checker { CurrentFilePath = mainPath };
      checker.Check(program);

      // Verify that test/main_test.pino can be parsed and executed
      var testPath = Path.Combine(projectDir, "test", "main_test.pino");
      var testProgram = Parser.ParseFile(testPath);
      var testChecker = new Checker { CurrentFilePath = testPath };
      testChecker.Check(testProgram);

      // Verify overwrite protection
      var mainContentBefore = File.ReadAllText(mainPath);
      Program.InitProject("my_app", tempDir); // Run again
      var mainContentAfter = File.ReadAllText(mainPath);
      Assert.Equal(mainContentBefore, mainContentAfter);
    } finally {
      Console.SetOut(originalOut);
      if (Directory.Exists(tempDir)) {
        try { Directory.Delete(tempDir, true); } catch { /* Ignore */ }
      }
    }
  }
}
