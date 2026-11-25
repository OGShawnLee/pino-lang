using System;

namespace PinoBenchmarks;

public static class Program {
  public static int Main(string[] args) {
    if (args.Length == 0) {
      Console.Error.WriteLine("Usage: Benchmarks <benchmark-name> [--quick]");
      return 2;
    }

    string name = args[0];
    bool quick = (args.Length > 1 && args[1] == "--quick") || File.Exists("benchmarks/data/quick");

    switch (name) {
      case "hello":
        Hello.Run(quick);
        return 0;
      case "fib":
        Fib.Run(quick);
        return 0;
      case "binary-trees":
        BinaryTrees.Run(quick);
        return 0;
      case "nbody":
        NBody.Run(quick);
        return 0;
      case "spectral-norm":
        SpectralNorm.Run(quick);
        return 0;
      case "mandelbrot":
        Mandelbrot.Run(quick);
        return 0;
      case "sort":
        Sort.Run(quick);
        return 0;
      case "word-count":
        WordCount.Run(quick);
        return 0;
      case "string-build":
        StringBuild.Run(quick);
        return 0;
      case "regex":
        RegexBench.Run(quick);
        return 0;
      case "lexer":
        Lexer.Run(quick);
        return 0;
      default:
        Console.Error.WriteLine($"Unknown benchmark: {name}");
        return 2;
    }
  }
}
