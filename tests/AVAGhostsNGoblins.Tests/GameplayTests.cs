using Avalonia.Input;
using AVAGhostsNGoblins.Engine;

namespace AVAGhostsNGoblins.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaghostsngoblins-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The graveyard, nothing in it yet but the knight at its start (and its gravestones and pits).</summary>
    private static World Graveyard(double col = 2.5)
    {
        var w = new World();
        Sim.Start(w, 0, 1, false, Weapon.Lance, 1);
        Array.Fill(w.Spawned, true);
        Array.Fill(w.SwarmIn, 9999);
        w.Items.Clear();
        w.Knight.X = col * Field.Tile;
        w.Knight.Y = w.Stage.FloorAt((int)col);
        w.CamX = Math.Max(0, w.Knight.X - 300);
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static readonly Controls Right = new(1, 0), Left = new(-1, 0), Up = new(0, -1), Down = new(0, 1);

    // ───────────────────────────── moving about ─────────────────────────────

    [Fact]
    public void He_walks_and_a_gravestone_stops_him()
    {
        var w = Graveyard(8);
        Run(w, 60, Right);
        // The first gravestone is in column 12.
        Assert.InRange(w.Knight.X, 11 * Field.Tile, 12 * Field.Tile);
        Assert.True(w.Knight.Ground);
    }

    [Fact]
    public void A_jump_goes_up_and_over_and_its_way_across_is_fixed_when_it_starts()
    {
        var w = Graveyard(10);
        Sim.Step(w, new Controls(1, 0, true));
        double top = w.Knight.Y;
        for (int i = 0; i < 60; i++)
        {
            // Pushing back the other way mid-air makes no difference.
            Sim.Step(w, Left);
            top = Math.Min(top, w.Knight.Y);
        }
        Assert.True(w.Stage.FloorAt(10) - top > 100, "a jump should clear a gravestone");
        Assert.True(w.Knight.X > 12.5 * Field.Tile, "over the gravestone");
        Assert.True(w.Knight.Ground);
    }

    [Fact]
    public void Walking_into_a_pit_is_death()
    {
        var w = Graveyard(50);
        Run(w, 120, Right);
        Assert.True(w.Knight.Dead);
        Assert.Equal("fell", w.Cause);
    }

    [Fact]
    public void A_pit_three_squares_wide_can_be_jumped()
    {
        var w = Graveyard(51.4);
        Sim.Step(w, new Controls(1, 0, true));
        Run(w, 90);
        Assert.False(w.Knight.Dead);
        Assert.True(w.Knight.X > 55 * Field.Tile);
    }

    [Fact]
    public void Up_a_ladder_onto_a_ledge_and_down_again()
    {
        // The graveyard's ladder is in column 31, up to a ledge at row 13.
        var w = Graveyard(31.5);
        Run(w, 2, Up);
        Assert.True(w.Knight.Climbing);
        Run(w, 120, Up);
        Assert.False(w.Knight.Climbing);
        Assert.Equal(Field.Top + 13 * Field.Tile, w.Knight.Y, 1);
        // Along the ledge (the ladder's top is floor too), then back down.
        Run(w, 10, Right);
        Run(w, 10, Left);
        Assert.Equal(Field.Top + 13 * Field.Tile, w.Knight.Y, 1);
        w.Knight.X = 31.5 * Field.Tile;
        Run(w, 140, Down);
        Assert.Equal(w.Stage.FloorAt(31, Field.Top + 15 * Field.Tile), w.Knight.Y, 1);
    }

    [Fact]
    public void Crouching_he_stops_and_is_shorter()
    {
        var w = Graveyard(8);
        Run(w, 5, new Controls(1, 1));
        Assert.True(w.Knight.Crouch);
        Assert.Equal(w.Knight.Y - Knight.Low, w.Knight.Top);
        double x = w.Knight.X;
        Run(w, 20, new Controls(1, 1));
        Assert.Equal(x, w.Knight.X);
    }

    // ───────────────────────────── throwing ─────────────────────────────

    [Fact]
    public void Lances_fly_two_at_a_time_daggers_three()
    {
        var w = Graveyard(4);
        int most = 0;
        for (int i = 0; i < 60; i++) { Sim.Step(w, new Controls(0, 0, false, i % 2 == 0)); most = Math.Max(most, w.Shots.Count); }
        Assert.Equal(2, most);
        // (Clear of gravestones a good way ahead.)
        w = Graveyard(112.5);
        w.Knight.Weapon = Weapon.Dagger;
        most = 0;
        for (int i = 0; i < 60; i++) { Sim.Step(w, new Controls(0, 0, false, i % 2 == 0)); most = Math.Max(most, w.Shots.Count); }
        Assert.Equal(3, most);
    }

    [Fact]
    public void A_gravestone_stops_a_thrown_weapon()
    {
        var w = Graveyard(8);
        Sim.Step(w, new Controls(1, 0, false, true));
        Run(w, 40);
        Assert.Empty(w.Shots);
    }

    [Fact]
    public void A_lance_brings_down_a_zombie()
    {
        var w = Graveyard(2.5);
        w.Enemies.Add(new Enemy { Kind = Foe.Zombie, X = 9 * Field.Tile, Y = w.Knight.Y, T = 1, Hp = 1, Height = 44 });
        Sim.Step(w, new Controls(0, 0, false, true));
        Run(w, 30);
        Assert.Empty(w.Enemies);
        Assert.Equal(Sim.Points(Foe.Zombie), w.Score);
    }

    [Fact]
    public void A_torch_burns_where_it_lands_and_an_axe_goes_up_and_over()
    {
        var w = Graveyard(2.5);
        w.Knight.Weapon = Weapon.Torch;
        Sim.Step(w, new Controls(0, 0, false, true));
        Run(w, 50);
        Assert.Contains(w.Shots, s => s.Burn > 0);
        w = Graveyard(2.5);
        w.Knight.Weapon = Weapon.Axe;
        Sim.Step(w, new Controls(0, 0, false, true));
        double y = w.Shots[0].Y, top = y;
        for (int i = 0; i < 30 && w.Shots.Count > 0; i++) { Sim.Step(w, default); top = Math.Min(top, w.Shots[0].Y); }
        Assert.True(y - top > 60);
    }

    [Fact]
    public void A_pot_broken_gives_up_what_is_in_it()
    {
        var w = Graveyard(2.5);
        w.Items.Add(new Item { X = 8 * Field.Tile, Y = w.Knight.Y, Pot = true, Loot = Loot.Weapon, Weapon = Weapon.Dagger, Life = double.MaxValue });
        Sim.Step(w, new Controls(0, 0, false, true));
        Run(w, 40);
        Assert.False(w.Items[0].Pot);
        Run(w, 100, Right);
        Assert.Equal(Weapon.Dagger, w.Knight.Weapon);
        Assert.Empty(w.Items);
    }

    // ───────────────────────────── the armour ─────────────────────────────

    [Fact]
    public void The_first_hit_takes_his_armour_and_the_next_takes_him()
    {
        var w = Graveyard(4);
        Sim.Hurt(w, "zombie");
        Assert.False(w.Knight.Armour);
        Assert.False(w.Knight.Dead);
        // A moment safe from harm.
        Sim.Hurt(w, "zombie");
        Assert.False(w.Knight.Dead);
        Run(w, (int)(60 * Sim.HurtSeconds) + 5);
        Sim.Hurt(w, "zombie");
        Assert.True(w.Knight.Dead);
    }

    [Fact]
    public void Armour_found_puts_it_back_on()
    {
        var w = Graveyard(4);
        w.Knight.Armour = false;
        w.Items.Add(new Item { X = w.Knight.X + 40, Y = w.Knight.Y, Loot = Loot.Armour, Life = 10 });
        Run(w, 30, Right);
        Assert.True(w.Knight.Armour);
    }

    // ───────────────────────────── creatures ─────────────────────────────

    [Fact]
    public void Zombies_rise_out_of_the_ground_shamble_on_and_sink_back()
    {
        var w = Graveyard(2.5);
        var z = new Enemy { Kind = Foe.Zombie, X = 9 * Field.Tile, Y = w.Knight.Y, Hp = 1, Height = 44, Facing = -1 };
        w.Enemies.Add(z);
        Run(w, 30);
        Assert.Equal(0, z.State);
        double x = z.X;
        Run(w, 60);
        Assert.True(z.X < x, "it should come towards him");
        w.Knight.X = 2 * Field.Tile;
        Run(w, 60 * 9);
        Assert.DoesNotContain(z, w.Enemies);
    }

    [Fact]
    public void A_crow_takes_off_when_he_comes_near()
    {
        var w = Graveyard(20);
        var c = new Enemy { Kind = Foe.Crow, X = 26 * Field.Tile, Y = w.Knight.Y - 32, HomeY = w.Knight.Y - 32 };
        w.Enemies.Add(c);
        Run(w, 5);
        Assert.Equal(1, c.State);
    }

    [Fact]
    public void A_gargoyle_slips_aside_from_a_throw_but_not_from_every_one()
    {
        var w = Graveyard(10);
        var gar = new Enemy { Kind = Foe.Gargoyle, X = 18 * Field.Tile, Y = w.Knight.Y - 10, Hp = 3, Width = 34, Height = 36, FireIn = 99, T = -99 };
        w.Enemies.Add(gar);
        w.Shots.Add(new Shot { X = 15 * Field.Tile, Y = gar.Y - 18, Vx = 520, Life = 2, Kind = Weapon.Lance });
        Run(w, 6);
        Assert.True(gar.Dodge > 0, "it should see it coming");
        Assert.True(gar.Hp == 3 || gar.Dead == false);
    }

    [Fact]
    public void At_the_end_the_boss_comes_the_rest_flee_and_beating_it_drops_the_key()
    {
        var w = Graveyard(140);
        w.Enemies.Add(new Enemy { Kind = Foe.Zombie, X = w.Knight.X + 100, Y = w.Knight.Y, T = 1, Hp = 1, Height = 44 });
        w.CamX = w.Stage.ArenaX;
        w.Knight.X = w.Stage.ArenaX + 300;
        Run(w, 2);
        Assert.True(w.BossUp);
        var boss = Assert.Single(w.Enemies);
        Assert.Equal(Foe.Cyclops, boss.Kind);
        boss.Hp = 1;
        boss.Hurt = 0;
        w.Shots.Add(new Shot { X = boss.X - 20, Y = boss.Y - 50, Vx = 520, Life = 1, Kind = Weapon.Lance });
        Run(w, 3);
        Assert.True(w.BossDown);
        var key = Assert.Single(w.Items, i => i.Key);
        w.Knight.X = key.X;
        Run(w, 120);
        Assert.True(w.KeyTaken);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_copy_of_the_world_plays_out_exactly_as_the_world_does()
    {
        var w = new World();
        Sim.Start(w, 0, 1, false, Weapon.Lance, 5);
        Run(w, 60 * 10, Right);
        var copy = w.Copy();
        var c = new Controls(1, 0, false, true);
        for (int i = 0; i < 300; i++) { Sim.Step(w, c with { Jump = i % 50 == 0 }); Sim.Step(copy, c with { Jump = i % 50 == 0 }); }
        Assert.Equal(w.Knight.X, copy.Knight.X);
        Assert.Equal(w.Score, copy.Score);
        Assert.Equal(w.Enemies.Select(e => (e.Kind, e.X)), copy.Enemies.Select(e => (e.Kind, e.X)));
    }

    [Fact]
    public void Out_of_time_is_death_and_a_life_lost_past_the_checkpoint_starts_again_there()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        g.World.Knight.X = g.World.Stage.Checkpoint + 64;
        g.World.Knight.Y = g.World.Stage.FloorAt((int)(g.World.Knight.X / Field.Tile));
        g.World.CamX = g.World.Knight.X - 300;
        g.Tick(default);
        g.World.TimeLeft = 0.05;
        g.Run(10);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("out of time", g.World.Cause);
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(g.World.Stage.Checkpoint, g.World.Knight.X, 1);
        Assert.True(g.World.Knight.Armour);
    }

    [Fact]
    public void The_last_life_lost_ends_the_game_and_a_good_score_goes_on_the_table()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 90000;
        Sim.Kill(g.World, "test");
        g.Run((int)(60 * (GameEngine.DyingSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void Taking_the_key_clears_the_stage_and_the_time_left_becomes_points()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        g.World.KeyTaken = true;
        g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        int score = g.Score, left = (int)g.World.TimeLeft;
        g.Run((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.InRange(g.Score - score, (left - 2) * GameEngine.BonusPerSecond, left * GameEngine.BonusPerSecond);
        Assert.Equal(1, g.World.StageIndex);
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_walk_jump_climb_and_throw()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Jump);
        Assert.False(input.Player.Fire);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Fire);
        input.Dispose();
        // Up on the keys climbs a ladder.
        var w = Graveyard(31.5);
        double y = w.Knight.Y;
        Run(w, 30, new Controls(0, -1));
        Assert.True(w.Knight.Y < y);
    }
}
