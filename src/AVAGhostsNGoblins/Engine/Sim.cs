namespace AVAGhostsNGoblins.Engine;

/// <summary>
/// How a stage plays, one 1/60 s step at a time, on a <see cref="World"/>. It touches nothing outside the world
/// it's given, so the computer player can run it on a copy to see what a move would bring.
/// </summary>
public static class Sim
{
    public const double Gravity = 1500, MaxFall = 720;
    public const double HurtSeconds = 1.6;
    public const int MoneyPoints = 500, ArmourPoints = 500;

    public static double Pace(World w) => 1 + 0.15 * (w.Round - 1);

    /// <summary>A stage begun (or begun again from its checkpoint): everything placed fresh.</summary>
    public static void Start(World w, int stage, int round, bool fromCheckpoint, Weapon weapon, int seed)
    {
        var s = StageBook.All[stage];
        w.Stage = s;
        w.StageIndex = stage;
        w.Round = round;
        w.Dice = new Dice(seed);
        double x = fromCheckpoint ? s.Checkpoint : 2.5 * Field.Tile;
        // (Looking for the floor from below the roof, where there is one.)
        w.Knight = new Knight { X = x, Y = s.FloorAt((int)(x / Field.Tile), Field.Top + 5 * Field.Tile), Ground = true, Weapon = weapon };
        w.Shots.Clear();
        w.Enemies.Clear();
        w.Missiles.Clear();
        w.Items.Clear();
        w.Effects.Clear();
        w.Platforms = s.Glides.Select(gl => new Platform { Path = gl, X = gl.X0, Y = gl.Y0 }).ToList();
        foreach (var p in s.Pots) w.Items.Add(new Item { X = p.X, Y = p.Y, Pot = true, Loot = p.Loot, Weapon = p.Weapon, Life = double.MaxValue });
        w.Spawned = s.Spawns.Select(sp => fromCheckpoint && sp.X < x + 200).ToArray();
        w.SwarmIn = s.Swarms.Select(sw => sw.Every).ToArray();
        w.TimeLeft = s.Time;
        w.Clock = 0;
        w.CamX = Math.Clamp(x - 300, 0, s.ArenaX);
        w.BossUp = w.BossDown = w.KeyTaken = false;
        w.Cause = "";
    }

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        w.Clock += dt;
        foreach (var e in w.Effects) e.Age += dt;
        w.Effects.RemoveAll(e => e.Done);
        var k = w.Knight;
        if (!k.Dead && !w.KeyTaken)
        {
            w.TimeLeft -= dt;
            if (w.TimeLeft <= 0) { w.TimeLeft = 0; Kill(w, "out of time"); }
        }
        MovePlatforms(w, dt);
        MoveKnight(w, c, dt);
        Camera(w);
        Arrivals(w, dt);
        // (A copy of the list: Malgrim calls up bats as he goes.)
        foreach (var e in w.Enemies.ToList()) Act(w, e, dt);
        MoveShots(w, dt);
        MoveMissiles(w, dt);
        MoveItems(w, dt);
        Hits(w);
        w.Enemies.RemoveAll(e => e.Dead);
        w.Shots.RemoveAll(s => s.Dead);
        w.Missiles.RemoveAll(m => m.Dead);
        w.Items.RemoveAll(i => i.Dead);
    }

    // ───────────────────────────── ground under foot ─────────────────────────────

    private static bool Blocks(Tile t) => t is Tile.Solid or Tile.Grave;

    /// <summary>A surface for feet (solid, a gravestone's top, a ledge, or a gliding ledge) between two heights at x.</summary>
    private static double? Landing(World w, double x, double from, double to, out Platform? on)
    {
        on = null;
        var s = w.Stage;
        int r0 = (int)Math.Floor((from - Field.Top) / Field.Tile), r1 = (int)Math.Floor((to - Field.Top) / Field.Tile);
        double? best = null;
        foreach (double fx in new[] { x - Knight.HalfWidth + 3, x + Knight.HalfWidth - 3 })
        {
            int col = (int)Math.Floor(fx / Field.Tile);
            for (int r = Math.Max(0, r0); r <= r1 + 1; r++)
            {
                double top = Field.Top + r * Field.Tile;
                if (top < from - 0.01 || top > to + 0.01) continue;
                var t = s.At(col, r);
                // (A ledge only from above, and not the very top of a ladder going down.)
                // (The top of a ladder is floor too.)
                if (Blocks(t) || t == Tile.Ledge || t == Tile.Ladder && s.At(col, r - 1) != Tile.Ladder) { if (best == null || top < best) best = top; break; }
            }
        }
        foreach (var p in w.Platforms)
            if (x > p.X - 4 && x < p.X + p.Width + 4 && p.Y >= from - 0.01 && p.Y <= to + 4 && (best == null || p.Y < best)) { best = p.Y; on = p; }
        return best;
    }

    private static bool Standing(World w, Knight k, out Platform? on) => Landing(w, k.X, k.Y - 1, k.Y + 2, out on) is { } y && Math.Abs(y - k.Y) < 3;

    private static bool LadderAt(World w, double x, double y) => w.Stage.AtPoint(x, y) == Tile.Ladder;

    // ───────────────────────────── Sir Hollin ─────────────────────────────

    private static void MoveKnight(World w, Controls c, double dt)
    {
        var k = w.Knight;
        bool jump = c.Jump && !k.JumpPrev, fire = c.Fire && !k.FirePrev;
        k.JumpPrev = c.Jump;
        k.FirePrev = c.Fire;
        if (k.Dead)
        {
            k.DeadTime += dt;
            return;
        }
        k.Hurt = Math.Max(0, k.Hurt - dt);
        k.ThrowCool -= dt;
        bool knocked = k.Hurt > HurtSeconds - 0.4;

        if (k.Climbing)
        {
            k.Crouch = false;
            k.Vx = 0;
            k.Vy = 0;
            if (c.Dy != 0 && !knocked)
            {
                k.Walk += dt;
                double ny = k.Y + c.Dy * Knight.ClimbSpeed * dt;
                // Off the top onto the ledge, or off the bottom onto the ground.
                if (!LadderAt(w, k.X, ny - 4) && c.Dy < 0) { k.Y = Field.Top + Math.Floor((ny - 4 - Field.Top) / Field.Tile + 1) * Field.Tile; k.Climbing = false; k.Ground = true; }
                else if (c.Dy > 0 && Landing(w, k.X, k.Y, ny, out _) is { } floor && !LadderAt(w, k.X, floor + 2)) { k.Y = floor; k.Climbing = false; k.Ground = true; }
                else k.Y = ny;
            }
            if (jump && c.Dx != 0) { k.Climbing = false; k.Ground = false; k.Vy = -Knight.JumpSpeed * 0.5; k.Vx = c.Dx * Knight.AirSpeed * 0.6; }
            return;
        }

        if (k.Ground && !knocked)
        {
            // Onto a ladder: up one from beside it, or down one from its top.
            double mid = (Math.Floor(k.X / Field.Tile) + 0.5) * Field.Tile;
            if (c.Dy < 0 && LadderAt(w, k.X, k.Y - 8) && Math.Abs(k.X - mid) < 10) { k.Climbing = true; k.X = mid; k.Crouch = false; return; }
            if (c.Dy > 0 && LadderAt(w, k.X, k.Y + 8) && Math.Abs(k.X - mid) < 10) { k.Climbing = true; k.X = mid; k.Y += 6; k.Crouch = false; return; }
            k.Crouch = c.Dy > 0;
            if (c.Dx != 0) k.Facing = c.Dx;
            k.Vx = k.Crouch ? 0 : c.Dx * Knight.WalkSpeed;
            if (c.Dx != 0 && !k.Crouch) k.Walk += dt;
            if (jump)
            {
                // A jump is a jump: its way across is fixed when it starts.
                k.Ground = false;
                k.Crouch = false;
                k.Vy = -Knight.JumpSpeed;
                k.Vx = c.Dx * Knight.AirSpeed;
                w.Heard.Add(Sfx.Jump);
            }
        }
        else k.Crouch = false;
        if (fire && k.ThrowCool <= 0 && !knocked) Throw(w, k);

        // Across, stopped by walls and gravestones and the left edge of the screen.
        double nx = k.X + k.Vx * dt;
        double side = nx + Math.Sign(k.Vx) * Knight.HalfWidth;
        if (k.Vx != 0)
        {
            for (double y = k.Top + 2; y < k.Y - 1; y += 10)
                if (Blocks(w.Stage.AtPoint(side, y)) || Blocks(w.Stage.AtPoint(side, k.Y - 2)))
                {
                    nx = k.X;
                    if (!k.Ground) k.Vx = 0;
                    break;
                }
        }
        k.X = Math.Clamp(nx, w.CamX + Knight.HalfWidth, w.Stage.Cols * Field.Tile - Knight.HalfWidth);

        // Up and down.
        if (k.Ground)
        {
            if (Standing(w, k, out var on))
            {
                if (on != null) { k.X += on.Dx; k.Y = on.Y; }
            }
            else
            {
                // Walked off an edge: straight down.
                k.Ground = false;
                k.Vy = 0;
                k.Vx = 0;
            }
        }
        if (!k.Ground)
        {
            k.Vy = Math.Min(MaxFall, k.Vy + Gravity * dt);
            double ny = k.Y + k.Vy * dt;
            if (k.Vy > 0 && Landing(w, k.X, k.Y, ny, out var on) is { } floor)
            {
                k.Y = floor;
                k.Vy = 0;
                k.Ground = true;
                if (on != null) k.X += on.Dx;
            }
            else
            {
                // A ceiling stops a jump.
                if (k.Vy < 0 && Blocks(w.Stage.AtPoint(k.X, ny - Knight.Height))) { ny = k.Y; k.Vy = 0; }
                k.Y = ny;
            }
        }
        if (w.Stage.AtPoint(k.X, k.Y - 4) == Tile.Hazard || k.Y > Field.Top + Field.Rows * Field.Tile) Kill(w, "fell");
    }

    private static int MaxInAir(Weapon w) => w switch { Weapon.Dagger => 3, _ => 2 };

    private static void Throw(World w, Knight k)
    {
        if (w.Shots.Count(s => s.Burn <= 0) >= MaxInAir(k.Weapon)) return;
        double y = k.Crouch ? k.Y - 14 : k.Y - 30;
        var s = new Shot { X = k.X + k.Facing * 14, Y = y, Kind = k.Weapon, Life = 1.4 };
        switch (k.Weapon)
        {
            case Weapon.Lance: s.Vx = k.Facing * 520; break;
            case Weapon.Dagger: s.Vx = k.Facing * 720; break;
            case Weapon.Torch: s.Vx = k.Facing * 300; s.Vy = -260; break;
            case Weapon.Axe: s.Vx = k.Facing * 260; s.Vy = -480; break;
        }
        w.Shots.Add(s);
        k.ThrowCool = k.Weapon == Weapon.Dagger ? 0.16 : 0.24;
        w.Heard.Add(Sfx.Throw);
    }

    /// <summary>Hit: the armour comes off (and he's knocked back), or, with none left, that's that.</summary>
    public static void Hurt(World w, string cause)
    {
        var k = w.Knight;
        if (k.Dead || k.Hurt > 0) return;
        if (!k.Armour) { Kill(w, cause); return; }
        k.Armour = false;
        k.Hurt = HurtSeconds;
        k.Climbing = false;
        k.Ground = false;
        k.Vy = -360;
        k.Vx = -k.Facing * 120;
        w.Heard.Add(Sfx.ArmourOff);
        Burst(w, k.X, k.Y - 30, 0xFFC0C8D8, 10);
    }

    public static void Kill(World w, string cause)
    {
        var k = w.Knight;
        if (k.Dead) return;
        k.Dead = true;
        k.Armour = false;
        k.DeadTime = 0;
        w.Cause = cause;
        w.Heard.Add(Sfx.Die);
    }

    private static void MovePlatforms(World w, double dt)
    {
        foreach (var p in w.Platforms)
        {
            var g = p.Path;
            p.T += dt;
            double len = Math.Max(1, Math.Sqrt((g.X1 - g.X0) * (g.X1 - g.X0) + (g.Y1 - g.Y0) * (g.Y1 - g.Y0)));
            double f = (1 - Math.Cos(p.T * g.Speed / len * Math.PI)) / 2;
            double nx = g.X0 + (g.X1 - g.X0) * f, ny = g.Y0 + (g.Y1 - g.Y0) * f;
            p.Dx = nx - p.X;
            p.Dy = ny - p.Y;
            p.X = nx;
            p.Y = ny;
        }
    }

    /// <summary>The camera follows him forwards, never back, and stops for the fight at the end.</summary>
    private static void Camera(World w)
    {
        var s = w.Stage;
        w.CamX = Math.Clamp(Math.Max(w.CamX, w.Knight.X - 380), 0, s.ArenaX);
        if (!w.BossUp && w.Knight.X > s.ArenaX + 260)
        {
            w.BossUp = true;
            SpawnBoss(w);
        }
    }

    // ───────────────────────────── creatures arriving ─────────────────────────────

    private static void Arrivals(World w, double dt)
    {
        var s = w.Stage;
        var k = w.Knight;
        for (int i = 0; i < s.Spawns.Count; i++)
        {
            if (w.Spawned[i] || s.Spawns[i].X > w.CamX + Field.Width + 64) continue;
            w.Spawned[i] = true;
            w.Enemies.Add(Make(w, s.Spawns[i].Kind, s.Spawns[i].X, s.Spawns[i].Y));
        }
        if (k.Dead || w.BossUp) return;
        for (int i = 0; i < s.Swarms.Count; i++)
        {
            var (x0, x1, kind, every) = s.Swarms[i];
            if (k.X < x0 || k.X > x1 || (w.SwarmIn[i] -= dt) > 0) continue;
            w.SwarmIn[i] = every * w.Dice.Between(0.7, 1.3) / Pace(w);
            if (w.Enemies.Count(e => e.Kind == kind) >= 4) continue;
            switch (kind)
            {
                case Foe.Zombie:
                {
                    // Up out of the ground, ahead mostly, never on top of him.
                    double x = k.X + (w.Dice.Next() < 0.7 ? 1 : -1) * w.Dice.Between(150, 380);
                    x = Math.Clamp(x, w.CamX + 40, w.CamX + Field.Width - 40);
                    double y = s.FloorAt((int)(x / Field.Tile), Field.Top + 4 * Field.Tile);
                    if (y > Field.Top + Field.Rows * Field.Tile || s.AtPoint(x, y + 4) != Tile.Solid) break;
                    var z = Make(w, Foe.Zombie, x, y);
                    w.Enemies.Add(z);
                    w.Heard.Add(Sfx.Rise);
                    break;
                }
                case Foe.Ghost:
                    w.Enemies.Add(Make(w, Foe.Ghost, w.CamX + Field.Width + 30, Field.Top + w.Dice.Between(140, 460)));
                    break;
                case Foe.Bat:
                    w.Enemies.Add(Make(w, Foe.Bat, Math.Min(w.CamX + Field.Width - 30, k.X + w.Dice.Between(200, 420)), Field.Top + 4 * Field.Tile + 30));
                    break;
            }
        }
    }

    private static Enemy Make(World w, Foe kind, double x, double y)
    {
        double pace = Pace(w);
        var e = new Enemy { Kind = kind, X = x, Y = y, HomeX = x, HomeY = y, FireIn = w.Dice.Between(1.5, 3) };
        switch (kind)
        {
            case Foe.Zombie: e.Hp = 1; e.Width = 26; e.Height = 44; break;
            case Foe.Crow: e.Hp = 1; e.Width = 24; e.Height = 20; break;
            case Foe.Plant: e.Hp = 2; e.Width = 28; e.Height = 40; break;
            case Foe.Ghost: e.Hp = 1; e.Width = 30; e.Height = 30; break;
            case Foe.Bat: e.Hp = 1; e.Width = 26; e.Height = 18; break;
            case Foe.Ogre: e.Hp = (int)(6 * pace); e.Width = 40; e.Height = 62; break;
            case Foe.Gargoyle: e.Hp = (int)(3 * pace); e.Width = 34; e.Height = 36; e.Y = y - 160; break;
        }
        return e;
    }

    private static void SpawnBoss(World w)
    {
        var s = w.Stage;
        double pace = Pace(w);
        double x = s.ArenaX + Field.Width - 160, floor = s.FloorAt((int)(x / Field.Tile), Field.Top + 4 * Field.Tile);
        // The lesser creatures flee before it: the end of a stage is a duel.
        foreach (var o in w.Enemies)
        {
            o.Dead = true;
            Burst(w, o.X, o.Y - o.Height / 2, 0xFF8A7AA0, 6);
        }
        var b = new Enemy { Kind = s.Boss, X = x, Y = floor, HomeX = x, HomeY = floor, Boss = true, FireIn = 2, Facing = -1 };
        switch (s.Boss)
        {
            case Foe.Cyclops: b.Hp = (int)(14 * pace); b.Width = 64; b.Height = 112; break;
            case Foe.GargoyleLord: b.Hp = (int)(18 * pace); b.Width = 60; b.Height = 64; b.Y = floor - 220; break;
            case Foe.Serpent:
                b.Hp = (int)(16 * pace); b.Width = 40; b.Height = 36; b.Y = Field.Top + 220;
                b.Trail = Enumerable.Repeat((b.X, b.Y), 120).ToArray();
                break;
            case Foe.Malgrim: b.Hp = (int)(24 * pace); b.Width = 90; b.Height = 120; b.Y = Field.Top + 300; b.X = s.ArenaX + Field.Width - 130; break;
        }
        w.Enemies.Add(b);
        w.Heard.Add(Sfx.BossCome);
    }

    // ───────────────────────────── what they do ─────────────────────────────

    private static void Act(World w, Enemy e, double dt)
    {
        var k = w.Knight;
        double pace = Pace(w);
        e.T += dt;
        e.Hurt = Math.Max(0, e.Hurt - dt);
        int toward = Math.Sign(k.X - e.X) is 0 ? e.Facing : Math.Sign(k.X - e.X);
        switch (e.Kind)
        {
            case Foe.Zombie:
                // Up out of the ground, a shamble towards him, and back down after a while.
                if (e.T < 0.8) { e.State = 0; break; }
                if (e.T > 9) { e.State = 2; if (e.T > 9.8) e.Dead = true; break; }
                e.State = 1;
                e.Facing = e.T < 1 ? toward : e.Facing;
                Walker(w, e, 52 * pace, dt);
                break;
            case Foe.Crow:
                if (e.State == 0 && Math.Abs(k.X - e.X) < 240) { e.State = 1; e.Facing = toward; e.T = 0; w.Heard.Add(Sfx.Caw); }
                if (e.State == 1)
                {
                    e.X += e.Facing * 150 * pace * dt;
                    e.Y = e.HomeY - 40 + Math.Sin(e.T * 5) * 28 + Math.Min(60, e.T * 40);
                    if (Math.Abs(e.X - w.CamX - Field.Width / 2) > Field.Width) e.Dead = true;
                }
                break;
            case Foe.Plant:
                if ((e.FireIn -= dt) <= 0 && Math.Abs(k.X - e.X) < 520 && !k.Dead)
                {
                    e.FireIn = 2.6 / pace;
                    double tx = k.X - e.X, t = 1.0;
                    w.Missiles.Add(new Missile { X = e.X, Y = e.Y - 34, Vx = tx / t, Vy = -420, Gravity = 760, Life = 3 });
                    w.Heard.Add(Sfx.Spit);
                }
                e.Facing = toward;
                break;
            case Foe.Ghost:
                e.X -= 120 * pace * dt;
                e.Y = e.HomeY + Math.Sin(e.T * 2.4) * 60;
                if (e.X < w.CamX - 60) e.Dead = true;
                break;
            case Foe.Bat:
            {
                // A swoop down at him from the roof.
                double tx = k.X, ty = k.Y - 30;
                e.Vx += Math.Clamp(tx - e.X, -1, 1) * 260 * dt;
                e.Vy += Math.Clamp(ty - e.Y, -1, 1) * 260 * dt;
                e.Vx = Math.Clamp(e.Vx, -170 * pace, 170 * pace);
                e.Vy = Math.Clamp(e.Vy, -120 * pace, 120 * pace);
                e.X += e.Vx * dt;
                e.Y += e.Vy * dt + Math.Sin(e.T * 12) * 1.2;
                if (e.T > 10) e.Dead = true;
                break;
            }
            case Foe.Ogre:
                e.Facing = toward;
                if (e.State == 0 && e.T > 2.5 && Math.Abs(k.X - e.X) < 260) { e.State = 1; e.Vy = -520; e.T = 0; }
                if (e.State == 1)
                {
                    e.Vy += Gravity * dt;
                    e.X += e.Facing * 120 * pace * dt;
                    e.Y += e.Vy * dt;
                    double floor = w.Stage.FloorAt((int)(e.X / Field.Tile), e.Y - 30);
                    if (e.Vy > 0 && e.Y >= floor) { e.Y = floor; e.State = 0; e.Vy = 0; e.T = 0; }
                }
                else Walker(w, e, 44 * pace, dt);
                break;
            case Foe.Gargoyle or Foe.GargoyleLord:
                Gargoyle(w, e, dt, pace);
                break;
            case Foe.Cyclops:
                Cyclops(w, e, dt, pace);
                break;
            case Foe.Serpent:
                Serpent(w, e, dt, pace);
                break;
            case Foe.Malgrim:
                Malgrim(w, e, dt, pace);
                break;
        }
    }

    /// <summary>Along the ground towards its facing, turning back at a wall, a gravestone or an edge.</summary>
    private static void Walker(World w, Enemy e, double speed, double dt)
    {
        var s = w.Stage;
        double nx = e.X + e.Facing * speed * dt, ahead = nx + e.Facing * e.Width / 2;
        bool wall = Blocks(s.AtPoint(ahead, e.Y - 8)), edge = s.AtPoint(ahead, e.Y + 4) is not (Tile.Solid or Tile.Grave or Tile.Ledge);
        if (wall || edge) { e.Facing = -e.Facing; return; }
        e.X = nx;
    }

    /// <summary>
    /// The red gargoyle: it hangs in the air near him, slips out of the way of anything thrown at it, and every
    /// so often swoops; it spits fire too. The lord of them is bigger, and spits three at once.
    /// </summary>
    private static void Gargoyle(World w, Enemy e, double dt, double pace)
    {
        var k = w.Knight;
        bool lord = e.Kind == Foe.GargoyleLord;
        double side = e.X > k.X ? 1 : -1;
        // Dodging: a shot coming at it, it rises (or drops) out of the way; but it needs a moment before it can again.
        e.Dodge -= dt;
        if (e.Dodge <= 0 && w.Shots.Any(s => s.Burn <= 0 && Math.Abs(s.Y - e.Y + e.Height / 2) < 50 && (e.X - s.X) * Math.Sign(s.Vx) is > 0 and < 140))
            e.Dodge = lord ? 1.4 : 1.6;
        var threat = e.Dodge > (lord ? 1.0 : 1.2) ? w.Shots.FirstOrDefault(s => s.Burn <= 0) : null;
        if (e.State == 0)
        {
            double hx = k.X + side * (lord ? 230 : 170), hy = k.Y - (lord ? 200 : 140) + Math.Sin(e.T * 2) * 30;
            if (threat != null) hy = e.Y + (e.Y > Field.Top + 200 ? -1 : 1) * 200;
            e.Vx += Math.Clamp(hx - e.X, -1, 1) * 900 * dt;
            e.Vy += Math.Clamp(hy - e.Y, -1, 1) * 900 * dt;
            double top = (threat != null ? 260 : 190) * pace;
            e.Vx = Math.Clamp(e.Vx * 0.96, -top, top);
            e.Vy = Math.Clamp(e.Vy * 0.96, -top, top);
            if (e.T > (lord ? 3.2 : 3.8) / pace && threat == null && !k.Dead) { e.State = 1; e.T = 0; w.Heard.Add(Sfx.Swoop); e.HomeX = k.X; e.HomeY = k.Y - 20; }
            if ((e.FireIn -= dt) <= 0 && !k.Dead)
            {
                e.FireIn = (lord ? 1.6 : 2.6) / pace;
                double a = Math.Atan2(k.X - e.X, -(k.Y - 24 - (e.Y - e.Height / 2)));
                for (int i = lord ? -1 : 0; i <= (lord ? 1 : 0); i++)
                    w.Missiles.Add(new Missile { X = e.X, Y = e.Y - e.Height / 2, Vx = Math.Sin(a + i * 0.3) * 240, Vy = -Math.Cos(a + i * 0.3) * 240, Life = 3 });
                w.Heard.Add(Sfx.Spit);
            }
        }
        else
        {
            // The swoop: down at where he was, and up again.
            double tx = e.HomeX, ty = e.HomeY;
            double dx = tx - e.X, dy = ty - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double sp = 330 * pace;
            e.Vx = dx / d * sp;
            e.Vy = dy / d * sp;
            if (d < 20 || e.T > 1.4) { e.State = 0; e.T = 0; e.Vy = -sp; }
        }
        e.Facing = k.X > e.X ? 1 : -1;
        e.X += e.Vx * dt;
        e.Y = Math.Clamp(e.Y + e.Vy * dt, Field.Top + 60, Field.Top + (Field.Rows - 3) * Field.Tile);
        e.X = Math.Clamp(e.X, w.CamX + 20, w.CamX + Field.Width - 20);
    }

    /// <summary>The cyclops: it paces its end of the ground, leaps at him, and hurls fire.</summary>
    private static void Cyclops(World w, Enemy e, double dt, double pace)
    {
        var k = w.Knight;
        var s = w.Stage;
        e.Facing = k.X > e.X ? 1 : -1;
        if (e.State == 0)
        {
            Walker(w, e, 60 * pace, dt);
            if (e.T > 2.2 / pace && !k.Dead) { e.State = 1; e.Vy = -640; e.Vx = Math.Clamp(k.X - e.X, -360, 360); e.T = 0; w.Heard.Add(Sfx.Swoop); }
            if ((e.FireIn -= dt) <= 0 && !k.Dead)
            {
                e.FireIn = 1.9 / pace;
                w.Missiles.Add(new Missile { X = e.X, Y = e.Y - 90, Vx = (k.X - e.X) / 1.1, Vy = -380, Gravity = 600, Life = 3 });
                w.Heard.Add(Sfx.Spit);
            }
        }
        else
        {
            e.Vy += Gravity * dt;
            e.X = Math.Clamp(e.X + e.Vx * dt, s.ArenaX + 60, s.ArenaX + Field.Width - 60);
            e.Y += e.Vy * dt;
            double floor = s.FloorAt((int)(e.X / Field.Tile), Field.Top + 4 * Field.Tile);
            if (e.Vy > 0 && e.Y >= floor)
            {
                e.Y = floor;
                e.State = 0;
                e.T = 0;
                w.Heard.Add(Sfx.Thud);
            }
        }
    }

    /// <summary>The serpent: its head loops about the cavern, its body coiling after; only the head can be hurt.</summary>
    private static void Serpent(World w, Enemy e, double dt, double pace)
    {
        var s = w.Stage;
        // (Low enough on its loops for a thrown weapon to reach.)
        double cx = s.ArenaX + Field.Width * 0.6, cy = Field.Top + 420;
        e.X = cx + Math.Sin(e.T * 0.9 * pace) * 330;
        e.Y = cy + Math.Sin(e.T * 1.7 * pace) * 170;
        Array.Copy(e.Trail, 0, e.Trail, 1, e.Trail.Length - 1);
        e.Trail[0] = (e.X, e.Y);
        e.Facing = Math.Cos(e.T * 0.9 * pace) > 0 ? 1 : -1;
        if ((e.FireIn -= dt) <= 0 && !w.Knight.Dead)
        {
            e.FireIn = 2.2 / pace;
            var k = w.Knight;
            double a = Math.Atan2(k.X - e.X, -(k.Y - 24 - e.Y));
            w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = Math.Sin(a) * 230, Vy = -Math.Cos(a) * 230, Life = 3 });
            w.Heard.Add(Sfx.Spit);
        }
    }

    /// <summary>Malgrim: he hangs at the far end of his hall, rising and falling, flinging fans of fire, and calling bats.</summary>
    private static void Malgrim(World w, Enemy e, double dt, double pace)
    {
        var k = w.Knight;
        e.Y = Field.Top + 500 + Math.Sin(e.T * 0.8) * 90;
        e.X = w.Stage.ArenaX + Field.Width - 140 + Math.Sin(e.T * 0.5) * 60;
        e.Facing = -1;
        if ((e.FireIn -= dt) <= 0 && !k.Dead)
        {
            e.FireIn = 2.1 / pace;
            double a = Math.Atan2(k.X - e.X, -(k.Y - 24 - (e.Y - 60)));
            for (int i = -2; i <= 2; i++)
                w.Missiles.Add(new Missile { X = e.X - 30, Y = e.Y - 70, Vx = Math.Sin(a + i * 0.22) * 220, Vy = -Math.Cos(a + i * 0.22) * 220, Life = 4 });
            w.Heard.Add(Sfx.Spit);
        }
        if (e.State++ % 400 == 399 && w.Enemies.Count(b => b.Kind == Foe.Bat) < 3)
            w.Enemies.Add(Make(w, Foe.Bat, e.X - 60, e.Y - 100));
    }

    // ───────────────────────────── things in flight ─────────────────────────────

    private static void MoveShots(World w, double dt)
    {
        var s = w.Stage;
        foreach (var shot in w.Shots)
        {
            if (shot.Burn > 0)
            {
                // A torch's fire, burning where it fell.
                if ((shot.Burn -= dt) <= 0) shot.Dead = true;
                continue;
            }
            if (shot.Kind is Weapon.Torch or Weapon.Axe) shot.Vy += (shot.Kind == Weapon.Torch ? 900 : 1100) * dt;
            shot.X += shot.Vx * dt;
            shot.Y += shot.Vy * dt;
            shot.Life -= dt;
            var t = s.AtPoint(shot.X, shot.Y);
            if (t == Tile.Grave || t == Tile.Solid || t == Tile.Hazard || (t == Tile.Ledge && shot.Vy > 0 && shot.Kind == Weapon.Torch))
            {
                if (shot.Kind == Weapon.Torch && t != Tile.Hazard)
                {
                    // It lands, and burns.
                    shot.Burn = 0.9;
                    shot.Y = Field.Top + Math.Floor((shot.Y - Field.Top) / Field.Tile) * Field.Tile;
                    shot.Vx = shot.Vy = 0;
                    w.Heard.Add(Sfx.Flame);
                }
                else
                {
                    shot.Dead = true;
                    if (t == Tile.Grave) Spark(w, shot.X, shot.Y);
                }
            }
            if (shot.Life <= 0 || shot.X < w.CamX - 40 || shot.X > w.CamX + Field.Width + 40 || shot.Y > Field.Top + Field.Rows * Field.Tile) shot.Dead = true;
        }
    }

    private static void MoveMissiles(World w, double dt)
    {
        foreach (var m in w.Missiles)
        {
            m.Vy += m.Gravity * dt;
            m.X += m.Vx * dt;
            m.Y += m.Vy * dt;
            if ((m.Life -= dt) <= 0 || Blocks(w.Stage.AtPoint(m.X, m.Y))) m.Dead = true;
        }
    }

    private static void MoveItems(World w, double dt)
    {
        foreach (var i in w.Items)
        {
            if (i.Pot) continue;
            i.Vy = Math.Min(400, i.Vy + 900 * dt);
            double floor = w.Stage.FloorAt((int)(i.X / Field.Tile), i.Y - 10);
            i.Y = Math.Min(floor, i.Y + i.Vy * dt);
            if ((i.Life -= dt) <= 0 && !i.Key) i.Dead = true;
        }
    }

    // ───────────────────────────── hits ─────────────────────────────

    private static int Damage(Weapon w) => w switch { Weapon.Torch => 2, Weapon.Axe => 2, _ => 1 };

    public static int Points(Foe f) => f switch
    {
        Foe.Zombie or Foe.Crow or Foe.Ghost or Foe.Bat => 100,
        Foe.Plant => 200,
        Foe.Ogre => 500,
        Foe.Gargoyle => 1000,
        _ => 5000,
    };

    private static bool Touches(Enemy e, double x, double y, double r) =>
        x + r > e.X - e.Width / 2 && x - r < e.X + e.Width / 2 && y + r > e.Y - e.Height && y - r < e.Y;

    private static void Hits(World w)
    {
        var k = w.Knight;
        foreach (var shot in w.Shots.Where(s => !s.Dead))
        {
            double r = shot.Burn > 0 ? 14 : 8;
            foreach (var e in w.Enemies)
            {
                if (e.Dead || (e.Kind == Foe.Zombie && e.State == 0 && e.T < 0.3)) continue;
                if (!Touches(e, shot.X, shot.Y - (shot.Burn > 0 ? 10 : 0), r)) continue;
                if (shot.Burn > 0 && e.Hurt > 0) continue;
                e.Hp -= Damage(shot.Kind);
                e.Hurt = 0.2;
                w.Heard.Add(e.Boss ? Sfx.BossHit : Sfx.Hit);
                if (shot.Burn <= 0 && (shot.Kind != Weapon.Axe || ++shot.Pierced >= 3)) shot.Dead = true;
                if (e.Hp <= 0) Defeat(w, e);
                if (shot.Dead) break;
            }
            if (shot.Dead) continue;
            foreach (var pot in w.Items.Where(i => i.Pot && !i.Dead))
                if (Math.Abs(shot.X - pot.X) < 16 && shot.Y > pot.Y - 38 && shot.Y < pot.Y)
                {
                    pot.Pot = false;
                    pot.Vy = -300;
                    pot.Life = 12;
                    if (shot.Burn <= 0) shot.Dead = true;
                    w.Heard.Add(Sfx.Pot);
                    Burst(w, pot.X, pot.Y - 14, 0xFFC08A50, 8);
                    break;
                }
        }
        if (k.Dead) return;
        // What he picks up.
        foreach (var i in w.Items.Where(i => !i.Pot && !i.Dead && Math.Abs(i.X - k.X) < 24 && i.Y > k.Top - 10 && i.Y - 20 < k.Y))
        {
            i.Dead = true;
            if (i.Key) { w.KeyTaken = true; w.KeyAt = w.Clock; w.Heard.Add(Sfx.Key); continue; }
            switch (i.Loot)
            {
                case Loot.Weapon: k.Weapon = i.Weapon; w.Heard.Add(Sfx.Pickup); Word(w, k.X, k.Y - 60, i.Weapon.ToString().ToUpperInvariant(), 0xFFFFE14A); break;
                case Loot.Armour:
                    if (k.Armour) Award(w, ArmourPoints, k.X, k.Y - 60);
                    k.Armour = true;
                    w.Heard.Add(Sfx.Armour);
                    Word(w, k.X, k.Y - 60, "ARMOUR", 0xFFC0D8FF);
                    break;
                case Loot.Money: Award(w, MoneyPoints, k.X, k.Y - 60); w.Heard.Add(Sfx.Pickup); break;
            }
        }
        if (k.Hurt > 0) return;
        double top = k.Top, bottom = k.Y;
        foreach (var e in w.Enemies)
        {
            if (e.Dead || (e.Kind == Foe.Zombie && e.State != 1) || (e.Kind == Foe.Crow && e.State == 0) || (e.Kind == Foe.Plant)) continue;
            if (k.X + Knight.HalfWidth - 3 > e.X - e.Width / 2 + 4 && k.X - Knight.HalfWidth + 3 < e.X + e.Width / 2 - 4 && bottom > e.Y - e.Height + 4 && top < e.Y - 4)
            { Hurt(w, e.Boss ? e.Kind.ToString() : e.Kind.ToString().ToLowerInvariant()); return; }
            if (e.Kind == Foe.Serpent)
                for (int i = 8; i < e.Trail.Length; i += 8)
                {
                    var (bx, by) = e.Trail[i];
                    if (Math.Abs(bx - k.X) < 18 + Knight.HalfWidth && by > top - 14 && by < bottom + 14) { Hurt(w, "serpent"); return; }
                }
        }
        foreach (var m in w.Missiles)
            if (!m.Dead && Math.Abs(m.X - k.X) < Knight.HalfWidth + 5 && m.Y > top - 5 && m.Y < bottom + 2) { m.Dead = true; Hurt(w, "fire"); return; }
    }

    private static void Defeat(World w, Enemy e)
    {
        e.Dead = true;
        int points = Points(e.Kind) * (e.Boss ? w.StageIndex + 1 : 1);
        Award(w, points, e.X, e.Y - e.Height);
        Burst(w, e.X, e.Y - e.Height / 2, e.Boss ? 0xFFFFC040 : 0xFFFF8A4A, e.Boss ? 30 : 10);
        w.Heard.Add(e.Boss ? Sfx.BossDie : Sfx.Kill);
        if (e.Boss)
        {
            w.BossDown = true;
            w.Missiles.Clear();
            foreach (var o in w.Enemies) o.Dead = true;
            // The key to the next stage, falling where it fell.
            w.Items.Add(new Item { X = Math.Clamp(e.X, w.CamX + 80, w.CamX + Field.Width - 80), Y = Math.Min(e.Y - 40, Field.Top + 200), Key = true, Life = double.MaxValue });
        }
    }

    private static void Award(World w, int points, double x, double y)
    {
        w.Score += points;
        w.Effects.Add(new Effect { Kind = EffectKind.Score, X = x, Y = y, Text = points.ToString(), Colour = 0xFFFFE14A });
    }

    private static void Word(World w, double x, double y, string text, uint colour) =>
        w.Effects.Add(new Effect { Kind = EffectKind.Word, X = x, Y = y, Text = text, Colour = colour, Life = 1.2 });

    private static void Burst(World w, double x, double y, uint colour, int size) =>
        w.Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Colour = colour, Life = 0.4 + size / 60.0, Text = size.ToString() });

    private static void Spark(World w, double x, double y) =>
        w.Effects.Add(new Effect { Kind = EffectKind.Spark, X = x, Y = y, Colour = 0xFFFFF0C0, Life = 0.2 });
}
