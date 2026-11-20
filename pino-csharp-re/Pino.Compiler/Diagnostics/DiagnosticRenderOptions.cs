namespace Pino.Compiler.Diagnostics;

public sealed record DiagnosticRenderOptions {
  public bool Color { get; init; }
  public DiagnosticVoice Voice { get; init; } = DiagnosticVoice.Pino;
  public int TabWidth { get; init; } = 4;
  public int MaxLineWidth { get; init; } = 120;
  public IReadOnlyDictionary<string, string>? Notes { get; init; }
}
