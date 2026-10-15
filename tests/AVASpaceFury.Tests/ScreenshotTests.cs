using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASpaceFury.Engine;
using AVASpaceFury.Views;

namespace AVASpaceFury.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set FURY_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaspacefury-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("FURY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASpaceFury-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
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
        g.HighScores.Insert("ACE", 84250, 12);
        g.HighScores.Insert("AVA", 61800, 9);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void The_Commander_and_the_fragments()
    {
        var (w, c, g) = Open();
        g.StartGame();
        g.SetLives(4);
        g.Idle(110);
        c.Advance(0.5);
        Shot(w, c, "commander");
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Fragments.Count >= 4 && g.Shots.Count(s => s.Mine) >= 1 && g.Ship.Alive));
        Shot(w, c, "assembling");
    }

    [AvaloniaFact]
    public void Docking()
    {
        var (w, c, g) = Open(3);
        g.StartGame();
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Dock && g.PhaseTime > 1.2 && g.Ship.Alive));
        Shot(w, c, "docking");
    }

    [AvaloniaFact]
    public void Marauders()
    {
        var (w, c, g) = Open(4);
        g.Go(4);
        g.SetLives(9);
        g.SetModule(Module.Spread);
        // Two Raiders close together, off to the side: they find each other and merge.
        g.PutRaider(820, 250);
        g.PutRaider(900, 300);
        Assert.True(PlayUntil(g, c, g => g.Raiders.Any(r => r.Marauder) && g.Ship.Alive, 60));
        for (int t = 0; t < 40; t++) { g.Tick(g.Pilot.Decide(g)); c.Advance(1 / 60.0); }
        Shot(w, c, "marauder");
    }

    [AvaloniaFact]
    public void A_ship_lost()
    {
        var (w, c, g) = Open(5);
        g.Go(2);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.WaveTime > 5 && g.Ship.Alive && g.Ship.Safe <= 0 && g.Remaining >= 3));
        g.PutShot(g.Ship.X, g.Ship.Y - 20, 0, 300);
        g.Idle(20);
        c.Advance(0.3);
        Shot(w, c, "lost");
    }
}
