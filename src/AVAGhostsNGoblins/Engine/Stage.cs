namespace AVAGhostsNGoblins.Engine;

/// <summary>Left and right, up and down (up the screen is -1; down is crouch), jump, and throw.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Jump = false, bool Fire = false);

/// <summary>The screen: the score bar, then a window onto the stage, 32 squares wide and 22 high.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 64;
    public const double Tile = 32;
    public const int Rows = 22, ViewCols = 32;
    public const double Top = Hud;
}

/// <summary>What a square of a stage is.</summary>
public enum Tile : byte
{
    Empty,
    /// <summary>Earth, rock or wall: nothing passes.</summary>
    Solid,
    /// <summary>A ledge: stood on from above, passed through from below.</summary>
    Ledge,
    Ladder,
    /// <summary>A gravestone: solid, and in the way of thrown things.</summary>
    Grave,
    /// <summary>Water or lava: death.</summary>
    Hazard,
}

public enum Foe
{
    Zombie, Crow, Plant, Ghost, Gargoyle, Ogre, Bat,
    // The ends of the stages.
    Cyclops, GargoyleLord, Serpent, Malgrim,
}

public enum Weapon { Lance, Dagger, Torch, Axe }

public enum Loot { Weapon, Armour, Money }

/// <summary>Something placed in a stage, which comes to life when it comes into view.</summary>
public sealed record Spawn(Foe Kind, double X, double Y);

/// <summary>A pot standing in a stage: break it for what's inside.</summary>
public sealed record Pot(double X, double Y, Loot Loot, Weapon Weapon = Weapon.Lance);

/// <summary>A ledge that slides to and fro (or up and down) between two places.</summary>
public sealed record Glide(double X0, double Y0, double X1, double Y1, int Width, double Speed);

/// <summary>
/// A stage: its squares, the creatures and pots placed in it, where creatures rise or swarm without end, where
/// a lost knight starts again, and what waits at the end.
/// </summary>
public sealed class StageDef
{
    public string Name = "";
    public int Cols;
    public Tile[,] Tiles = new Tile[0, 0];
    public List<Spawn> Spawns { get; } = [];
    public List<Pot> Pots { get; } = [];
    public List<Glide> Glides { get; } = [];
    /// <summary>Stretches where something keeps coming: zombies up out of the ground, ghosts, bats.</summary>
    public List<(double X0, double X1, Foe Kind, double Every)> Swarms { get; } = [];
    public double Checkpoint;
    public Foe Boss;
    public double Time = 180;
    public int Theme;

    /// <summary>Where the camera stops, for the fight at the end.</summary>
    public double ArenaX => (Cols - Field.ViewCols) * Field.Tile;

    public Tile At(int c, int r) => c < 0 || c >= Cols ? Tile.Solid : r < 0 ? Tile.Empty : r >= Field.Rows ? Tile.Hazard : Tiles[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor((y - Field.Top) / Field.Tile));

    /// <summary>The height of the ground's top at a column (the first solid or ledge square from the top), in screen pixels.</summary>
    public double FloorAt(int c, double fromY = Field.Top)
    {
        for (int r = Math.Max(0, (int)((fromY - Field.Top) / Field.Tile)); r < Field.Rows; r++)
            if (At(c, r) is Tile.Solid or Tile.Ledge or Tile.Grave) return Field.Top + r * Field.Tile;
        return double.MaxValue;
    }
}

/// <summary>Building a stage out of runs of ground, pits, steps, ledges, ladders and gravestones.</summary>
public sealed class StageBuilder
{
    private readonly StageDef _s;
    public StageBuilder(string name, int cols, int theme)
    {
        _s = new StageDef { Name = name, Cols = cols, Tiles = new Tile[cols, Field.Rows], Theme = theme };
    }

    /// <summary>Solid ground <paramref name="height"/> squares deep from column x0 to x1.</summary>
    public StageBuilder Ground(int x0, int x1, int height)
    {
        for (int x = x0; x <= x1; x++)
        for (int r = 0; r < Field.Rows; r++)
            _s.Tiles[x, r] = r >= Field.Rows - height ? Tile.Solid : _s.Tiles[x, r] == Tile.Solid ? Tile.Empty : _s.Tiles[x, r];
        return this;
    }

