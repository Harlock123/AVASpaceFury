# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game and its controls;
this file covers how the code is put together and the rules that keep it working.

## What this is

A colour vector space shooter in the style of Sega's 1981 arcade game Space Fury, in C# on .NET 10 and
Avalonia 12, drawn with Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS
and Linux, x64 and ARM). The graphics are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASpaceFury.slnx                    # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASpaceFury.slnx                     # every test runs headless: no window opens
dotnet run --project src/AVASpaceFury             # play it; --no-audio for silence
FURY_SHOTS=$PWD/Images dotnet test tests/AVASpaceFury.Tests --filter ScreenshotTests   # refresh the README's pictures
FURY_PROBE=1 dotnet test tests/AVASpaceFury.Tests --filter PilotProbe   # how the computer pilot gets on ("lazy" for a dumb one)
python3 tools/make_sounds.py                      # rebuild src/AVASpaceFury/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `net10.0`, `TreatWarningsAsErrors`, nullable reference types and implicit usings for
every project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASpaceFury/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` is a wave in play: the ship in wrapping space, fragments joining into Raiders and
    Raiders into Marauders, shots and hits, and docking with add-on modules between waves.
  - `Entities.cs` has `Controls`, the screen measures (`Field`), the `Module` kinds and the things in space
    (`Ship`, `Shot`, `Fragment`, `Raider`, `Pod`).
  - `ShipPilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASpaceFury/Views/`: `GameCanvas` (split over partial files) draws the frame and runs the 60 Hz timer.
- `src/AVASpaceFury/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVASpaceFury.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`.
  - `GameplayTests.cs` holds the gameplay tests.
  - `ScreenshotTests.cs` draws the README's pictures.
  - `PilotProbe.cs` is a tuning probe that does nothing unless `FURY_PROBE` is set.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` advances the game in fixed 1/60 s steps (`Field.Step`), independent of
  the frame rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`
  (`new Random(seed)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** `Controls.Dx` turns the ship (-1 left, 1 right). Thrust is `Dy = -1`;
  there is no reverse, so `Dy` is never +1. Up on the keys, the D-pad and the stick (negative Y) all mean thrust.
  Test new controls through `InputHandler`.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder
  (`SpecialFolder.ApplicationData/AVASpaceFury`). Tests load the table from a fresh temporary folder and set
  `Silent = true`. The engine's internal `Calm` flag stops anything coming, merging or firing by itself.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features and controls) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64, kept as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
After a release it asks the portfolio site (Harlock123.github.io) to rebuild, if `SITE_REFRESH_TOKEN` is set.
