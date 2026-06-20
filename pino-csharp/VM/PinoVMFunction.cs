using System.Collections.Generic;

namespace Pino;

public class PinoVMFunction : IPinoCallable {
  public string Name { get; }
  public int Arity { get; }
  public Chunk Chunk { get; }

  public PinoVMFunction(string name, int arity, Chunk chunk) {
    Name = name;
    Arity = arity;
    Chunk = chunk;
  }

  public object? Call(Evaluator evaluator, List<object?> arguments) {
    // If called from the classic AST evaluator, execute using the VM!
    var vm = new VM(evaluator, evaluator.Globals);
    return vm.Execute(this, arguments);
  }
}
