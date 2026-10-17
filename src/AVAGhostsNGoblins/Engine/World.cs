namespace AVAGhostsNGoblins.Engine;

/// <summary>A small random number generator whose state can be copied (so a copy of the world plays out the same).</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Between(double a, double b) => a + Next() * (b - a);
}

/// <summary>Sir Hollin: where his feet are, which way he faces, and what he's doing.</summary>
public sealed class Knight
{
    public const double HalfWidth = 11, Height = 44, Low = 28;
    public const double WalkSpeed = 150, JumpSpeed = 600, AirSpeed = 170, ClimbSpeed = 110;
    public double X, Y, Vx, Vy, Walk, Hurt, ThrowCool, DeadTime;
    public int Facing = 1;
    public bool Ground, Climbing, Crouch, Armour = true, Dead, JumpPrev, FirePrev;
    public Weapon Weapon = Weapon.Lance;
    public double Top => Y - (Crouch ? Low : Height);
    public Knight Copy() => (Knight)MemberwiseClone();
}

/// <summary>A thrown weapon in flight (or a torch's fire burning on the ground).</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy, Life, Burn;
    public Weapon Kind;
    public bool Dead;
    /// <summary>Who it's already hit (an axe goes on through).</summary>
    public int Pierced;
    public Shot Copy() => (Shot)MemberwiseClone();
}

public sealed class Enemy
{
    public Foe Kind;
    public double X, Y, Vx, Vy, T, FireIn, Hurt, HomeX, HomeY, Width = 26, Height = 40;
    /// <summary>A gargoyle's dodging: how long until it can slip aside again.</summary>
    public double Dodge;
    public int Hp = 1, Facing = -1, State;
    public bool Dead, Boss;
    /// <summary>The serpent's body: where its head has been.</summary>
    public (double X, double Y)[] Trail = [];
    public Enemy Copy()
    {
        var e = (Enemy)MemberwiseClone();
        e.Trail = (ValueTuple<double, double>[])Trail.Clone();
        return e;
    }
    public bool Flying => Kind is Foe.Crow or Foe.Ghost or Foe.Gargoyle or Foe.Bat or Foe.GargoyleLord or Foe.Serpent or Foe.Malgrim;
}

/// <summary>Something an enemy throws or spits.</summary>
public sealed class Missile
{
    public double X, Y, Vx, Vy, Life, Gravity;
    public bool Dead;
    public Missile Copy() => (Missile)MemberwiseClone();
}

/// <summary>A pot (still whole), or what came out of one (or a key, at the end).</summary>
public sealed class Item
{
    public double X, Y, Vy, Life = 12;
    public Loot Loot;
    public Weapon Weapon;
    public bool Pot, Key, Dead;
    public Item Copy() => (Item)MemberwiseClone();
}

/// <summary>A gliding ledge, and where it is now.</summary>
public sealed class Platform
{
    public Glide Path = new(0, 0, 0, 0, 3, 1);
    public double X, Y, T, Dx, Dy;
    public Platform Copy() => (Platform)MemberwiseClone();
    public double Width => Path.Width * Field.Tile;
}

public enum EffectKind { Score, Burst, Word, Spark }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

/// <summary>
/// Everything in a stage that moves or changes, kept together so it can be copied whole: the computer player
/// tries its moves out on a copy before making them.
/// </summary>
public sealed class World
{
    public StageDef Stage = StageBook.All[0];
    public int StageIndex, Round = 1;
    public Knight Knight = new();
    public List<Shot> Shots = [];
    public List<Enemy> Enemies = [];
    public List<Missile> Missiles = [];
    public List<Item> Items = [];
    public List<Platform> Platforms = [];
    public List<Effect> Effects = [];
    public double CamX, TimeLeft, Clock;
    public int Score;
    /// <summary>Which of the stage's placed creatures have come to life already.</summary>
    public bool[] Spawned = [];
    public double[] SwarmIn = [];
    public bool BossUp, BossDown, KeyTaken;
    /// <summary>When the key was taken (by the stage's clock).</summary>
    public double KeyAt;
    public Dice Dice;
    /// <summary>Sounds asked for this step (the game plays them; a copy's are thrown away).</summary>
    public List<Sfx> Heard = [];
    public string Cause = "";

    public World Copy() => new()
    {
        Stage = Stage, StageIndex = StageIndex, Round = Round, Knight = Knight.Copy(),
        Shots = Shots.Select(s => s.Copy()).ToList(),
        Enemies = Enemies.Select(e => e.Copy()).ToList(),
        Missiles = Missiles.Select(m => m.Copy()).ToList(),
        Items = Items.Select(i => i.Copy()).ToList(),
        Platforms = Platforms.Select(p => p.Copy()).ToList(),
        CamX = CamX, TimeLeft = TimeLeft, Clock = Clock, Score = Score,
        Spawned = (bool[])Spawned.Clone(), SwarmIn = (double[])SwarmIn.Clone(),
        BossUp = BossUp, BossDown = BossDown, KeyTaken = KeyTaken, KeyAt = KeyAt, Dice = Dice, Cause = Cause,
    };
}
