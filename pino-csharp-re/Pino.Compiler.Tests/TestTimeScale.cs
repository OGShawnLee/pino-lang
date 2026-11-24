namespace Pino.Compiler.Tests;

/// <summary>Scales the hang-guard limits of selected tests by the <c>PINO_TEST_TIME_SCALE</c> environment variable (default 1).</summary>
internal static class TestTimeScale {
  private const string VariableName = "PINO_TEST_TIME_SCALE";

  /// <summary>The factor read from <c>PINO_TEST_TIME_SCALE</c>; 1 when the variable is unset, not a number, or not positive.</summary>
  public static double Factor { get; } = ReadFactor();

  /// <summary>Returns <paramref name="limit"/> multiplied by <see cref="Factor"/>.</summary>
  public static TimeSpan Apply(TimeSpan limit) => limit * Factor;

  private static double ReadFactor() {
    string? raw = Environment.GetEnvironmentVariable(VariableName);
    if (double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value)
        && value > 0 && double.IsFinite(value)) {
      return value;
    }
    return 1;
  }
}
