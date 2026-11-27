namespace Pino.Compiler.Diagnostics;

/// <summary>Longer, human-written explanations for diagnostic codes, shown by <c>pino explain</c>.</summary>
public static class DiagnosticExplanations {
  private static readonly Dictionary<string, string> s_explanations = new(StringComparer.OrdinalIgnoreCase) {
    [DiagnosticCodes.SemicolonIgnored] = """
      PN1018: Pino doesn't use semicolons

      Pino ends a statement at the end of a line, or where the next statement
      begins, so it never needs a semicolon. If you type one out of habit, Pino
      ignores it and shows this hint, and your program still runs.

          val a = 1;   # the ';' is ignored, with this hint
          val a = 1    # the same program, without the hint

      Why: Pino was designed so that a semicolon can never be the reason your
      program fails. It was made for someone who once failed a university course
      over one.
      """,
    [DiagnosticCodes.LostWrite] = """
      PN3552: Mutating a copied variable doesn't update the original

      In Pino, structs, vectors, maps, and sets are value types. Assigning an
      element or field into a local variable makes an independent copy:

          var item = xs[i]
          item:hp -= 1        # modifies only the copy 'item', not 'xs[i]'

      To update the original value, choose one of three fixes:

        1. Mutate the original place directly:
           xs[i]:hp -= 1

        2. Write the modified copy back:
           var item = xs[i]
           item:hp -= 1
           xs[i] = item

        3. Pass the original place to a 'mut' function:
           take_damage(mut xs[i])
      """,
  };

  /// <summary>Looks up the explanation for a code, ignoring case.</summary>
  public static bool TryGet(string code, out string explanation) {
    ArgumentNullException.ThrowIfNull(code);
    return s_explanations.TryGetValue(code, out explanation!);
  }
}
