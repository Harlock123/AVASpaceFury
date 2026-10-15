namespace AVASpaceFury.Engine;

public sealed partial class GameEngine
{
    public const double RotateSpeed = 4.2, Thrust = 360, MaxSpeed = 430, Drag = 0.35;
    public const double ShotSpeed = 680, ShotLife = 0.85, FireRepeat = 0.2, RapidRepeat = 0.1, EnemyShotSpeed = 300;
    public const double TauntSeconds = 3.0, DyingSeconds = 2.0, SafeSeconds = 2.5, DockSeconds = 10;
    public const int FirstExtraLife = 20000, ExtraLifeEvery = 50000;
    public const int FragmentPoints = 100, LonePoints = 150, RaiderPoints = 300, MarauderPoints = 800;

    public static readonly string[] Taunts =
    [
        "SURRENDER, EARTHLING. MY FLEET ASSEMBLES.",
        "YOU CANNOT BREAK WHAT REBUILDS ITSELF.",
        "MY RAIDERS HUNGER FOR YOUR SHIP.",
        "TWO RAIDERS ARE STRONGER THAN ONE.",
        "YOUR MODULES WILL NOT SAVE YOU.",
        "FOOLISH HUMAN. THIS SECTOR IS MINE.",
        "I HAVE ENDLESS SHIPS. YOU HAVE FEW.",
        "STILL ALIVE? NOT FOR LONG.",
    ];

    public static string NameOf(Module m) => m switch
    {
        Module.Twin => "TWIN CANNONS", Module.Spread => "SPREAD SHOT", Module.Tail => "TAIL GUN", Module.Rapid => "RAPID FIRE", _ => "",
    };

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Ship Ship { get; } = new();
    public List<Shot> Shots { get; } = [];
    public List<Fragment> Fragments { get; } = [];
    public List<Raider> Raiders { get; } = [];
    public List<Pod> Pods { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    public double WaveTime { get; private set; }
    /// <summary>What the Commander says before the wave.</summary>
    public string Taunt { get; private set; } = "";
    /// <summary>How many more threes the wave has still to send.</summary>
    public int ToCome { get; private set; }
    /// <summary>For tests: nothing comes, merges or fires by itself.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _triad;
    private double _spawnIn, _thrustSound;
    private bool _firePrev;

    public double Pace => Math.Min(1.8, 1 + 0.07 * (Wave - 1));
    public int Remaining => Fragments.Count(f => !f.Dead) + Raiders.Count(r => !r.Dead);
    public double DockLeft => Phase == Phase.Dock ? Math.Max(0, DockSeconds - PhaseTime) : 0;
    private int MaxEnemyShots => Math.Min(10, 3 + Wave);

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        ResetShip();
        Ship.Module = Module.None;
        Clear();
        SetPhase(Phase.Start);
    }

    private void Clear()
    {
        Shots.Clear();
        Fragments.Clear();
        Raiders.Clear();
        Pods.Clear();
    }

