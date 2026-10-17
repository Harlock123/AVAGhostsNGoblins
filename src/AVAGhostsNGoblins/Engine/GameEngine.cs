namespace AVAGhostsNGoblins.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>How a stage is going.</summary>
public enum Phase
{
    /// <summary>The stage named; Sir Hollin at the ready.</summary>
    Start,
    Play,
    /// <summary>Caught: a moment, then back to the start (or the checkpoint), or the game is over.</summary>
    Dying,
    /// <summary>The key taken: the time left counted, then the next stage.</summary>
    Clear,
    /// <summary>Malgrim beaten: the princess free, then round again.</summary>
    Ending,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Jump, Throw, Hit, Kill, Pot, Pickup, Armour, ArmourOff, Die, Rise, Caw, Spit, Swoop, Thud, Flame, Key, ExtraLife, Bonus,
    BossCome, BossHit, BossDie,
    Music, MusicStop, StageClear, Victory, TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.5, DyingSeconds = 2.6, ClearSeconds = 4.5, EndingSeconds = 9;
    public const int Lives0 = 3, FirstExtra = 20000, ExtraEvery = 50000, BonusPerSecond = 10;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _checkpoint;
    private readonly int _seed;
    private int _tries, _nextExtra;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    /// <summary>Stages cleared this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || (c.Fire || c.Jump) && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _tries = 0;
        _nextExtra = FirstExtra;
        Begin(0, 1, fromCheckpoint: false, Weapon.Lance);
    }

    /// <summary>A stage begun: from its start, or from its checkpoint after a life lost past it.</summary>
    private void Begin(int stage, int round, bool fromCheckpoint, Weapon weapon)
    {
        int score = World.Score;
        Sim.Start(World, stage, round, fromCheckpoint, weapon, _seed * 1000 + stage * 31 + _tries++);
        World.Score = score;
        if (!fromCheckpoint) _checkpoint = false;
        SetPhase(Phase.Start);
        Say(World.Stage.Name, round > 1 ? $"ROUND {round}" : stage == 0 && !fromCheckpoint ? "RESCUE THE PRINCESS ELOWEN" : null, StartSeconds);
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void GameOver()
    {
        Music(false);
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                Sim.Step(w, default);
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
                int before = w.Score;
                Sim.Step(w, c);
                if (w.Knight.X > w.Stage.Checkpoint) _checkpoint = true;
                if (w.Score != before) ExtraLives();
                if (w.Knight.Dead) { SetPhase(Phase.Dying); Music(false); }
                else if (w.KeyTaken)
                {
                    SetPhase(w.StageIndex == StageBook.All.Length - 1 ? Phase.Ending : Phase.Clear);
                    Music(false);
                    Play(Phase == Phase.Ending ? Sfx.Victory : Sfx.StageClear);
                    if (Phase == Phase.Ending) Say("MALGRIM IS BEATEN!", "THE PRINCESS ELOWEN IS FREE", EndingSeconds);
                    else Say("STAGE CLEAR", $"TIME BONUS {(int)w.TimeLeft * BonusPerSecond}", ClearSeconds);
                }
                break;
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { GameOver(); return; }
                    Begin(w.StageIndex, w.Round, _checkpoint, Weapon.Lance);
                }
                break;
            case Phase.Clear or Phase.Ending:
                // The time left, counted into the score.
                if (w.TimeLeft > 0 && PhaseTime > 1)
                {
                    double take = Math.Min(w.TimeLeft, dt * 60);
                    int whole = (int)Math.Ceiling(w.TimeLeft) - (int)Math.Ceiling(w.TimeLeft - take);
                    w.TimeLeft -= take;
                    if (whole > 0) { w.Score += whole * BonusPerSecond; ExtraLives(); Play(Sfx.Bonus); }
                }
                if (PhaseTime > (Phase == Phase.Ending ? EndingSeconds : ClearSeconds))
                {
                    Cleared++;
                    bool last = w.StageIndex == StageBook.All.Length - 1;
                    Begin(last ? 0 : w.StageIndex + 1, last ? w.Round + 1 : w.Round, false, w.Knight.Weapon);
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a stage, past its name.</summary>
    public void Go(int stage = 0, int round = 1)
    {
        NewGame();
        State = GameState.Playing;
        Begin(stage, round, false, Weapon.Lance);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
