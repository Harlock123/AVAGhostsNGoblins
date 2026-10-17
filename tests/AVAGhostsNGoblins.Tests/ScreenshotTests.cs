using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAGhostsNGoblins.Engine;
using AVAGhostsNGoblins.Views;
using Controls = AVAGhostsNGoblins.Engine.Controls;

namespace AVAGhostsNGoblins.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set GOBLINS_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaghostsngoblins-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("GOBLINS_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAGhostsNGoblins-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("SIR", 98300, 8);
        g.HighScores.Insert("AVA", 41500, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A stage played by the computer knight until something worth showing.</summary>
    private static void Play(int stage, string name, Func<GameEngine, bool> moment, int seconds = 180)
    {
        var (w, c, g) = Open();
        g.Go(stage);
        g.SetLives(20);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void Zombies_rising() => Play(0, "graveyard", g => g.World.Enemies.Count(e => e.Kind == Foe.Zombie && e.State == 0 && e.T > 0.4) >= 1 && g.World.Shots.Count >= 1);

    [AvaloniaFact]
    public void A_jump() => Play(0, "jump", g => !g.World.Knight.Ground && g.World.Knight.Vy > -200 && g.World.Knight.Vy < 0 && g.World.Enemies.Count >= 2);

    [AvaloniaFact]
    public void The_cyclops() => Play(0, "cyclops", g => g.World.Enemies.Any(e => e.Kind == Foe.Cyclops && e.State == 1) && g.World.Shots.Count >= 1);

    [AvaloniaFact]
    public void The_village() => Play(1, "village", g => g.World.Knight.X > 40 * Field.Tile && g.World.Enemies.Any(e => e.Kind == Foe.Ghost));

    [AvaloniaFact]
    public void The_gargoyle() => Play(3, "gargoyle", g => g.World.Enemies.Any(e => e.Kind == Foe.Gargoyle && Math.Abs(e.X - g.World.Knight.X) < 420 && e.X > g.World.CamX + 60 && e.X < g.World.CamX + Field.Width - 60));

    [AvaloniaFact]
    public void The_caverns() => Play(2, "caverns", g => g.World.Platforms.Any(p => Math.Abs(p.X - g.World.Knight.X) < 80) && !g.World.Knight.Ground);

    [AvaloniaFact]
    public void The_serpent() => Play(2, "serpent", g => g.World.Enemies.Any(e => e.Kind == Foe.Serpent && e.T > 3) && g.World.Shots.Count >= 1);

    [AvaloniaFact]
    public void Malgrim() => Play(3, "malgrim", g => g.World.Enemies.Any(e => e.Kind == Foe.Malgrim && e.T > 2) && g.World.Missiles.Count >= 3);

    [AvaloniaFact]
    public void Without_armour()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, g => g.World.Knight.X > 20 * Field.Tile, 60));
        g.World.Knight.Armour = false;
        g.World.Knight.Hurt = 0;
        for (int i = 0; i < 60; i++) { g.Tick(g.Pilot.Decide(g)); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "no-armour");
    }

    [AvaloniaFact]
    public void Stage_clear() => Play(0, "clear", g => g.Phase == Phase.Clear && g.PhaseTime > 1.5);
}
