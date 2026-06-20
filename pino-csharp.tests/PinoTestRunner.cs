using System;
using Pino;

namespace pino_csharp.tests;

public enum ExecutionEngine {
  TreeWalk,
  VM
}

public static class PinoTestRunner {
  public static Pino.Environment Execute(string source, ExecutionEngine engine) {
    var program = Parser.ParseProgramString(source);
    
    // Validate types statically
    var checker = new Checker();
    checker.Check(program);

    var evaluator = new Evaluator();
    var env = new Pino.Environment(evaluator.Globals);

    if (engine == ExecutionEngine.VM) {
      var compiler = new Compiler();
      var vmFn = compiler.Compile(program);
      var vm = new VM(evaluator, env);
      vm.Execute(vmFn);
    } else {
      evaluator.Execute(program, env);
    }

    return env;
  }
}