    /// <summary>A gap in the ground with water (or lava) at the bottom.</summary>
    public StageBuilder Pit(int x0, int x1)
    {
        for (int x = x0; x <= x1; x++)
        for (int r = 0; r < Field.Rows; r++)
            _s.Tiles[x, r] = r == Field.Rows - 1 ? Tile.Hazard : Tile.Empty;
        return this;
    }

    public StageBuilder Block(int x0, int x1, int r0, int r1)
    {
        for (int x = x0; x <= x1; x++)
        for (int r = r0; r <= r1; r++)
            _s.Tiles[x, r] = Tile.Solid;
        return this;
    }

    public StageBuilder Ledge(int x0, int x1, int row)
    {
        for (int x = x0; x <= x1; x++) _s.Tiles[x, row] = Tile.Ledge;
        return this;
    }

    /// <summary>A ladder in column x from a ledge (or ground) at row <paramref name="top"/> down to the ground below.</summary>
    public StageBuilder Ladder(int x, int top)
    {
        for (int r = top; r < Field.Rows && _s.Tiles[x, r] != Tile.Solid; r++) _s.Tiles[x, r] = Tile.Ladder;
        return this;
    }

    /// <summary>Gravestones standing on the ground at these columns.</summary>
    public StageBuilder Graves(params int[] cols)
    {
        foreach (int x in cols)
        {
            int r = 0;
            while (r < Field.Rows && _s.Tiles[x, r] != Tile.Solid) r++;
            _s.Tiles[x, r - 1] = Tile.Grave;
        }
        return this;
    }

    /// <summary>A roof of rock over the whole stage, <paramref name="depth"/> squares deep.</summary>
    public StageBuilder Ceiling(int depth)
    {
        for (int x = 0; x < _s.Cols; x++)
        for (int r = 0; r < depth; r++)
            _s.Tiles[x, r] = Tile.Solid;
        return this;
    }

    /// <summary>The top of the ground at a column, as a row.</summary>
    public int Surface(int x)
    {
        int r = 0;
        while (r < Field.Rows && _s.Tiles[x, r] is not (Tile.Solid or Tile.Grave)) r++;
        return r;
    }

    /// <summary>A creature, standing on the ground at column x (or at a given row's top).</summary>
    public StageBuilder Put(Foe kind, int x, int? row = null)
    {
        _s.Spawns.Add(new Spawn(kind, (x + 0.5) * Field.Tile, Field.Top + (row ?? Surface(x)) * Field.Tile));
        return this;
    }

    public StageBuilder PotAt(int x, Loot loot, Weapon weapon = Weapon.Lance, int? row = null)
    {
        _s.Pots.Add(new Pot((x + 0.5) * Field.Tile, Field.Top + (row ?? Surface(x)) * Field.Tile, loot, weapon));
        return this;
    }

    public StageBuilder Glide(int x0, int row0, int x1, int row1, int width, double speed)
    {
        _s.Glides.Add(new Glide(x0 * Field.Tile, Field.Top + row0 * Field.Tile, x1 * Field.Tile, Field.Top + row1 * Field.Tile, width, speed));
        return this;
    }

    public StageBuilder Swarm(int x0, int x1, Foe kind, double every)
    {
        _s.Swarms.Add((x0 * Field.Tile, x1 * Field.Tile, kind, every));
        return this;
    }

    public StageDef End(Foe boss, int checkpoint, double time = 180)
    {
        _s.Boss = boss;
        _s.Checkpoint = (checkpoint + 0.5) * Field.Tile;
        _s.Time = time;
        return _s;
    }
}

/// <summary>The four stages, from the graveyard to the demon's tower.</summary>
public static class StageBook
{
    public static readonly StageDef[] All = [Graveyard(), Village(), Caverns(), Tower()];