    /// <summary>The Commander's word, then the wave: threes of fragments coming in from the edges.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        WaveTime = 0;
        Clear();
        ToCome = Math.Min(14, 4 + wave);
        _spawnIn = 0.5;
        Taunt = Taunts[(wave - 1) % Taunts.Length];
        SetPhase(Phase.Start);
        Say($"WAVE {wave}", null, TauntSeconds);
        Play(Sfx.Taunt);
    }

    private void ResetShip()
    {
        var s = Ship;
        s.X = Field.CentreX;
        s.Y = Field.CentreY;
        s.Vx = s.Vy = s.Angle = 0;
        s.Alive = true;
        s.FireCool = 0;
        s.Safe = SafeSeconds;
    }

    // ───────────────────────────── a step of play ─────────────────────────────

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        switch (Phase)
        {
            case Phase.Start:
                FlyShip(c, dt, canFire: false);
                MoveShots(dt);
                if (PhaseTime > TauntSeconds)
                {
                    SetPhase(Phase.Play);
                    Play(Sfx.WaveStart);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Play:
                WaveTime += dt;
                FlyShip(c, dt, canFire: true);
                MoveShots(dt);
                Spawn(dt);
                MoveFragments(dt);
                MoveRaiders(dt);
                Hits();
                if (Phase == Phase.Play) Progress();
                break;
            case Phase.Dying:
                MoveShots(dt);
                MoveFragments(dt);
                MoveRaiders(dt);
                if (PhaseTime > DyingSeconds)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    ResetShip();
                    SetPhase(Phase.Play);
                    if (State == GameState.Playing) Music(true);
                }
                break;
            case Phase.Dock:
                FlyShip(c, dt, canFire: false);
                MoveShots(dt);
                MovePods(dt);
                if (Phase == Phase.Dock && PhaseTime > DockSeconds) BeginWave(Wave + 1);
                break;
        }
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
            d.Angle += d.Spin * dt;
        }
        Wreckage.RemoveAll(d => d.Age > d.Life);
    }

    // ───────────────────────────── the ship ─────────────────────────────

    private void FlyShip(Controls c, double dt, bool canFire)
    {
        var s = Ship;
        bool fresh = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        if (!s.Alive) return;
        s.Safe = Math.Max(0, s.Safe - dt);
        s.Angle += Math.Sign(c.Dx) * RotateSpeed * dt;
        s.Thrusting = c.Dy < 0;
        if (s.Thrusting)
        {
            s.Vx += Math.Sin(s.Angle) * Thrust * dt;
            s.Vy -= Math.Cos(s.Angle) * Thrust * dt;
            _thrustSound -= dt;
            if (_thrustSound <= 0) { _thrustSound = 0.25; Play(Sfx.Thrust); }
        }
        double k = Math.Max(0, 1 - Drag * dt);
        s.Vx *= k;
        s.Vy *= k;
        double v = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);
        if (v > MaxSpeed) { s.Vx *= MaxSpeed / v; s.Vy *= MaxSpeed / v; }
        s.X = Field.WrapX(s.X + s.Vx * dt);
        s.Y = Field.WrapY(s.Y + s.Vy * dt);
        // Docking, by flying into a module.
        if (Phase == Phase.Dock)
            foreach (var p in Pods)
                if (Field.Distance(s.X, s.Y, p.X, p.Y) < 28) { Dock(p); break; }
        s.FireCool -= dt;
        double repeat = s.Module == Module.Rapid ? RapidRepeat : FireRepeat;
        int perVolley = s.Module switch { Module.Twin or Module.Tail => 2, Module.Spread => 3, _ => 1 };
        int limit = (s.Module == Module.Rapid ? 7 : 4) * perVolley;
        if (canFire && c.Fire && (fresh || s.FireCool <= 0) && Shots.Count(x => x.Mine) + perVolley <= limit)
        {
            s.FireCool = repeat;
            Volley(s);
            Play(Sfx.Fire);
        }
    }

    /// <summary>One press of the trigger: a single shot, or two or three with a module.</summary>
    private void Volley(Ship s)
    {
        void Fire(double angle, double side)
        {
            double fx = Math.Sin(s.Angle), fy = -Math.Cos(s.Angle);
            // Side by side across the nose: the ship's right is (cos, sin).
            double x = s.X + fx * 16 + Math.Cos(s.Angle) * side, y = s.Y + fy * 16 + Math.Sin(s.Angle) * side;
            Shots.Add(new Shot { X = x, Y = y, Vx = Math.Sin(angle) * ShotSpeed + s.Vx, Vy = -Math.Cos(angle) * ShotSpeed + s.Vy, Life = ShotLife, Mine = true });
        }
        switch (s.Module)
        {
            case Module.Twin: Fire(s.Angle, -7); Fire(s.Angle, 7); break;
            case Module.Spread: Fire(s.Angle - 0.2, 0); Fire(s.Angle, 0); Fire(s.Angle + 0.2, 0); break;
            case Module.Tail: Fire(s.Angle, 0); Fire(s.Angle + Math.PI, 0); break;
            default: Fire(s.Angle, 0); break;
        }
    }

    private void MoveShots(double dt)
    {
        foreach (var s in Shots)
        {
            s.X = Field.WrapX(s.X + s.Vx * dt);
            s.Y = Field.WrapY(s.Y + s.Vy * dt);
            s.Life -= dt;
            if (s.Life <= 0) s.Dead = true;
        }
        Shots.RemoveAll(s => s.Dead);
    }

    // ───────────────────────────── the enemy ─────────────────────────────

    /// <summary>Another three fragments, in from the edges towards a meeting point.</summary>
    private void Spawn(double dt)
    {
        if (Calm || ToCome <= 0) return;
        _spawnIn -= dt;
        if (_spawnIn > 0) return;
        _spawnIn = Math.Max(0.8, 2.4 - 0.13 * Wave) * (0.7 + 0.6 * _rng.NextDouble());
        ToCome--;
        _triad++;
        // Meet somewhere not too near the ship.
        double mx, my;
        do
        {
            mx = 120 + _rng.NextDouble() * (Field.Width - 240);
            my = Field.Top + 100 + _rng.NextDouble() * (Field.PlayHeight - 200);
        } while (Field.Distance(mx, my, Ship.X, Ship.Y) < 220);
        for (int i = 0; i < 3; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2;
            double x = Field.WrapX(mx + Math.Sin(a) * 600), y = Field.WrapY(my - Math.Cos(a) * 600);
            Fragments.Add(new Fragment { X = x, Y = y, Triad = _triad, Piece = i, MeetX = mx, MeetY = my, Angle = a, Spin = (_rng.NextDouble() - 0.5) * 4 });
        }
    }

    private double FragmentSpeed => Math.Min(210, 100 + 8 * Wave);

    private void MoveFragments(double dt)
    {
        foreach (var f in Fragments)
        {
            if (f.Dead) continue;
            f.Angle += f.Spin * dt;
            double tx = f.Lone ? Ship.X : f.MeetX, ty = f.Lone ? Ship.Y : f.MeetY;
            var (dx, dy) = Field.Delta(f.X, f.Y, tx, ty);
            double d = Math.Sqrt(dx * dx + dy * dy);
            double speed = f.Lone ? FragmentSpeed * 1.35 : FragmentSpeed;
            if (!f.Lone && d < 3) { (f.Vx, f.Vy) = (0, 0); continue; }
            double step = Math.Min(d, speed * dt);
            (f.Vx, f.Vy) = (dx / Math.Max(d, 1e-6) * speed, dy / Math.Max(d, 1e-6) * speed);
            f.X = Field.WrapX(f.X + dx / Math.Max(d, 1e-6) * step);
            f.Y = Field.WrapY(f.Y + dy / Math.Max(d, 1e-6) * step);
        }
        // Threes (or twos) all at their meeting point assemble into a Raider.
        foreach (var g in Fragments.Where(f => !f.Dead && !f.Lone).GroupBy(f => f.Triad).ToList())
        {
            var alive = g.ToList();
            if (alive.Count < 2 || alive.Any(f => Field.Distance(f.X, f.Y, f.MeetX, f.MeetY) > 3)) continue;
            foreach (var f in alive) f.Dead = true;
            Raiders.Add(new Raider { X = alive[0].MeetX, Y = alive[0].MeetY, Angle = Field.Bearing(alive[0].MeetX, alive[0].MeetY, Ship.X, Ship.Y), FireIn = 1 + _rng.NextDouble() });
            Effects.Add(new Effect { Kind = EffectKind.Burst, X = alive[0].MeetX, Y = alive[0].MeetY, Life = 0.4, Size = 0.8, Colour = 0xFF6AFFE0 });
            Play(Sfx.Assemble);
        }
        Fragments.RemoveAll(f => f.Dead);
    }

    private double RaiderSpeed => Math.Min(250, 125 + 10 * Wave);

    /// <summary>Raiders hunt the ship and fire at it; from wave 3, two that meet merge into a Marauder.</summary>
    private void MoveRaiders(double dt)
    {
        var s = Ship;
        foreach (var r in Raiders)
        {
            if (r.Dead) continue;
            r.T += dt;
            r.Hurt = Math.Max(0, r.Hurt - dt);
            // Weave a little as they come, so they're not sitting ducks; from wave 3, a Raider with a
            // partner close by goes to join it instead.
            double want = Field.Bearing(r.X, r.Y, s.X, s.Y) + Math.Sin(r.T * 1.7 + r.X) * 0.5;
            if (!r.Marauder && Wave >= 3 && !Calm)
            {
                var mate = Raiders.Where(o => o != r && !o.Dead && !o.Marauder).MinBy(o => Field.Distance(r.X, r.Y, o.X, o.Y));
                if (mate != null && Field.Distance(r.X, r.Y, mate.X, mate.Y) < 280) want = Field.Bearing(r.X, r.Y, mate.X, mate.Y);
            }
            double turn = r.Marauder ? 1.1 : 1.7;
            r.Angle += Math.Clamp(Field.AngleDiff(want, r.Angle), -turn * dt, turn * dt);
            double speed = RaiderSpeed * (r.Marauder ? 0.75 : 1);
            (r.Vx, r.Vy) = (Math.Sin(r.Angle) * speed, -Math.Cos(r.Angle) * speed);
            r.X = Field.WrapX(r.X + r.Vx * dt);
            r.Y = Field.WrapY(r.Y + r.Vy * dt);
            if (Calm || Phase != Phase.Play || !s.Alive) continue;
            r.FireIn -= dt;
            if (r.FireIn > 0 || Shots.Count(x => !x.Mine) >= MaxEnemyShots) continue;
            r.FireIn = Math.Max(0.7, (r.Marauder ? 1.6 : 2.1) - 0.1 * Wave) * (0.7 + 0.6 * _rng.NextDouble());
            // Mostly leading the ship, to catch it where it's going.
            double lead = _rng.NextDouble() < 0.7 ? Field.Distance(r.X, r.Y, s.X, s.Y) / EnemyShotSpeed : 0;
            double aim = Field.Bearing(r.X, r.Y, s.X + s.Vx * lead, s.Y + s.Vy * lead);
            int n = r.Marauder ? 3 : 1;
            for (int i = 0; i < n; i++)
            {
                double a = aim + (i - (n - 1) / 2.0) * 0.25 + (_rng.NextDouble() - 0.5) * 0.1;
                Shots.Add(new Shot { X = r.X, Y = r.Y, Vx = Math.Sin(a) * EnemyShotSpeed, Vy = -Math.Cos(a) * EnemyShotSpeed, Life = 2.2 });
            }
            Play(Sfx.EnemyFire);
        }
        if (!Calm && Wave >= 3)
        {
            var plain = Raiders.Where(r => !r.Dead && !r.Marauder).ToList();
            for (int i = 0; i < plain.Count; i++)
                for (int j = i + 1; j < plain.Count; j++)
                {
                    var a = plain[i];
                    var b = plain[j];
                    if (a.Dead || b.Dead || Field.Distance(a.X, a.Y, b.X, b.Y) > 40) continue;
                    var (dx, dy) = Field.Delta(a.X, a.Y, b.X, b.Y);
                    b.Dead = true;
                    a.X = Field.WrapX(a.X + dx / 2);
                    a.Y = Field.WrapY(a.Y + dy / 2);
                    a.Marauder = true;
                    a.Hp = 3;
                    Effects.Add(new Effect { Kind = EffectKind.Burst, X = a.X, Y = a.Y, Life = 0.5, Size = 1.4, Colour = 0xFFFF6AE0 });
                    Play(Sfx.Merge);
                }
        }
        Raiders.RemoveAll(r => r.Dead);
    }

    private void Hits()
    {
        foreach (var shot in Shots)
        {
            if (!shot.Mine || shot.Dead) continue;
            foreach (var f in Fragments)
            {
                if (f.Dead || Field.Distance(shot.X, shot.Y, f.X, f.Y) > 15) continue;
                shot.Dead = true;
                KillFragment(f);
                break;
            }
            if (shot.Dead) continue;
            foreach (var r in Raiders)
            {
                if (r.Dead || Field.Distance(shot.X, shot.Y, r.X, r.Y) > r.Radius + 3) continue;
                shot.Dead = true;
                if (--r.Hp > 0)
                {
                    r.Hurt = 0.15;
                    Play(Sfx.MarauderHit);
                }
                else KillRaider(r);
                break;
            }
        }
        Fragments.RemoveAll(f => f.Dead);
        Raiders.RemoveAll(r => r.Dead);
        var s = Ship;
        if (!s.Alive || s.Safe > 0) return;
        foreach (var shot in Shots)
            if (!shot.Mine && !shot.Dead && Field.Distance(shot.X, shot.Y, s.X, s.Y) < 12) { shot.Dead = true; Die("SHOT"); return; }
        foreach (var f in Fragments)
            if (Field.Distance(f.X, f.Y, s.X, s.Y) < 12 + Ship.Radius) { KillFragment(f); Fragments.RemoveAll(x => x.Dead); Die("RAMMED"); return; }
        foreach (var r in Raiders)
            if (Field.Distance(r.X, r.Y, s.X, s.Y) < r.Radius + Ship.Radius - 2) { KillRaider(r); Raiders.RemoveAll(x => x.Dead); Die("RAMMED"); return; }
    }

    private void KillFragment(Fragment f)
    {
        f.Dead = true;
        int points = f.Lone ? LonePoints : FragmentPoints;
        AddScore(points);
        Shatter(f.X, f.Y, 3, 0xFF6AFFE0, 10);
        if (f.Lone) Effects.Add(new Effect { Kind = EffectKind.Score, X = f.X, Y = f.Y - 14, Text = points.ToString() });
        Play(Sfx.FragmentHit);
        // A last survivor of its three comes straight for the ship.
        var rest = Fragments.Where(o => !o.Dead && o.Triad == f.Triad && !o.Lone).ToList();
        if (rest.Count == 1)
        {
            rest[0].Lone = true;
            Play(Sfx.Lone);
        }
    }

    private void KillRaider(Raider r)
    {
        r.Dead = true;
        int points = r.Marauder ? MarauderPoints : RaiderPoints;
        AddScore(points);
        Shatter(r.X, r.Y, r.Marauder ? 10 : 6, r.Marauder ? 0xFFFF6AE0 : 0xFFFFB04A, r.Radius);
        Effects.Add(new Effect { Kind = EffectKind.Score, X = r.X, Y = r.Y - 18, Text = points.ToString(), Colour = r.Marauder ? 0xFFFFE04A : 0xFFFFFFFF });
        Play(Sfx.RaiderHit);
    }

    private void Shatter(double x, double y, int lines, uint colour, double size)
    {
        for (int i = 0; i < lines; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 110;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Sin(a) * v, Vy = Math.Cos(a) * v, Angle = _rng.NextDouble() * 6, Spin = (_rng.NextDouble() - 0.5) * 10, Length = size * (0.5 + _rng.NextDouble() * 0.6), Life = 0.8 + _rng.NextDouble() * 0.5, Colour = colour });
        }
    }

    private void Die(string cause)
    {
        Cause = cause;
        var s = Ship;
        s.Alive = false;
        Lives--;
        // The module goes with the ship.
        s.Module = Module.None;
        Shatter(s.X, s.Y, 12, 0xFFFFFFFF, 18);
        Effects.Add(new Effect { Kind = EffectKind.Burst, X = s.X, Y = s.Y, Life = 1, Size = 2.2, Colour = 0xFFFFE070 });
        Shots.RemoveAll(x => !x.Mine);
        SetPhase(Phase.Dying);
        Music(false);
        Play(Sfx.Death);
    }

    /// <summary>The wave's beaten once it has sent all its fragments and none of them, nor any Raider, is left.</summary>
    private void Progress()
    {
        if (Calm || ToCome > 0 || Remaining > 0) return;
        Shots.Clear();
        SetPhase(Phase.Dock);
        Music(false);
        // Three different modules, circling the middle.
        var kinds = new[] { Module.Twin, Module.Spread, Module.Tail, Module.Rapid }.OrderBy(_ => _rng.Next()).Take(3).ToList();
        for (int i = 0; i < 3; i++) Pods.Add(new Pod { Kind = kinds[i], Orbit = i * Math.PI * 2 / 3 + _rng.NextDouble() });
        MovePods(0);
        Say("WAVE BEATEN", "DOCK WITH A MODULE", 2.5);
        Play(Sfx.ModulesIn);
    }

    private void MovePods(double dt)
    {
        foreach (var p in Pods)
        {
            p.Orbit += 0.25 * dt;
            p.Angle += 1.2 * dt;
            p.X = Field.CentreX + Math.Sin(p.Orbit) * 240;
            p.Y = Field.CentreY - Math.Cos(p.Orbit) * 200;
        }
    }

    private void Dock(Pod p)
    {
        Ship.Module = p.Kind;
        int bonus = 1000 + 100 * (int)DockLeft;
        AddScore(bonus);
        Effects.Add(new Effect { Kind = EffectKind.Word, X = p.X, Y = p.Y - 30, Text = $"{NameOf(p.Kind)} +{bonus}", Life = 2.2, Colour = 0xFFFFE04A });
        Play(Sfx.Docked);
        BeginWave(Wave + 1);
    }

    private void AddScore(int points)
    {
        Score += points;
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into the play of a wave, past the taunt.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
        Ship.Safe = 0;
    }

    internal List<Fragment> PutTriad(double meetX, double meetY, double spread = 200)
    {
        _triad++;
        var list = new List<Fragment>();
        for (int i = 0; i < 3; i++)
        {
            double a = i * Math.PI * 2 / 3;
            var f = new Fragment { X = meetX + Math.Sin(a) * spread, Y = meetY - Math.Cos(a) * spread, Triad = _triad, Piece = i, MeetX = meetX, MeetY = meetY };
            Fragments.Add(f);
            list.Add(f);
        }
        return list;
    }

    internal Raider PutRaider(double x, double y, bool marauder = false)
    {
        var r = new Raider { X = x, Y = y, Marauder = marauder, Hp = marauder ? 3 : 1 };
        Raiders.Add(r);
        return r;
    }

    internal void PutShot(double x, double y, double vx, double vy, bool mine = false) => Shots.Add(new Shot { X = x, Y = y, Vx = vx, Vy = vy, Life = 3, Mine = mine });
    internal void SetToCome(int n) => ToCome = n;
    internal void SetShip(double x, double y, double angle = 0) { Ship.X = x; Ship.Y = y; Ship.Angle = angle; Ship.Vx = Ship.Vy = 0; }
    internal void SetModule(Module m) => Ship.Module = m;
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
