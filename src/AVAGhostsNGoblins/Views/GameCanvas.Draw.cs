using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAGhostsNGoblins.Engine;
using Effect = AVAGhostsNGoblins.Engine.Effect;

namespace AVAGhostsNGoblins.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAGhostsNGoblins/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Stone = 0xFF3A3448, Moss = 0xFF6AA05A;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;

    private static readonly (uint SkyTop, uint SkyLow, uint Earth, uint Edge, uint Hazard)[] Themes =
    [
        (0xFF0A1030, 0xFF2A2050, 0xFF3A2A1E, 0xFF3A7A2A, 0xFF2A5AA0),   // the graveyard: night, earth, grass, water
        (0xFF20102A, 0xFF5A2A4A, 0xFF4A3A30, 0xFF6A5A4A, 0xFF2A4A8A),   // the village: dusk, cobbles
        (0xFF06101A, 0xFF10283A, 0xFF4A6A8A, 0xFFC0E0F0, 0xFF3A8AC0),   // the caverns: ice
        (0xFF200808, 0xFF5A1A10, 0xFF4A3A3A, 0xFF8A5A4A, 0xFFE05A10),   // the tower: fire
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame) DrawStage(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the stage ─────────────────────────────

    private void DrawStage(DrawingContext dc)
    {
        var w = Engine.World;
        var th = Themes[w.Stage.Theme];
        DrawBackdrop(dc, w, th);
        using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Height - Field.Top)))
        using (dc.PushTransform(Matrix.CreateTranslation(-Math.Round(w.CamX), 0)))
        {
            int c0 = (int)(w.CamX / T) - 1, c1 = c0 + Field.ViewCols + 3;
            for (int c = Math.Max(0, c0); c < Math.Min(w.Stage.Cols, c1); c++)
            for (int r = 0; r < Field.Rows; r++)
                DrawTile(dc, w.Stage, c, r, th);
            foreach (var p in w.Platforms) DrawPlatform(dc, p, th);
            foreach (var i in w.Items) DrawItem(dc, i);
            foreach (var e in w.Enemies) DrawEnemy(dc, e);
            DrawKnight(dc, w.Knight);
            foreach (var s in w.Shots) DrawShot(dc, s);
            foreach (var m in w.Missiles) DrawMissile(dc, m);
            foreach (var e in w.Effects) DrawEffect(dc, e);
        }
    }

    /// <summary>The sky, and far-off scenery drifting slower than the stage.</summary>
    private void DrawBackdrop(DrawingContext dc, World w, (uint SkyTop, uint SkyLow, uint Earth, uint Edge, uint Hazard) th)
    {
        for (int i = 0; i < 8; i++)
        {
            double f = i / 7.0;
            uint a = th.SkyTop, b = th.SkyLow;
            uint Mix(int shift) => (uint)((((a >> shift) & 0xFF) * (1 - f) + ((b >> shift) & 0xFF) * f)) << shift;
            dc.FillRectangle(Brush(0xFF000000 | Mix(16) | Mix(8) | Mix(0)), new Rect(0, Field.Top + i * 88, Field.Width, 89));
        }
        double far = -w.CamX * 0.2, near = -w.CamX * 0.45;
        switch (w.Stage.Theme)
        {
            case 0:
                dc.FillEllipse(820, 170, 46, 0xFFE8E4C8);
                dc.FillEllipse(806, 160, 8, 0xFFC8C4A8);
                for (int i = -1; i < 9; i++)
                {
                    double x = ((far + i * 160) % 1280 + 1280) % 1280 - 128;
                    Poly(dc, 0xFF141030, null, 0, new Point(x, 600), new Point(x + 80, 470), new Point(x + 170, 600));
                }
                for (int i = -1; i < 8; i++)
                {
                    double x = ((near + i * 190) % 1330 + 1330) % 1330 - 160;
                    DrawDeadTree(dc, x, 640, 0xFF0A0818);
                }
                break;
            case 1:
                dc.FillEllipse(200, 160, 34, 0xFFF0C8A0);
                for (int i = -1; i < 10; i++)
                {
                    double x = ((far + i * 140) % 1400 + 1400) % 1400 - 140, h = 60 + Hash(i, 1, 2) % 80;
                    dc.FillRectangle(Brush(0xFF1A0C20), new Rect(x, 620 - h, 90, h + 20));
                    Poly(dc, 0xFF1A0C20, null, 0, new Point(x - 10, 620 - h), new Point(x + 45, 580 - h), new Point(x + 100, 620 - h));
                    if (Hash(i, 2, 3) % 2 == 0) dc.FillRectangle(Brush(0xFFE0A040), new Rect(x + 30, 640 - h, 12, 16));
                }
                break;
            case 2:
                for (int i = -1; i < 14; i++)
                {
                    double x = ((far + i * 90) % 1260 + 1260) % 1260 - 90, h = 40 + Hash(i, 4, 1) % 120;
                    Poly(dc, 0xFF0E2236, null, 0, new Point(x, Field.Top + 120), new Point(x + 30, Field.Top + 120 + h), new Point(x + 60, Field.Top + 120));
                    Poly(dc, 0xFF0E2236, null, 0, new Point(x + 20, 720), new Point(x + 50, 720 - h * 0.7), new Point(x + 80, 720));
                }
                break;
            case 3:
                for (int i = -1; i < 7; i++)
                {
                    double x = ((far + i * 220) % 1540 + 1540) % 1540 - 220;
                    dc.FillRectangle(Brush(0xFF2A0A08), new Rect(x, 260, 80, 460));
                    for (int b = 0; b < 4; b++) dc.FillRectangle(Brush(0xFF2A0A08), new Rect(x - 6 + b * 26, 240, 16, 22));
                    if ((int)(_clock * 2 + i) % 3 != 0) dc.FillRectangle(Brush(0xFFF0A030), new Rect(x + 32, 320, 14, 22));
                }
                break;
        }
    }

    private static void DrawDeadTree(DrawingContext dc, double x, double y, uint c)
    {
        dc.DrawLine(Pen(c, 10), new Point(x, y), new Point(x + 6, y - 150));
        dc.DrawLine(Pen(c, 6), new Point(x + 4, y - 90), new Point(x - 40, y - 140));
        dc.DrawLine(Pen(c, 5), new Point(x + 5, y - 120), new Point(x + 46, y - 170));
        dc.DrawLine(Pen(c, 4), new Point(x - 24, y - 118), new Point(x - 30, y - 160));
    }

    private void DrawTile(DrawingContext dc, StageDef s, int c, int r, (uint SkyTop, uint SkyLow, uint Earth, uint Edge, uint Hazard) th)
    {
        double x = c * T, y = Field.Top + r * T;
        switch (s.At(c, r))
        {
            case Tile.Solid:
            {
                dc.FillRectangle(Brush(th.Earth), new Rect(x, y, T, T));
                uint h = Hash(c, r, s.Theme);
                if (s.Theme is 1 or 3)
                {
                    // Brick.
                    double off = r % 2 == 0 ? 0 : T / 2;
                    dc.DrawLine(Pen(0x40000000, 2), new Point(x, y + T / 2), new Point(x + T, y + T / 2));
                    dc.DrawLine(Pen(0x40000000, 2), new Point(x + (off + T / 2) % T, y), new Point(x + (off + T / 2) % T, y + T / 2));
                    dc.DrawLine(Pen(0x40000000, 2), new Point(x + off % T, y + T / 2), new Point(x + off % T, y + T));
                }
                else
                    for (int i = 0; i < 4; i++)
                        dc.FillRectangle(Brush(Alpha(0xFF000000, 0.2)), new Rect(x + (h >> (i * 4)) % 28, y + (h >> (i * 4 + 2)) % 28, 4, 3));
                if (s.At(c, r - 1) is not Tile.Solid)
                {
                    dc.FillRectangle(Brush(th.Edge), new Rect(x, y, T, 6));
                    if (s.Theme == 0)
                        for (int i = 0; i < 4; i++) dc.DrawLine(Pen(th.Edge, 2), new Point(x + 4 + i * 8, y + 2), new Point(x + 2 + i * 8, y - 5));
                    if (s.Theme == 2)
                        for (int i = 0; i < 2; i++) Poly(dc, 0xFFC0E0F0, null, 0, new Point(x + 6 + i * 14, y + 6), new Point(x + 10 + i * 14, y + 16), new Point(x + 14 + i * 14, y + 6));
                }
                if (s.Theme == 2 && s.At(c, r + 1) is Tile.Empty or Tile.Ladder && r < 6)
                    Poly(dc, 0xFFA0D0F0, null, 0, new Point(x + 8, y + T), new Point(x + 14, y + T + 18 + Hash(c, r, 5) % 12), new Point(x + 20, y + T));
                break;
            }
            case Tile.Ledge:
                dc.FillRectangle(Brush(s.Theme == 2 ? 0xFF6A8AAA : 0xFF7A5A3A), new Rect(x, y, T, 10));
                dc.FillRectangle(Brush(0x40000000), new Rect(x, y + 7, T, 3));
                dc.DrawLine(Pen(0x50000000, 1), new Point(x + T - 1, y), new Point(x + T - 1, y + 10));
                break;
            case Tile.Ladder:
                dc.DrawLine(Pen(0xFF8A6A3A, 3), new Point(x + 7, y), new Point(x + 7, y + T));
                dc.DrawLine(Pen(0xFF8A6A3A, 3), new Point(x + T - 7, y), new Point(x + T - 7, y + T));
                for (int i = 0; i < 3; i++) dc.DrawLine(Pen(0xFFA07A4A, 3), new Point(x + 7, y + 6 + i * 11), new Point(x + T - 7, y + 6 + i * 11));
                if (s.At(c, r - 1) != Tile.Ladder) dc.FillRectangle(Brush(0xFF7A5A3A), new Rect(x, y, T, 4));
                break;
            case Tile.Grave:
            {
                uint stone = 0xFF8A8A9A;
                if (Hash(c, r, 9) % 2 == 0)
                {
                    dc.FillRectangle(Brush(stone), new Rect(x + 13, y - 4, 7, T + 4));
                    dc.FillRectangle(Brush(stone), new Rect(x + 5, y + 4, 22, 7));
                }
                else
                {
                    dc.FillRectangle(Brush(stone), new Rect(x + 4, y + 6, 24, T - 6), 2);
                    dc.DrawEllipse(Brush(stone), null, new Point(x + 16, y + 8), 12, 8);
                    dc.DrawLine(Pen(0xFF5A5A6A, 2), new Point(x + 10, y + 14), new Point(x + 22, y + 14));
                    dc.DrawLine(Pen(0xFF5A5A6A, 2), new Point(x + 10, y + 20), new Point(x + 20, y + 20));
                }
                break;
            }
            case Tile.Hazard:
            {
                dc.FillRectangle(Brush(th.Hazard), new Rect(x, y + 6, T, T - 6));
                double wave = Math.Sin(_clock * 3 + c) * 3;
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.5), 2), new Point(x, y + 8 + wave), new Point(x + T, y + 8 - wave));
                break;
            }
        }
    }

    private static void DrawPlatform(DrawingContext dc, Platform p, (uint SkyTop, uint SkyLow, uint Earth, uint Edge, uint Hazard) th)
    {
        dc.FillRectangle(Brush(th.Earth), new Rect(p.X, p.Y, p.Width, 14), 3);
        dc.FillRectangle(Brush(th.Edge), new Rect(p.X, p.Y, p.Width, 4), 2);
    }

    // ───────────────────────────── Sir Hollin ─────────────────────────────

    private void DrawKnight(DrawingContext dc, Knight k)
    {
        double x = k.X, y = k.Y;
        int f = k.Facing;
        if (k.Dead)
        {
            // A heap of bones, and the helmet.
            for (int i = 0; i < 5; i++) dc.DrawLine(Pen(0xFFE8E8E0, 3), new Point(x - 16 + i * 7, y - 2), new Point(x - 10 + i * 6, y - 8 - (i % 2) * 4));
            dc.FillEllipse(x + 4, y - 10, 7, 0xFFE8E8E0);
            return;
        }
        if (k.Hurt > 0 && (int)(_clock * 16) % 2 == 0) return;
        uint plate = 0xFFC0C8D8, dark = 0xFF6A7488, skin = 0xFFF0C8A0;
        bool armour = k.Armour;
        double swing = k.Ground && Math.Abs(k.Vx) > 1 ? Math.Sin(k.Walk * 14) * 7 : 0;
        if (k.Climbing)
        {
            double sw = Math.Sin(k.Walk * 12) * 4;
            dc.FillRectangle(Brush(armour ? plate : 0xFFE8E8E8), new Rect(x - 9, y - 40, 18, 22), 3);
            dc.DrawLine(Pen(armour ? dark : skin, 5), new Point(x - 5, y - 18), new Point(x - 5, y - 2 + sw));
            dc.DrawLine(Pen(armour ? dark : skin, 5), new Point(x + 5, y - 18), new Point(x + 5, y - 2 - sw));
            dc.DrawLine(Pen(armour ? plate : skin, 4), new Point(x - 9, y - 36), new Point(x - 12, y - 50 + sw));
            dc.DrawLine(Pen(armour ? plate : skin, 4), new Point(x + 9, y - 36), new Point(x + 12, y - 50 - sw));
            dc.FillEllipse(x, y - 46, 8, armour ? plate : skin);
            if (armour) dc.FillRectangle(Brush(0xFFD03030), new Rect(x - 2, y - 58, 4, 8));
            return;
        }
        double h = k.Crouch ? 16 : 0;
        // Legs.
        uint legs = armour ? dark : skin;
        if (k.Ground && !k.Crouch)
        {
            dc.DrawLine(Pen(legs, 6), new Point(x - 3, y - 18), new Point(x - 3 + swing, y - 2));
            dc.DrawLine(Pen(legs, 6), new Point(x + 3, y - 18), new Point(x + 3 - swing, y - 2));
        }
        else if (k.Crouch)
        {
            dc.DrawLine(Pen(legs, 6), new Point(x - 4, y - 8), new Point(x + 6 * f, y - 2));
            dc.DrawLine(Pen(legs, 6), new Point(x + 2, y - 8), new Point(x - 6 * f, y - 2));
        }
        else
        {
            dc.DrawLine(Pen(legs, 6), new Point(x - 3, y - 18), new Point(x - 8 * f, y - 8));
            dc.DrawLine(Pen(legs, 6), new Point(x + 3, y - 18), new Point(x + 6 * f, y - 6));
        }
        // Body: plate, or (the armour gone) his undershirt and striped shorts.
        if (armour)
        {
            dc.FillRectangle(Brush(plate), new Rect(x - 10, y - 40 + h, 20, 24 - (k.Crouch ? 6 : 0)), 3);
            dc.FillRectangle(Brush(dark), new Rect(x - 10, y - 22 + h * 0.6, 20, 4));
        }
        else
        {
            dc.FillRectangle(Brush(skin), new Rect(x - 9, y - 40 + h, 18, 16), 3);
            dc.FillRectangle(Brush(0xFFF0F0F0), new Rect(x - 10, y - 26 + h * 0.6, 20, 9), 2);
            dc.FillRectangle(Brush(0xFFE04040), new Rect(x - 10, y - 23 + h * 0.6, 20, 3));
        }
        // Arm: thrown forward just after a throw.
        bool throwing = k.ThrowCool > 0.08;
        uint arm = armour ? plate : skin;
        if (throwing) dc.DrawLine(Pen(arm, 5), new Point(x + 4 * f, y - 34 + h), new Point(x + 18 * f, y - 32 + h));
        else dc.DrawLine(Pen(arm, 5), new Point(x + 4 * f, y - 34 + h), new Point(x + 6 * f - swing * 0.4, y - 20 + h));
        // Head: a helmet with a red plume, or just his head.
        if (armour)
        {
            dc.FillRectangle(Brush(plate), new Rect(x - 9, y - 56 + h, 18, 17), 5);
            dc.FillRectangle(Brush(0xFF20242E), new Rect(x + (f > 0 ? 1 : -9), y - 50 + h, 8, 3));
            Poly(dc, 0xFFD03030, null, 0, new Point(x - 2, y - 56 + h), new Point(x - 12 * f, y - 66 + h), new Point(x + 4, y - 60 + h));
        }
        else
        {
            dc.FillEllipse(x, y - 48 + h, 8, skin);
            dc.FillEllipse(x + 3 * f, y - 49 + h, 1.6, 0xFF101010);
            dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(x - 8, y - 57 + h, 16, 5), 2);
        }
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        double x = s.X, y = s.Y;
        int d = s.Vx >= 0 ? 1 : -1;
        if (s.Burn > 0)
        {
            for (int i = 0; i < 3; i++)
            {
                double fl = Math.Sin(_clock * 20 + i * 2) * 4;
                Poly(dc, i == 1 ? 0xFFFFE060 : 0xFFFF7A20, null, 0, new Point(x - 14 + i * 9, y), new Point(x - 10 + i * 9, y - 22 - fl), new Point(x - 4 + i * 9, y));
            }
            return;
        }
        switch (s.Kind)
        {
            case Weapon.Lance:
                dc.DrawLine(Pen(0xFF8A6A3A, 3), new Point(x - 22 * d, y), new Point(x + 8 * d, y));
                Poly(dc, 0xFFE0E0F0, null, 0, new Point(x + 8 * d, y - 4), new Point(x + 20 * d, y), new Point(x + 8 * d, y + 4));
                break;
            case Weapon.Dagger:
                Poly(dc, 0xFFE0E8F8, null, 0, new Point(x - 4 * d, y - 3), new Point(x + 12 * d, y), new Point(x - 4 * d, y + 3));
                dc.FillRectangle(Brush(0xFF8A5A2A), new Rect(x - 10 * d - (d < 0 ? 0 : 0), y - 2, 7, 4));
                break;
            case Weapon.Torch:
                dc.DrawLine(Pen(0xFF6A4A2A, 4), new Point(x - 8, y + 6), new Point(x + 4, y - 4));
                dc.FillEllipse(x + 5, y - 6, 6 + Math.Sin(_clock * 30) * 1.5, 0xFFFF9A20);
                dc.FillEllipse(x + 5, y - 6, 3, 0xFFFFE080);
                break;
            case Weapon.Axe:
            {
                double a = _clock * 18 * d;
                for (int i = 0; i < 2; i++)
                {
                    double aa = a + i * Math.PI;
                    Poly(dc, 0xFFD0D8E8, null, 0, new Point(x, y), new Point(x + Math.Cos(aa - 0.4) * 13, y + Math.Sin(aa - 0.4) * 13), new Point(x + Math.Cos(aa + 0.4) * 13, y + Math.Sin(aa + 0.4) * 13));
                }
                dc.FillEllipse(x, y, 3, 0xFF8A6A3A);
                break;
            }
        }
    }

    private void DrawMissile(DrawingContext dc, Missile m)
    {
        if (m.Gravity > 0 && m.Gravity < 700)
        {
            dc.FillEllipse(m.X, m.Y, 8, 0xFFFF6A1A);
            dc.FillEllipse(m.X, m.Y, 4, 0xFFFFE070);
        }
        else if (m.Gravity > 0)
        {
            // A plant's eyeball.
            dc.FillEllipse(m.X, m.Y, 7, 0xFFF0F0F0);
            dc.FillEllipse(m.X + 2, m.Y, 3, 0xFFD02020);
        }
        else
        {
            dc.FillEllipse(m.X, m.Y, 9 + Math.Sin(_clock * 30) * 1.5, Alpha(0xFFFF4A1A, 0.6));
            dc.FillEllipse(m.X, m.Y, 5, 0xFFFFC040);
        }
    }

    private void DrawItem(DrawingContext dc, Item i)
    {
        double x = i.X, y = i.Y;
        if (i.Key)
        {
            double bob = Math.Sin(_clock * 4) * 3;
            dc.DrawEllipse(null, Pen(0xFFFFD040, 4), new Point(x - 10, y - 16 + bob), 8, 8);
            dc.DrawLine(Pen(0xFFFFD040, 4), new Point(x - 2, y - 16 + bob), new Point(x + 16, y - 16 + bob));
            dc.DrawLine(Pen(0xFFFFD040, 3), new Point(x + 12, y - 16 + bob), new Point(x + 12, y - 8 + bob));
            dc.DrawLine(Pen(0xFFFFD040, 3), new Point(x + 6, y - 16 + bob), new Point(x + 6, y - 10 + bob));
            return;
        }
        if (i.Pot)
        {
            dc.DrawEllipse(Brush(0xFFB0704A), Pen(0xFF6A3A1A, 2), new Point(x, y - 14), 13, 14);
            dc.FillRectangle(Brush(0xFFB0704A), new Rect(x - 7, y - 32, 14, 8));
            dc.FillRectangle(Brush(0xFF6A3A1A), new Rect(x - 9, y - 33, 18, 3));
            return;
        }
        if (i.Life < 3 && (int)(_clock * 8) % 2 == 0) return;
        switch (i.Loot)
        {
            case Loot.Armour:
                dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(x - 12, y - 26, 24, 22), 4);
                dc.DrawLine(Pen(0xFF6A7488, 2), new Point(x, y - 26), new Point(x, y - 6));
                break;
            case Loot.Money:
                dc.FillEllipse(x, y - 11, 11, 0xFF8A6A3A);
                dc.FillRectangle(Brush(0xFF8A6A3A), new Rect(x - 5, y - 26, 10, 6));
                Text(dc, "$", new Point(x, y - 11), 18, 0xFFFFE14A, center: true, shadow: false);
                break;
            default:
                DrawShot(dc, new Shot { X = x, Y = y - 12, Vx = 1, Kind = i.Weapon });
                dc.DrawEllipse(null, Pen(Alpha(0xFFFFE14A, 0.5 + 0.4 * Math.Sin(_clock * 6)), 2), new Point(x, y - 12), 18, 18);
                break;
        }
    }

    // ───────────────────────────── creatures ─────────────────────────────

    private void DrawEnemy(DrawingContext dc, Enemy e)
    {
        double x = e.X, y = e.Y;
        int f = e.Facing;
        bool flash = e.Hurt > 0 && (int)(_clock * 30) % 2 == 0;
        uint W(uint c) => flash ? 0xFFFFFFFF : c;
        switch (e.Kind)
        {
            case Foe.Zombie:
            {
                // Rising out of the ground (only what's above it shows), shambling, sinking back.
                double up = e.State == 0 ? Math.Min(1, e.T / 0.8) : e.State == 2 ? Math.Max(0, 1 - (e.T - 9) / 0.8) : 1;
                using (dc.PushClip(new Rect(x - 30, y - 70, 60, 70)))
                using (dc.PushTransform(Matrix.CreateTranslation(0, (1 - up) * 46)))
                {
                    double sw = Math.Sin(e.T * 6) * 4;
                    dc.DrawLine(Pen(W(0xFF4A6A3A), 6), new Point(x - 3, y - 18), new Point(x - 3 + sw, y));
                    dc.DrawLine(Pen(W(0xFF4A6A3A), 6), new Point(x + 3, y - 18), new Point(x + 3 - sw, y));
                    dc.FillRectangle(Brush(W(0xFF6A5A7A)), new Rect(x - 9, y - 38, 18, 22), 2);
                    dc.DrawLine(Pen(W(0xFF7AA05A), 4), new Point(x, y - 34), new Point(x + 18 * f, y - 30));
                    dc.FillEllipse(x + 2 * f, y - 44, 8, W(0xFF7AA05A));
                    dc.FillEllipse(x + 5 * f, y - 46, 2, 0xFFFF3A2A);
                }
                if (e.State != 1) for (int i = 0; i < 4; i++) dc.FillEllipse(x - 12 + i * 8, y - 2, 3, 0xFF3A2A1E);
                break;
            }
            case Foe.Crow:
            {
                double flap = e.State == 0 ? 0 : Math.Sin(e.T * 16) * 9;
                dc.FillEllipse(x, y - 10, 9, W(0xFF181820));
                Poly(dc, W(0xFF181820), null, 0, new Point(x - 4, y - 12), new Point(x - 22 * f, y - 14 - flap), new Point(x - 6 * f, y - 6));
                dc.FillEllipse(x + 8 * f, y - 16, 5, W(0xFF181820));
                Poly(dc, 0xFFE0A030, null, 0, new Point(x + 12 * f, y - 17), new Point(x + 19 * f, y - 15), new Point(x + 12 * f, y - 13));
                dc.FillEllipse(x + 9 * f, y - 17, 1.4, 0xFFFF3030);
                break;
            }
            case Foe.Plant:
            {
                double open = e.FireIn < 0.4 ? 8 : 3;
                dc.DrawLine(Pen(W(0xFF3A8A3A), 5), new Point(x, y), new Point(x + Math.Sin(_clock * 2) * 3, y - 26));
                Poly(dc, W(0xFF3A8A3A), null, 0, new Point(x, y - 6), new Point(x - 14, y - 14), new Point(x - 2, y - 12));
                dc.FillEllipse(x, y - 32, 11, W(0xFF6AC04A));
                dc.FillRectangle(Brush(0xFF3A0A0A), new Rect(x + (f > 0 ? 0 : -10), y - 33 - open / 2, 10, open));
                break;
            }
            case Foe.Ghost:
            {
                double wav = Math.Sin(_clock * 8) * 3;
                Poly(dc, W(Alpha(0xFFE0E8FF, 0.85)), null, 0, new Point(x - 14, y - 6), new Point(x - 14, y - 22), new Point(x, y - 32), new Point(x + 14, y - 22), new Point(x + 14, y - 6),
                    new Point(x + 7, y - 2 + wav), new Point(x, y - 8), new Point(x - 7, y - 2 - wav));
                dc.FillEllipse(x - 5, y - 20, 3, 0xFF101020);
                dc.FillEllipse(x + 5, y - 20, 3, 0xFF101020);
                break;
            }
            case Foe.Bat:
            {
                double flap = Math.Sin(e.T * 20) * 8;
                Poly(dc, W(0xFF2A1A3A), null, 0, new Point(x, y - 9), new Point(x - 16, y - 14 - flap), new Point(x - 8, y - 6));
                Poly(dc, W(0xFF2A1A3A), null, 0, new Point(x, y - 9), new Point(x + 16, y - 14 - flap), new Point(x + 8, y - 6));
                dc.FillEllipse(x, y - 9, 5, W(0xFF3A2A4A));
                dc.FillEllipse(x - 2, y - 10, 1.2, 0xFFFF3A2A);
                dc.FillEllipse(x + 2, y - 10, 1.2, 0xFFFF3A2A);
                break;
            }
            case Foe.Ogre:
            {
                double sw = e.State == 0 ? Math.Sin(e.T * 5) * 5 : 0;
                dc.DrawLine(Pen(W(0xFF5A3A2A), 9), new Point(x - 6, y - 22), new Point(x - 6 + sw, y));
                dc.DrawLine(Pen(W(0xFF5A3A2A), 9), new Point(x + 6, y - 22), new Point(x + 6 - sw, y));
                dc.FillRectangle(Brush(W(0xFF8A6A4A)), new Rect(x - 18, y - 58, 36, 38), 8);
                dc.FillRectangle(Brush(0xFF5A3A2A), new Rect(x - 18, y - 28, 36, 6));
                dc.FillEllipse(x + 4 * f, y - 62, 11, W(0xFFA08A5A));
                dc.FillEllipse(x + 8 * f, y - 64, 2, 0xFFFF2A1A);
                dc.DrawLine(Pen(0xFF6A4A2A, 7), new Point(x + 16 * f, y - 44), new Point(x + 30 * f, y - 72));
                dc.FillEllipse(x + 30 * f, y - 74, 7, 0xFF6A4A2A);
                break;
            }
            case Foe.Gargoyle or Foe.GargoyleLord:
                DrawGargoyle(dc, e, e.Kind == Foe.GargoyleLord ? 1.7 : 1, W(0xFFD02A2A));
                break;
            case Foe.Cyclops:
            {
                double sw = e.State == 0 ? Math.Sin(e.T * 4) * 6 : 0;
                dc.DrawLine(Pen(W(0xFF6A5A7A), 14), new Point(x - 12, y - 40), new Point(x - 12 + sw, y));
                dc.DrawLine(Pen(W(0xFF6A5A7A), 14), new Point(x + 12, y - 40), new Point(x + 12 - sw, y));
                dc.FillRectangle(Brush(W(0xFF8A7A9A)), new Rect(x - 30, y - 100, 60, 64), 14);
                dc.FillRectangle(Brush(0xFF5A3A2A), new Rect(x - 30, y - 46, 60, 10));
                dc.DrawLine(Pen(W(0xFF8A7A9A), 10), new Point(x + 26 * f, y - 88), new Point(x + 44 * f, y - 60));
                dc.FillEllipse(x + 6 * f, y - 108, 20, W(0xFF9A8AAA));
                dc.FillEllipse(x + 10 * f, y - 110, 9, 0xFFF0F0F0);
                dc.FillEllipse(x + 13 * f, y - 110, 4, 0xFFD02020);
                Poly(dc, 0xFFE0D8C0, null, 0, new Point(x + 2 * f, y - 126), new Point(x + 6 * f, y - 142), new Point(x + 12 * f, y - 124));
                break;
            }
            case Foe.Serpent:
            {
                for (int i = e.Trail.Length - 8; i >= 8; i -= 8)
                {
                    var (bx, by) = e.Trail[i];
                    double r = 16 - i / 16.0;
                    dc.FillEllipse(bx, by, r + 2, 0xFF2A5A2A);
                    dc.FillEllipse(bx, by, r, (i / 8) % 2 == 0 ? 0xFF6AB04A : 0xFFC0A030);
                }
                dc.FillEllipse(x, y, 22, W(0xFF4A9A3A));
                Poly(dc, W(0xFF4A9A3A), null, 0, new Point(x + 10 * f, y - 10), new Point(x + 36 * f, y), new Point(x + 10 * f, y + 12));
                dc.FillEllipse(x + 8 * f, y - 8, 4, 0xFFFFE040);
                Poly(dc, 0xFFE0D8C0, null, 0, new Point(x - 6, y - 18), new Point(x - 14 * f, y - 36), new Point(x + 4, y - 20));
                break;
            }
            case Foe.Malgrim:
            {
                double flap = Math.Sin(_clock * 3) * 16;
                Poly(dc, W(0xFF3A0A1A), null, 0, new Point(x, y - 90), new Point(x - 110, y - 150 - flap), new Point(x - 70, y - 40));
                Poly(dc, W(0xFF3A0A1A), null, 0, new Point(x, y - 90), new Point(x + 110, y - 150 - flap), new Point(x + 70, y - 40));
                dc.FillRectangle(Brush(W(0xFF5A1A2A)), new Rect(x - 40, y - 110, 80, 100), 20);
                dc.FillEllipse(x, y - 124, 28, W(0xFF6A1A2A));
                dc.FillEllipse(x - 10, y - 128, 5, 0xFFFFE040);
                dc.FillEllipse(x + 10, y - 128, 5, 0xFFFFE040);
                Poly(dc, 0xFFE0D8C0, null, 0, new Point(x - 22, y - 140), new Point(x - 34, y - 176), new Point(x - 12, y - 146));
                Poly(dc, 0xFFE0D8C0, null, 0, new Point(x + 22, y - 140), new Point(x + 34, y - 176), new Point(x + 12, y - 146));
                Poly(dc, 0xFFD0A030, null, 0, new Point(x - 18, y - 150), new Point(x - 12, y - 164), new Point(x, y - 154), new Point(x + 12, y - 164), new Point(x + 18, y - 150));
                break;
            }
        }
        if (e.Boss) DrawBossBar(dc, e);
    }

    private void DrawGargoyle(DrawingContext dc, Enemy e, double k, uint body)
    {
        double x = e.X, y = e.Y - e.Height / 2;
        int f = e.Facing;
        double flap = Math.Sin(_clock * 14) * 12 * k;
        Poly(dc, 0xFF5A0A0A, null, 0, new Point(x, y - 6 * k), new Point(x - 34 * k, y - 24 * k - flap), new Point(x - 20 * k, y + 4 * k));
        Poly(dc, 0xFF5A0A0A, null, 0, new Point(x, y - 6 * k), new Point(x + 34 * k, y - 24 * k - flap), new Point(x + 20 * k, y + 4 * k));
        dc.FillEllipse(x, y, 13 * k, body);
        dc.FillEllipse(x + 4 * f * k, y - 14 * k, 9 * k, body);
        Poly(dc, 0xFFE0D8C0, null, 0, new Point(x - 4 * k, y - 20 * k), new Point(x - 10 * k, y - 32 * k), new Point(x, y - 22 * k));
        Poly(dc, 0xFFE0D8C0, null, 0, new Point(x + 6 * k, y - 20 * k), new Point(x + 10 * k, y - 32 * k), new Point(x + 10 * k, y - 20 * k));
        dc.FillEllipse(x + 7 * f * k, y - 15 * k, 2.4 * k, 0xFFFFE040);
        dc.DrawLine(Pen(body, 3 * k), new Point(x - 4 * k, y + 10 * k), new Point(x - 8 * k, y + 20 * k));
        dc.DrawLine(Pen(body, 3 * k), new Point(x + 4 * k, y + 10 * k), new Point(x + 8 * k, y + 20 * k));
    }

    private void DrawBossBar(DrawingContext dc, Enemy e)
    {
        var w = Engine.World;
        double x = w.CamX + Field.Width / 2;
        Text(dc, e.Kind switch { Foe.Cyclops => "THE CYCLOPS", Foe.GargoyleLord => "THE GARGOYLE LORD", Foe.Serpent => "THE SERPENT", _ => "MALGRIM" }, new Point(x, Field.Top + 22), 26, 0xFFFFC0C0, center: true);
        double width = Math.Min(400, e.Hp * 16);
        dc.FillRectangle(Brush(0xFF400A0A), new Rect(x - 200, Field.Top + 40, 400, 8));
        dc.FillRectangle(Brush(0xFFE03A2A), new Rect(x - 200, Field.Top + 40, width, 8));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double a = 1 - e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 40), 22, Alpha(e.Colour, a), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 30), 26, Alpha(e.Colour, Math.Min(1, a * 2)), center: true);
                break;
            case EffectKind.Burst:
            {
                int n = int.TryParse(e.Text, out int v) ? v : 10;
                for (int i = 0; i < n; i++)
                {
                    double ang = i * Math.PI * 2 / n, r = 6 + e.Age * 160;
                    dc.FillEllipse(e.X + Math.Cos(ang) * r, e.Y + Math.Sin(ang) * r, 3.5 * a + 1, Alpha(e.Colour, a));
                }
                break;
            }
            case EffectKind.Spark:
                dc.FillEllipse(e.X, e.Y, 6 * a + 2, Alpha(e.Colour, a));
                break;
        }
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(0xFF0A0810), new Rect(0, 0, Field.Width, Field.Hud));
        dc.DrawLine(Pen(0xFF4A3A5A, 2), new Point(0, Field.Hud), new Point(Field.Width, Field.Hud));
        Text(dc, "SCORE", new Point(20, 18), 20, Dim);
        Text(dc, g.Score.ToString("000000"), new Point(20, 44), 34, Yellow);
        Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("000000"), new Point(170, 44), 24, Dim);
        if (!InGame) return;
        var left = TimeSpan.FromSeconds(Math.Ceiling(w.TimeLeft));
        Text(dc, "TIME", new Point(Field.Width / 2, 16), 20, Dim, center: true);
        Text(dc, $"{left.Minutes}:{left.Seconds:00}", new Point(Field.Width / 2, 44), 36, w.TimeLeft < 30 && (int)(_clock * 3) % 2 == 0 ? Red : White, center: true);
        Text(dc, w.Stage.Name, new Point(Field.Width / 2 + 200, 30), 22, 0xFFC0B0E0, center: true);
        // The weapon in hand, and the knights left.
        dc.DrawRectangle(Brush(0xFF1A1424), Pen(0xFF4A3A5A, 2), new Rect(Field.Width - 230, 10, 52, 44), 4, 4);
        DrawShot(dc, new Shot { X = Field.Width - 204, Y = 32, Vx = 1, Kind = w.Knight.Weapon });
        for (int i = 0; i < Math.Min(g.Lives, 6); i++)
        {
            double hx = Field.Width - 150 + i * 24;
            dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(hx - 8, 22, 16, 16), 5);
            Poly(dc, 0xFFD03030, null, 0, new Point(hx - 2, 22), new Point(hx - 10, 14), new Point(hx + 4, 18));
        }
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 210;
        dc.FillRectangle(Brush(0xA0000000), new Rect(cx - 400, cy - 52, 800, 108), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 18 ? 48 : 60, 0xFFFFC040, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        var th = Themes[0];
        for (int i = 0; i < 8; i++) dc.FillRectangle(Brush(i < 4 ? th.SkyTop : th.SkyLow), new Rect(0, Field.Hud + i * 88, Field.Width, 89));
        dc.FillEllipse(900, 420, 50, 0xFFE8E4C8);
        dc.FillEllipse(884, 408, 9, 0xFFC8C4A8);
        for (int i = 0; i < 6; i++) DrawDeadTree(dc, 60 + i * 190, 720, 0xFF0A0818);
        dc.FillRectangle(Brush(th.Earth), new Rect(0, 690, Field.Width, 80));
        dc.FillRectangle(Brush(th.Edge), new Rect(0, 690, Field.Width, 6));
        foreach (var gx in new[] { 150, 330, 620, 880 })
        {
            dc.FillRectangle(Brush(0xFF8A8A9A), new Rect(gx, 650, 24, 40), 3);
            dc.DrawEllipse(Brush(0xFF8A8A9A), null, new Point(gx + 12, 652), 12, 8);
        }
        // Sir Hollin, marching across.
        var k = new Knight { X = (_clock * 70) % 1100 - 40, Y = 690, Ground = true, Vx = 1, Walk = _clock, Facing = 1 };
        DrawKnight(dc, k);
        string title = "AVA GHOSTS 'N GOBLINS";
        for (int i = 0; i < title.Length; i++)
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 22 + 22 + i * 44, 170 + Math.Sin(_clock * 2.5 + i * 0.4) * 3), 84, i % 2 == 0 ? 0xFFE04A3A : 0xFFFFC040, center: true);
        Text(dc, "AFTER CAPCOM'S 1985 ARCADE GAME", new Point(cx, 232), 24, Dim, center: true);
        Text(dc, "THE DEMON MALGRIM HAS CARRIED OFF THE PRINCESS ELOWEN.", new Point(cx, 300), 28, White, center: true);
        Text(dc, "SIR HOLLIN MUST CROSS FOUR STAGES OF HIS REALM TO SAVE HER.", new Point(cx, 332), 28, White, center: true);
        Text(dc, "ONE HIT TAKES HIS ARMOUR. THE NEXT TAKES HIM.", new Point(cx, 380), 28, 0xFFFF8A6A, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO BEGIN", new Point(cx, 470), 44, Yellow, center: true);
        Text(dc, "ARROWS: WALK, CLIMB, CROUCH    SPACE OR Z: JUMP    X OR CTRL: THROW    P: PAUSE    M: SOUND", new Point(cx, 530), 20, 0xFFB0A0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0A0812), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "THE BRAVEST KNIGHTS", new Point(cx, 140), 64, 0xFFE04A3A, center: true);
        Text(dc, "RANK NAME    SCORE  STAGES", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF0A0812), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
