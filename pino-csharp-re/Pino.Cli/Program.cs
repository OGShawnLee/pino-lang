using System.Text;

namespace Pino.Cli;

internal static class Program {
  private static int Main(string[] args) {
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    var environment = new CliEnvironment(
        Environment.GetEnvironmentVariable,
        Console.IsOutputRedirected,
        ConsoleMode.TryEnableAnsi());

    return CliApp.Run(args, Console.Out, Console.Error, environment);
  }
}
