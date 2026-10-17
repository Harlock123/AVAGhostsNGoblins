using AVAGhostsNGoblins.Engine;

namespace AVAGhostsNGoblins.Tests;

/// <summary>
/// How the computer knight gets on in each stage (set GOBLINS_PROBE to run it, for tuning: "1" for the pilot,
/// "lazy" for a knight who only walks on and throws).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_knight_does()
    {
        string? mode = Environment.GetEnvironmentVariable("GOBLINS_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        for (int stage = 0; stage < StageBook.All.Length; stage++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "goblins-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go(stage);
            g.SetLives(30);
            g.Pilot.Reset();
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            int t = 0, armour = 0;
            var last = g.Phase;
            bool wasArmoured = true;
            for (; t < 60 * 900 && g.State == GameState.Playing && g.Cleared == 0 && g.World.StageIndex == stage; t++)
            {
                var c = mode == "lazy" ? new Controls(1, 0, false, t % 20 < 10) : g.Pilot.Decide(g);
                g.Tick(c);
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) { var key = $"{g.World.Cause} at {(int)(g.World.Knight.X / Field.Tile)}"; deaths[key] = deaths.GetValueOrDefault(key) + 1; }
                if (wasArmoured && !g.World.Knight.Armour) armour++;
                wasArmoured = g.World.Knight.Armour;
                last = g.Phase;
            }
            output.WriteLine($"{StageBook.All[stage].Name}: {(g.Cleared > 0 ? "CLEARED" : "not cleared")} after {t / 60}s, {deaths.Values.Sum()} deaths, armour lost {armour}x, reached col {(int)(g.World.Knight.X / Field.Tile)}/{StageBook.All[stage].Cols}, score {g.Score}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Take(8).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}
