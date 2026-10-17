using AVAGhostsNGoblins.Engine;

namespace AVAGhostsNGoblins.Tests;

/// <summary>The four stages: built to be got through, and got through by the computer knight.</summary>
public class StageTests(ITestOutputHelper output)
{
    public static TheoryData<int> Stages() => [.. Enumerable.Range(0, StageBook.All.Length)];

    /// <summary>The top of the ground along the main path, column by column (rows; Rows for none): ledges up above don't count.</summary>
    private static int Surface(StageDef s, int c)
    {
        for (int r = Field.Rows - 1; r >= 0; r--)
            if (s.At(c, r) is not (Tile.Solid or Tile.Grave)) return r + 1;
        return 0;
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public void Every_step_up_can_be_jumped_and_every_gap_without_a_ledge_to_ride_is_short(int index)
    {
        var s = StageBook.All[index];
        int gap = 0;
        for (int c = 1; c < s.Cols; c++)
        {
            int here = Surface(s, c), before = Surface(s, c - 1);
            if (here < Field.Rows && before < Field.Rows) Assert.True(before - here <= 3, $"{s.Name}: a wall {before - here} squares high at column {c}");
            bool ridden = s.Glides.Any(g => c * Field.Tile >= Math.Min(g.X0, g.X1) - Field.Tile && c * Field.Tile <= Math.Max(g.X0, g.X1) + g.Width * Field.Tile);
            gap = here >= Field.Rows - 1 && !ridden ? gap + 1 : 0;
            Assert.True(gap <= 3, $"{s.Name}: a gap too wide to jump at column {c}");
        }
        Assert.True(Surface(s, 2) < Field.Rows, "ground at the start");
        Assert.True(Surface(s, (int)(s.Checkpoint / Field.Tile)) < Field.Rows, "ground at the checkpoint");
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public void Every_stage_is_cleared_by_the_computer_knight(int index)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaghostsngoblins-tests-" + Guid.NewGuid().ToString("N"))), 1) { Silent = true };
        g.Go(index);
        g.SetLives(10);
        g.Pilot.Reset();
        int t = 0, lost = 0;
        var last = g.Phase;
        for (; t < 60 * 400 && g.State == GameState.Playing && !(g.Phase is Phase.Clear or Phase.Ending); t++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Phase == Phase.Dying && last != Phase.Dying) lost++;
            last = g.Phase;
        }
        output.WriteLine($"{StageBook.All[index].Name}: {g.Phase} after {t / 60.0:0}s, {lost} lives lost");
        Assert.True(g.Phase is Phase.Clear or Phase.Ending, $"not cleared: {g.Phase}, {g.World.Cause}, at column {(int)(g.World.Knight.X / Field.Tile)}");
        // (Knocked back into a pit, now and then: it happens to the best of knights.)
        Assert.True(lost <= 2, $"{lost} lives lost");
    }
}
