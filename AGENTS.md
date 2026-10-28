# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A run-and-throw platform game in the style of Capcom's 1985 arcade game Ghosts 'n Goblins, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and
ARM). Everything in it is original: the characters and graphics are drawn in code, and the sounds and tunes are
synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAGhostsNGoblins.slnx               # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAGhostsNGoblins.slnx                # every test runs headless: no window opens
dotnet run --project src/AVAGhostsNGoblins        # play it; add -- --no-audio for silence
GOBLINS_SHOTS=$PWD/Images dotnet test tests/AVAGhostsNGoblins.Tests --filter ScreenshotTests   # refresh the README's pictures
GOBLINS_PROBE=1 dotnet test tests/AVAGhostsNGoblins.Tests --filter PilotProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                      # rebuild src/AVAGhostsNGoblins/Sounds (standard-library Python only)
```

The probe reports how the computer knight gets on in each stage, for tuning; `GOBLINS_PROBE=lazy` runs a knight
who only walks on and throws. Without the variable it does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAGhostsNGoblins/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, game over, initials, high scores) and the
    fixed-step loop.
  - `Sim.cs` plays a stage one 1/60 s step at a time on a `World`, touching nothing outside it.
  - `World.cs` holds everything in a stage that moves or changes (the knight, shots, enemies, items, ledges),
    kept together so it can be copied whole, plus the copyable `Dice` random source.
  - `Stage.cs` has the screen measures (`Field`), `Controls`, the tiles, foes and weapons, and the four stages in
    `StageBook` (graveyard, village, caverns, tower).
  - `Pilot.cs` is the computer knight, used by the attract mode and the tests. It tries moves on copies of the
    world before choosing one.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepad) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAGhostsNGoblins/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024x768 picture scaled
  to the window and runs the 60 Hz timer.
- `src/AVAGhostsNGoblins/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVAGhostsNGoblins/Sounds/`: the generated WAV files.
- `tests/AVAGhostsNGoblins.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `StageTests` checks every stage can
  be jumped and is cleared by the computer knight.

## Rules that keep it working

- **Fixed steps.** The simulation advances in fixed 1/60 s steps (`Field.Step`), independent of the frame rate,
  so tests can play whole stages deterministically from a seed. Draw random numbers from the world's seeded
  `Dice` (`w.Dice`), never from `Random.Shared`.
- **Keep `World` copyable.** The pilot plays moves out on copies of the world. Any new state that changes during
  play belongs in `World` and must be copied with it; `Sim` must not touch anything outside the world it's given.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the world and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `dy` sign the engine
  expects (negative is up the screen; down is crouch). Test new controls through `InputHandler`, not by building
  `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAGhostsNGoblins`). A
  `GameEngine` built without a table loads that file, so tests load the table from a temporary folder and set
  `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If you change a stage, `StageTests` must still find it jumpable and clearable by the computer knight.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
