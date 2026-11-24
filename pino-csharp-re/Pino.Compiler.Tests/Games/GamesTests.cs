using Pino.Compiler.Checking;
using Pino.Compiler.Diagnostics;
using Pino.Compiler.IR;
using Pino.Compiler.Parsing;
using Pino.Compiler.Tests.Vm;
using Pino.Compiler.Text;
using Pino.Vm;

namespace Pino.Compiler.Tests.Games;

/// <summary>
/// The three Pino games in <c>games/</c>: each must check with no errors and no warnings, and a scripted session must
/// run to the end on the VM with shadow-RC validation on. The host is a <see cref="BufferedVmHost"/>: scripted stdin,
/// captured stdout, <c>sleep</c> that returns at once, a fixed random seed, and <c>clear</c> that does nothing.
/// </summary>
public sealed class GamesTests {
  private static string GamePath(string name) => Path.Combine(VmTestHarness.RepoRoot, "games", name + ".pino");

  private static string Lines(params string[] lines) => string.Join("\n", lines) + "\n";

  private static VmRun Play(string name, string input) {
    string path = GamePath(name);
    string source = File.ReadAllText(path);
    IrModule module = VmTestHarness.BuildPostRc(source, path);
    return VmTestHarness.Execute(module, verifyRc: true, input, new SourceText(source, path));
  }

  private static void AssertCleanRun(VmRun run) {
    Assert.Null(run.Result.PanicMessage);
    Assert.Null(run.Result.RcError);
    Assert.Equal(0, run.Result.ExitCode);
    Assert.NotNull(run.Result.LeakReport);
    Assert.True(run.Result.LeakReport!.IsClean, run.Result.LeakReport.Format());
  }

  [Theory]
  [InlineData("jade_temple")]
  [InlineData("sable")]
  [InlineData("star_pino")]
  public void Game_ChecksWithNoErrorsAndNoWarnings(string name) {
    string path = GamePath(name);
    var parsed = Parser.Parse(new SourceText(File.ReadAllText(path), path));
    Compilation compilation = Compilation.Create(parsed);

    Assert.Empty(parsed.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
    Assert.Empty(compilation.Diagnostics.Where(static d => d.Severity != DiagnosticSeverity.Hint));
  }

  [Fact]
  public void JadeTemple_WalksThePlazaAndAcademyAndQuits() {
    string input = Lines(
        "Tester", // warrior name
        "1", // Monk of Reason
        "1", // visit the academy
        "2", // spar with students
        "", // continue
        "3", // investigate the garden: Master Radiant appears
        "", // continue
        "4", // return to the plaza
        "3", // talk to citizens
        "", // continue
        "5", // character status
        "", // continue
        "4", // rest at the tea house (30 CR to spend)
        "", // continue
        "6"); // exit game

    VmRun run = Play("jade_temple", input);

    AssertCleanRun(run);
    Assert.Contains("JADE TEMPLE: PATH OF THE SPIRIT FIST", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("You spar with a training dummy and local students. +5 HP.", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("Master Radiant: \"Listen, traveler... I did not retire. I was murdered!", run.Stdout, StringComparison.Ordinal);
    Assert.Contains(" Warrior: Tester", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("You drink a warm cup of Ginseng tea. You feel fully rejuvenated!", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("Abandoning valley... Goodbye.", run.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void JadeTemple_DuelWithMaestroJiang_RunsCombatThroughTheMutPlayer() {
    // Learn the murder, accuse Jiang, strike three times, then the script ends: every later prompt reads an empty
    // line, so the fight is lost and the game ends on its own.
    string input = Lines(
        "Tester", "2",
        "1", // academy
        "3", "", // garden: learn about the murder
        "1", // talk to Maestro Jiang
        "5", // accuse Jiang publicly
        "", // begin combat
        "1", "n", "", // strike, no infusion, continue
        "1", "n", "",
        "1", "n", "");

    VmRun run = Play("jade_temple", input);

    AssertCleanRun(run);
    Assert.Contains("MARTIAL COMBAT!", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("A wild Maestro Jiang attacks you!", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("You hit Maestro Jiang with Iron Fist for", run.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void Sable_PlaysOneCycleThenNeglectsTheProtocolUntilTheKineticEnding() {
    string input = Lines(
        "", // start the simulation
        "1", // proceed to resource allocation
        "2", "2", // Quezon: evacuate, seed
        "2", "2", // Nueva Alejandria: evacuate, seed
        "2", // Aszod: evacuate (CPU is now spent)
        "3", "3", "3", "3", // RAM: four sector defences
        "4", // RAM: orbital interference
        "", // continue after the cycle report
        "1", // event: authorise the link
        ""); // continue
    // The script ends here. Every later prompt reads an empty line: zero allocation, twice, which ends the game.

    VmRun run = Play("sable", input);

    AssertCleanRun(run);
    Assert.Contains("Reach arde. El Covenant ha llegado.", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("PROCESANDO", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("[FINAL: DETONACIÓN CINÉTICA Y SILENCIO]", run.Stdout, StringComparison.Ordinal);
  }

  [Fact]
  public void StarPino_DocksBuysFuelRechargesShieldsAndQuits() {
    string input = Lines(
        "", // begin
        "3", // dock at Sol Station
        "1", "5", "", // buy 5 fuel
        "2", "", // sell ore (none)
        "7", // leave the dock
        "5", "", // repair shields
        "6"); // quit

    VmRun run = Play("star_pino", input);

    AssertCleanRun(run);
    Assert.Contains("STARPINO ODYSSEY v1.3", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("DOCKED AT STATION", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("Bought 5 fuel for 25 CR.", run.Stdout, StringComparison.Ordinal);
    Assert.Contains("No ore to sell.", run.Stdout, StringComparison.Ordinal);
  }
}
