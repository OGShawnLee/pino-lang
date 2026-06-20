using System.Collections.Generic;

namespace Pino;

public class Chunk {
  public List<byte> Code { get; } = new();
  public List<object?> Constants { get; } = new();
  public List<GlobalBox?> GlobalBoxes { get; } = new();

  public void Write(byte b) {
    Code.Add(b);
  }

  public void Write(OperationCode op) {
    Code.Add((byte)op);
  }

  public void WriteShort(ushort val) {
    Code.Add((byte)((val >> 8) & 0xFF));
    Code.Add((byte)(val & 0xFF));
  }

  public int AddConstant(object? value) {
    // Basic deduplication of constants
    for (int i = 0; i < Constants.Count; i++) {
      if (Equals(Constants[i], value)) {
        return i;
      }
    }
    Constants.Add(value);
    GlobalBoxes.Add(null);
    return Constants.Count - 1;
  }
}
