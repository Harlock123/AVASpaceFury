using AVASpaceFury.Engine;

namespace AVASpaceFury.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaspacefury-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A wave in play with nothing coming, merging or firing unless a test makes it.</summary>
    private static GameEngine Game(int wave = 1)
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        g.SetToCome(0);
        return g;
    }

    /// <summary>A real game in play, with plenty of ships.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void The_ship_turns_thrusts_drifts_and_slows()
    {
        var g = Game();
        g.Run(23, new Controls(Dx: 1));
        Assert.InRange(g.Ship.Angle, 1.4, 1.75);
        g.Run(30, new Controls(Dy: -1));
        Assert.True(g.Ship.Vx > 100);
        double x = g.Ship.X;
        g.Idle(30);
        Assert.True(g.Ship.X > x, "it drifts on");
        Assert.True(g.Ship.Vx < 180);
    }

    [Fact]
    public void Space_wraps_at_every_edge()
    {
        var g = Game();
        g.SetShip(Field.Width - 5, Field.CentreY, Math.PI / 2);
        g.Run(20, new Controls(Dy: -1));
        Assert.InRange(g.Ship.X, 0, 100);
        g.SetShip(Field.CentreX, Field.Top + 3);
        g.Run(20, new Controls(Dy: -1));
        Assert.True(g.Ship.Y > Field.Height - 100);
    }

    [Fact]
    public void The_guns_fire_a_few_at_a_time()
    {
        var g = Game();
        g.Run(120, FireButton);
        Assert.InRange(g.Shots.Count(s => s.Mine), 3, 4);
    }

    [Theory]
    [InlineData(Module.Twin, 2)]
    [InlineData(Module.Spread, 3)]
    [InlineData(Module.Tail, 2)]
    [InlineData(Module.Rapid, 1)]
    public void Each_module_changes_the_guns(Module module, int perVolley)
    {
        var g = Game();
        g.SetModule(module);
        g.Tick(FireButton);
        Assert.Equal(perVolley, g.Shots.Count(s => s.Mine));
        if (module == Module.Tail) Assert.Contains(g.Shots, s => s.Vy > 0);
        if (module == Module.Rapid)
        {
            g.Run(30, FireButton);
            Assert.True(g.Shots.Count(s => s.Mine) > 4);
        }
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    [Fact]
    public void Three_fragments_fly_together_and_assemble_into_a_Raider()
    {
        var g = Game();
        g.SetShip(100, 150);
        g.PutTriad(700, 500);
        for (int i = 0; i < 60 * 5 && g.Raiders.Count == 0; i++) g.Tick(default);
        Assert.Empty(g.Fragments);
        var r = Assert.Single(g.Raiders);
        Assert.InRange(r.X, 690, 710);
    }

    [Fact]
    public void Shooting_a_fragment_leaves_two_and_two_still_make_a_Raider()
    {
        var g = Game();
        g.SetShip(100, 150);
        var three = g.PutTriad(700, 500);
        g.PutShot(three[0].X, three[0].Y + 30, 0, -600, mine: true);
        g.Idle(5);
        Assert.Equal(2, g.Fragments.Count);
        Assert.Equal(GameEngine.FragmentPoints, g.Score);
        for (int i = 0; i < 60 * 5 && g.Raiders.Count == 0; i++) g.Tick(default);
        Assert.Single(g.Raiders);
    }

    [Fact]
    public void The_last_of_three_comes_straight_for_the_ship()
    {
        var g = Game();
        g.SetShip(200, 200);
        var three = g.PutTriad(700, 500);
        foreach (var f in three.Take(2)) g.PutShot(f.X, f.Y + 30, 0, -600, mine: true);
        g.Idle(5);
        var lone = Assert.Single(g.Fragments);
        Assert.True(lone.Lone);
        double d0 = Field.Distance(lone.X, lone.Y, g.Ship.X, g.Ship.Y);
        g.Idle(30);
        Assert.True(Field.Distance(lone.X, lone.Y, g.Ship.X, g.Ship.Y) < d0 - 50);
    }

    [Fact]
    public void Raiders_hunt_the_ship_and_fire()
    {
        var g = Game();
        g.Calm = false;
        g.SetShip(200, 400);
        var r = g.PutRaider(800, 400);
        double d0 = Field.Distance(r.X, r.Y, g.Ship.X, g.Ship.Y);
        bool fired = false;
        for (int i = 0; i < 60 * 3 && g.Ship.Alive; i++)
        {
            g.Tick(default);
            if (g.Shots.Any(s => !s.Mine)) fired = true;
        }
        Assert.True(fired);
        Assert.True(Field.Distance(r.X, r.Y, g.Ship.X, g.Ship.Y) < d0);
    }

    [Fact]
    public void From_wave_3_two_Raiders_that_meet_merge_into_a_Marauder_that_takes_three_hits()
    {
        var g = Game(3);
        g.Calm = false;
        g.SetShip(100, 150);
        g.PutRaider(600, 500);
        g.PutRaider(620, 500);
        g.Tick(default);
        var m = Assert.Single(g.Raiders);
        Assert.True(m.Marauder);
        g.Calm = true;
        for (int i = 0; i < 2; i++) { g.PutShot(m.X, m.Y + 40, 0, -900, mine: true); g.Idle(4); }
        Assert.False(m.Dead);
        g.PutShot(m.X, m.Y + 40, 0, -900, mine: true);
        g.Idle(4);
        Assert.Empty(g.Raiders);
        Assert.Equal(GameEngine.MarauderPoints, g.Score);
    }

    [Fact]
    public void Raiders_dont_merge_before_wave_3()
    {
        var g = Game(2);
        g.Calm = false;
        g.SetShip(100, 150);
        g.PutRaider(600, 500);
        g.PutRaider(620, 500);
        g.Tick(default);
        Assert.Equal(2, g.Raiders.Count);
    }

    // ───────────────────────────── losing ships ─────────────────────────────

    [Fact]
    public void A_shot_or_a_rammer_destroys_the_ship_and_its_module()
    {
        var g = Game();
        g.SetModule(Module.Spread);
        int lives = g.Lives;
        g.PutShot(g.Ship.X, g.Ship.Y - 60, 0, 300);
        g.Idle(20);
        Assert.False(g.Ship.Alive);
        Assert.Equal("SHOT", g.Cause);
        Assert.Equal(lives - 1, g.Lives);
        Assert.Equal(Module.None, g.Ship.Module);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.True(g.Ship.Alive);
        Assert.True(g.Ship.Safe > 0);
        var r = g.PutRaider(g.Ship.X, g.Ship.Y);
        g.Tick(default);
        Assert.True(g.Ship.Alive, "safe for a moment after coming back");
        g.Idle((int)(GameEngine.SafeSeconds * 60));
        g.Raiders.Clear();
        g.PutRaider(g.Ship.X, g.Ship.Y);
        g.Tick(default);
        Assert.Equal("RAMMED", g.Cause);
        Assert.NotNull(r);
    }

    [Fact]
    public void The_game_ends_with_the_last_ship_and_a_good_score_gets_initials()
    {
        var g = Game();
        g.SetLives(1);
        g.SetScore(500000);
        g.PutShot(g.Ship.X, g.Ship.Y - 30, 0, 300);
        g.Idle(10);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Idle(4 * 60);
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    // ───────────────────────────── waves and docking ─────────────────────────────

    [Fact]
    public void A_wave_opens_with_the_Commanders_taunt()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        Assert.Equal(Phase.Start, g.Phase);
        Assert.Contains("SURRENDER", g.Taunt);
        g.Idle((int)(GameEngine.TauntSeconds * 60) + 2);
        Assert.Equal(Phase.Play, g.Phase);
    }

    [Fact]
    public void A_beaten_wave_brings_three_different_modules_to_dock_with()
    {
        var g = Game();
        g.Calm = false;
        g.Tick(default);
        Assert.Equal(Phase.Dock, g.Phase);
        Assert.Equal(3, g.Pods.Count);
        Assert.Equal(3, g.Pods.Select(p => p.Kind).Distinct().Count());
    }

    [Fact]
    public void Docking_fits_the_module_scores_a_bonus_and_starts_the_next_wave()
    {
        var g = Game();
        g.Calm = false;
        g.Tick(default);
        var pod = g.Pods[0];
        g.SetShip(pod.X, pod.Y);
        g.Tick(default);
        Assert.Equal(pod.Kind, g.Ship.Module);
        Assert.InRange(g.Score, 1000, 2000);
        Assert.Equal(2, g.Wave);
        Assert.Equal(Phase.Start, g.Phase);
    }

    [Fact]
    public void Missing_the_docking_just_brings_the_next_wave()
    {
        var g = Game();
        g.Calm = false;
        g.SetShip(Field.CentreX, Field.CentreY);
        g.Tick(default);
        g.Idle((int)(GameEngine.DockSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal(Module.None, g.Ship.Module);
    }

    [Fact]
    public void Later_waves_send_more_and_quicker()
    {
        var a = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        a.Go(1);
        var b = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        b.Go(6);
        Assert.True(b.ToCome > a.ToCome);
        Assert.True(b.Pace > a.Pace);
    }

    [Fact]
    public void An_extra_ship_at_20000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(19950);
        g.SetShip(100, 150);
        var three = g.PutTriad(700, 500);
        g.PutShot(three[0].X, three[0].Y + 30, 0, -600, mine: true);
        g.Idle(5);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the machine ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_pilot_beats_the_first_waves_and_docks()
    {
        var g = Real();
        bool docked = false;
        for (int i = 0; i < 60 * 120 && g.Wave < 4; i++)
        {
            g.Tick(g.Pilot.Decide(g));
            if (g.Ship.Module != Module.None) docked = true;
        }
        Assert.True(g.Wave >= 4, $"reached wave {g.Wave}");
        Assert.True(docked);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            return $"{g.Score} {g.Wave} {g.Lives} {g.Ship.X:0.00} {g.Ship.Y:0.00} {g.Remaining}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real(2);
        g.Run(30, new Controls(Dy: -1));
        double x = g.Ship.X, y = g.Ship.Y;
        g.Pause();
        g.Idle(120);
        Assert.Equal((x, y), (g.Ship.X, g.Ship.Y));
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