    private static StageDef Graveyard() =>
        new StageBuilder("THE GRAVEYARD", 180, 0)
            .Ground(0, 179, 3)
            .Ground(70, 95, 5)
            .Pit(52, 54).Pit(118, 120)
            .Graves(12, 20, 29, 38, 46, 60, 66, 76, 86, 100, 108, 126, 134, 142)
            .Ledge(26, 36, 13).Ladder(31, 13)
            .PotAt(33, Loot.Armour, row: 13)
            .PotAt(62, Loot.Weapon, Weapon.Dagger)
            .PotAt(112, Loot.Money)
            .Swarm(6, 50, Foe.Zombie, 2.6).Swarm(96, 146, Foe.Zombie, 2.2)
            .Put(Foe.Crow, 38).Put(Foe.Crow, 100).Put(Foe.Crow, 134)
            .Put(Foe.Plant, 90)
            .Put(Foe.Gargoyle, 138)
            .End(Foe.Cyclops, 90);

    private static StageDef Village() =>
        new StageBuilder("THE HAUNTED VILLAGE", 190, 1)
            .Ground(0, 189, 3)
            .Pit(64, 66).Pit(132, 134)
            // Cottages low enough to jump onto, with roof ledges; one has a loft above, up a ladder.
            .Block(20, 27, 17, 18).Ledge(19, 28, 16)
            .Block(42, 49, 17, 18).Ledge(41, 50, 16).Ledge(41, 50, 12).Ladder(40, 12)
            .Block(84, 92, 17, 18).Ledge(83, 93, 16)
            .Block(108, 114, 17, 18).Ledge(107, 115, 16)
            .Graves(10, 34, 58, 74, 100, 124, 146)
            .PotAt(45, Loot.Armour, row: 12)
            .PotAt(70, Loot.Weapon, Weapon.Axe)
            .PotAt(120, Loot.Money)
            .PotAt(150, Loot.Weapon, Weapon.Lance)
            .Swarm(30, 60, Foe.Ghost, 3.5).Swarm(96, 150, Foe.Ghost, 3)
            .Put(Foe.Crow, 34).Put(Foe.Crow, 74).Put(Foe.Crow, 124)
            .Put(Foe.Ogre, 78).Put(Foe.Ogre, 140)
            .Put(Foe.Gargoyle, 104)
            .End(Foe.GargoyleLord, 96);

    private static StageDef Caverns() =>
        new StageBuilder("THE FROZEN CAVERNS", 170, 2)
            .Ground(0, 169, 3)
            .Pit(30, 36).Glide(30, 18, 33, 18, 3, 60)
            .Pit(70, 77).Glide(70, 18, 74, 18, 3, 70)
            .Ground(90, 110, 6)
            .Pit(120, 126).Glide(120, 17, 123, 15, 3, 50)
            // (The roof last: laying ground clears the rock above it.)
            .Ceiling(4)
            .Ledge(46, 54, 14).Ladder(50, 14)
            .PotAt(52, Loot.Armour, row: 14)
            .PotAt(100, Loot.Weapon, Weapon.Dagger)
            .PotAt(134, Loot.Money)
            .Swarm(8, 68, Foe.Bat, 2.4).Swarm(80, 140, Foe.Bat, 2)
            .Put(Foe.Plant, 42).Put(Foe.Plant, 104).Put(Foe.Plant, 116)
            .Put(Foe.Ogre, 60)
            .Put(Foe.Gargoyle, 132)
            .End(Foe.Serpent, 88);

    private static StageDef Tower() =>
        new StageBuilder("THE DEMON'S TOWER", 170, 3)
            .Ground(0, 169, 3)
            .Pit(36, 38).Pit(84, 86).Pit(120, 122)
            .Ground(50, 70, 6)
            .Block(96, 100, 17, 18)
            .Ledge(24, 32, 13).Ladder(28, 13)
            .Ledge(104, 114, 12).Ladder(110, 12)
            .PotAt(30, Loot.Armour, row: 13)
            .PotAt(60, Loot.Weapon, Weapon.Lance)
            .PotAt(112, Loot.Armour, row: 12)
            .Swarm(10, 80, Foe.Ghost, 2.8).Swarm(90, 136, Foe.Bat, 2.2)
            .Put(Foe.Gargoyle, 46).Put(Foe.Gargoyle, 126)
            .Put(Foe.Ogre, 66).Put(Foe.Plant, 104)
            .End(Foe.Malgrim, 88);
}
