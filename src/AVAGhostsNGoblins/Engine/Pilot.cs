namespace AVAGhostsNGoblins.Engine;

/// <summary>
/// The computer knight, for the demonstration and the tests. Every few steps it tries a handful of moves (on,
/// back, stand, jump on, jump in place, crouch, climb), each followed by the best of a few ways on, on copies of
/// the world, and takes the one that keeps it alive (and its armour on) and gets it furthest, throwing all the
/// while when something's in front of it. At the end of a stage, the boss's hurt counts instead of distance.
/// </summary>
public sealed class Pilot
{
    private Controls _choice;
    private int _tick, _held;
    private readonly List<(int Dx, int Dy, bool Jump)> _moves =
        [(1, 0, false), (1, 0, true), (0, 0, false), (0, 0, true), (-1, 0, false), (-1, 0, true), (0, 1, false), (0, -1, false)];

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _tick = 0;
        _held = 0;
        _choice = default;
        Doing = "";
    }

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var w = g.World;
        if (g.Phase != Phase.Play || w.Knight.Dead) { _held = 0; return default; }
        if (_held > 0)
        {
            _held--;
            // A jump is pressed once; the throw comes and goes.
            return _choice with { Jump = false, Fire = Fire(w) && _tick % 2 == 0 };
        }
        double best = double.MinValue;
        (int Dx, int Dy, bool Jump) pick = (1, 0, false);
        foreach (var m in _moves)
        {
            double value = Try(w, m, 12);
            if (value > best) { best = value; pick = m; }
        }
        _choice = new Controls(pick.Dx, pick.Dy, pick.Jump, false);
        _held = 7;
        Doing = pick switch { (1, 0, false) => "on", (1, 0, true) => "jump on", (0, 0, false) => "stand", (0, 0, true) => "jump up", (-1, 0, false) => "back", (-1, 0, true) => "jump back", (0, 1, false) => "crouch", _ => "climb" };
        return _choice with { Fire = Fire(w) };
    }

    /// <summary>Something to throw at: in front, at about his height, not too far.</summary>
    private static bool Fire(World w)
    {
        var k = w.Knight;
        foreach (var e in w.Enemies)
        {
            double dx = (e.X - k.X) * k.Facing;
            if (dx > 0 && dx < 500 && e.Y > k.Y - 260 && e.Y - e.Height < k.Y + 10) return true;
        }
        foreach (var i in w.Items)
            if (i.Pot && (i.X - k.X) * k.Facing is > 0 and < 300 && Math.Abs(i.Y - k.Y) < 40) return true;
        return false;
    }

    /// <summary>The move for a while, then the best of a few ways on: how good is where it leaves him?</summary>
    private double Try(World start, (int Dx, int Dy, bool Jump) m, int steps)
    {
        var w = start.Copy();
        Run(w, m, steps);
        double best = double.MinValue;
        foreach (var next in new[] { (1, 0, false), (0, 0, false), (1, 0, true), (-1, 0, false) })
        {
            var w2 = w.Copy();
            if (next == (1, 0, false)) Onward(w2, 54);
            else Run(w2, next, 54);
            best = Math.Max(best, Value(start, w2));
        }
        return best;
    }

    /// <summary>Walking on, and jumping at the last moment when a gap or a wall comes up ahead.</summary>
    private static void Onward(World w, int steps)
    {
        for (int i = 0; i < steps && !w.Knight.Dead; i++)
        {
            var k = w.Knight;
            bool jump = false;
            if (k.Ground && !k.Climbing)
            {
                double ahead = k.X + Knight.HalfWidth + 4;
                var s = w.Stage;
                bool gap = s.AtPoint(ahead, k.Y + 4) is Tile.Empty or Tile.Hazard && !w.Platforms.Any(p => ahead > p.X && ahead < p.X + p.Width && Math.Abs(p.Y - k.Y) < 6);
                bool wall = s.AtPoint(ahead, k.Y - 10) is Tile.Solid or Tile.Grave;
                jump = (gap || wall) && !k.JumpPrev;
            }
            Sim.Step(w, new Controls(1, 0, jump, Fire(w) && i % 2 == 0));
        }
    }

    private static void Run(World w, (int Dx, int Dy, bool Jump) m, int steps)
    {
        for (int i = 0; i < steps && !w.Knight.Dead; i++)
            Sim.Step(w, new Controls(m.Dx, m.Dy, m.Jump && i == 0, Fire(w) && i % 2 == 0));
    }

    private static double Value(World before, World after)
    {
        var k = after.Knight;
        if (k.Dead) return -1e6 + after.Clock;
        double v = 0;
        // Below the level of the ground: down a pit, as good as dead.
        if (k.Y > Field.Top + (Field.Rows - 2) * Field.Tile) v -= 500000;
        if (before.Knight.Armour && !k.Armour) v -= 5000;
        if (!before.Knight.Armour && k.Hurt > 0 && before.Knight.Hurt <= 0) v -= 5000;
        v += (after.Score - before.Score) * 0.5;
        // The key: the sooner the better (or it would put off fetching it for ever).
        if (after.KeyTaken) v += 100000 - (after.KeyAt - before.Clock) * 2000;
        // On into the fight at the end, rather than waiting short of it for ever.
        if (after.BossUp && !before.BossUp) v += 8000;
        if (after.BossUp)
        {
            // At the end: hurt the boss, and keep clear of it.
            int hp(World x) => x.Enemies.Where(e => e.Boss).Sum(e => e.Hp);
            if (before.BossUp) v += (hp(before) - hp(after)) * 400;
            var boss = after.Enemies.FirstOrDefault(e => e.Boss);
            if (boss != null) v -= Math.Max(0, 260 - Math.Abs(boss.X - k.X)) * 2;
            if (after.BossDown && after.Items.FirstOrDefault(i => i.Key) is { } key) v -= Math.Abs(key.X - k.X) * 3;
        }
        else v += (k.X - before.Knight.X) * 3;
        // Up a ladder is a way on too, if it leads somewhere.
        if (k.Climbing) v += 5;
        return v;
    }
}
