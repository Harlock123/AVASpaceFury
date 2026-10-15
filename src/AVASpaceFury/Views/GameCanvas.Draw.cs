using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASpaceFury.Engine;
using Effect = AVASpaceFury.Engine.Effect;

namespace AVASpaceFury.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASpaceFury/Assets/Fonts#VT323"));

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

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

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


    private const uint ShipCyan = 0xFF6AE8FF, FragGreen = 0xFF6AFFB0, RaiderOrange = 0xFFFFA040, MarauderPink = 0xFFFF5AD8, CommanderGreen = 0xFF8AFF6A;

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    /// <summary>A line as a vector display draws it: a soft glow under a bright core.</summary>
    private static void Glow(DrawingContext dc, Point a, Point b, uint colour, double alpha = 1, double width = 2)
    {
        if (alpha <= 0.01) return;
        dc.DrawLine(Pen(Alpha(colour, 0.22 * alpha), width * 3.2), a, b);
        dc.DrawLine(Pen(Alpha(colour, alpha), width), a, b);
    }

    /// <summary>A closed outline of points given about (0, 0), turned and placed.</summary>
    private static void Shape(DrawingContext dc, double x, double y, double angle, double k, uint colour, double alpha, params (double X, double Y)[] pts)
    {
        double c = Math.Cos(angle), s = Math.Sin(angle);
        Point At((double X, double Y) p) => new(x + (p.X * c - p.Y * s) * k, y + (p.X * s + p.Y * c) * k);
        for (int i = 0; i < pts.Length; i++) Glow(dc, At(pts[i]), At(pts[(i + 1) % pts.Length]), colour, alpha);
    }

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000004), new Rect(0, 0, Field.Width, Field.Height));
        DrawStars(dc);
        if (InGame)
        {
            foreach (var p in g.Pods) DrawPod(dc, p);
            foreach (var f in g.Fragments) DrawFragment(dc, f.X, f.Y, f.Angle, f.Piece, f.Lone, 1.25);
            foreach (var r in g.Raiders) DrawRaider(dc, r.X, r.Y, r.Angle, r.Marauder, r.Hurt > 0, 1.25);
            foreach (var d in g.Wreckage)
            {
                double a = Math.Clamp(1 - d.Age / d.Life, 0, 1), c = Math.Cos(d.Angle) * d.Length / 2, s = Math.Sin(d.Angle) * d.Length / 2;
                Glow(dc, new Point(d.X - c, d.Y - s), new Point(d.X + c, d.Y + s), d.Colour, a, 1.6);
            }
            foreach (var s in g.Shots)
            {
                if (s.Mine) Glow(dc, new Point(s.X, s.Y), new Point(s.X - s.Vx * 0.012, s.Y - s.Vy * 0.012), 0xFFFFF4C0, 1, 2.4);
                else Shape(dc, s.X, s.Y, _clock * 8, 1, 0xFFFF4A4A, 1, (0, -5), (4, 0), (0, 5), (-4, 0));
            }
            var ship = g.Ship;
            if (ship.Alive && (ship.Safe <= 0 || (int)(_clock * 10) % 2 == 0)) DrawShip(dc, ship.X, ship.Y, ship.Angle, ship.Module, ship.Thrusting, 1);
            if (g.Phase == Phase.Start && g.State != GameState.Title) DrawCommanderSpeech(dc);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
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
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Height - 18), 26, Yellow, center: true);
    }

    private void DrawStars(DrawingContext dc)
    {
        for (int k = 0; k < 110; k++)
        {
            double x = Hash(k, 1, 8) % 1024, y = Field.Top + Hash(k, 2, 8) % (uint)Field.PlayHeight;
            double tw = 0.5 + 0.5 * Math.Sin(_clock * (0.7 + k % 5 * 0.4) + k);
            uint col = (k % 7) switch { 0 => 0xFFFFC0E0, 1 => 0xFFA0E0FF, _ => 0xFFFFFFFF };
            dc.FillRectangle(Brush(Alpha(col, 0.15 + 0.5 * tw)), new Rect(x, y, 2, 2));
        }
    }

    // ───────────────────────────── the ships ─────────────────────────────

    /// <summary>The player's ship: a notched arrowhead, with whatever module is docked built on to it.</summary>
    private void DrawShip(DrawingContext dc, double x, double y, double angle, Module module, bool thrusting, double k)
    {
        Shape(dc, x, y, angle, k, ShipCyan, 1, (0, -17), (11, 11), (4, 6), (-4, 6), (-11, 11));
        Shape(dc, x, y, angle, k, ShipCyan, 0.6, (0, -8), (4, 2), (-4, 2));
        switch (module)
        {
            case Module.Twin:
                Shape(dc, x, y, angle, k, Yellow, 1, (-11, -10), (-8, -10), (-8, 6), (-11, 6));
                Shape(dc, x, y, angle, k, Yellow, 1, (8, -10), (11, -10), (11, 6), (8, 6));
                break;
            case Module.Spread:
                Shape(dc, x, y, angle, k, Yellow, 1, (-11, 11), (-20, 2), (-14, 0), (-6, 4));
                Shape(dc, x, y, angle, k, Yellow, 1, (11, 11), (20, 2), (14, 0), (6, 4));
                break;
            case Module.Tail:
                Shape(dc, x, y, angle, k, Yellow, 1, (-3, 6), (3, 6), (3, 18), (-3, 18));
                break;
            case Module.Rapid:
                Shape(dc, x, y, angle, k, Yellow, 1, (0, -24), (5, -18), (0, -12), (-5, -18));
                break;
        }
        if (thrusting)
        {
            double f = 10 + 6 * Math.Sin(_clock * 40);
            Shape(dc, x, y, angle, k, 0xFFFF8A3A, 1, (-4, 8), (0, 8 + f), (4, 8));
        }
    }

    /// <summary>One of the three pieces that make a Raider: its nose (0) or one of its wings (1, 2). A lone one glows red.</summary>
    private void DrawFragment(DrawingContext dc, double x, double y, double angle, int piece, bool lone, double k)
    {
        uint col = lone ? ((int)(_clock * 8) % 2 == 0 ? 0xFFFF4A4A : FragGreen) : FragGreen;
        switch (piece)
        {
            case 0: Shape(dc, x, y, angle, k, col, 1, (0, -10), (7, 4), (0, 1), (-7, 4)); break;
            case 1: Shape(dc, x, y, angle, k, col, 1, (-9, -6), (3, 0), (-3, 8), (-11, 6)); break;
            default: Shape(dc, x, y, angle, k, col, 1, (9, -6), (-3, 0), (3, 8), (11, 6)); break;
        }
    }

    /// <summary>A Raider, the three pieces locked together; a Marauder is two of them, bigger, with a glowing core.</summary>
    private void DrawRaider(DrawingContext dc, double x, double y, double angle, bool marauder, bool hurt, double k)
    {
        uint col = hurt ? 0xFFFFFFFF : marauder ? MarauderPink : RaiderOrange;
        if (!marauder)
        {
            Shape(dc, x, y, angle, k, col, 1, (0, -15), (7, -3), (16, 4), (8, 12), (0, 6), (-8, 12), (-16, 4), (-7, -3));
            Shape(dc, x, y, angle, k, col, 0.7, (0, -7), (4, 1), (-4, 1));
            return;
        }
        Shape(dc, x, y, angle, k, col, 1, (0, -26), (10, -10), (26, -4), (20, 14), (8, 10), (0, 20), (-8, 10), (-20, 14), (-26, -4), (-10, -10));
        Shape(dc, x, y, angle, k, col, 0.8, (0, -14), (8, 0), (0, 10), (-8, 0));
        double p = 3 + 2 * Math.Sin(_clock * 9);
        dc.DrawEllipse(Brush(Alpha(col, 0.7)), null, new Point(x, y), p * k, p * k);
    }

    /// <summary>A module floating in to be docked with: a spinning hexagon with its emblem and its name.</summary>
    private void DrawPod(DrawingContext dc, Pod p)
    {
        var hex = Enumerable.Range(0, 6).Select(i => (Math.Sin(i * Math.PI / 3) * 22, -Math.Cos(i * Math.PI / 3) * 22)).ToArray();
        Shape(dc, p.X, p.Y, p.Angle, 1, Yellow, 1, hex);
        switch (p.Kind)
        {
            case Module.Twin:
                Glow(dc, new Point(p.X - 5, p.Y - 9), new Point(p.X - 5, p.Y + 9), White);
                Glow(dc, new Point(p.X + 5, p.Y - 9), new Point(p.X + 5, p.Y + 9), White);
                break;
            case Module.Spread:
                for (int i = -1; i <= 1; i++) Glow(dc, new Point(p.X, p.Y + 8), new Point(p.X + i * 8, p.Y - 9), White);
                break;
            case Module.Tail:
                Glow(dc, new Point(p.X, p.Y - 10), new Point(p.X, p.Y + 10), White);
                Shape(dc, p.X, p.Y - 7, 0, 1, White, 1, (-4, 2), (0, -3), (4, 2));
                Shape(dc, p.X, p.Y + 7, Math.PI, 1, White, 1, (-4, 2), (0, -3), (4, 2));
                break;
            default:
                for (int i = 0; i < 3; i++) Shape(dc, p.X, p.Y - 8 + i * 7, 0, 1, White, 1, (-5, 3), (0, -2), (5, 3));
                break;
        }
        Text(dc, GameEngine.NameOf(p.Kind), new Point(p.X, p.Y + 36), 22, Yellow, center: true);
    }

    /// <summary>The Commander: a great one-eyed head with a ridged crown and twitching feelers, its eye following the ship.</summary>
    private void DrawCommander(DrawingContext dc, Point at, double k, bool talking)
    {
        var g = Engine;
        uint col = CommanderGreen;
        // The head.
        var head = Enumerable.Range(0, 16).Select(i =>
        {
            double a = i * Math.PI / 8;
            return (Math.Sin(a) * 60 * (1 + 0.08 * Math.Cos(2 * a)), -Math.Cos(a) * 70);
        }).ToArray();
        Shape(dc, at.X, at.Y, 0, k, col, 1, head);
        // The crown of ridges.
        for (int i = -2; i <= 2; i++)
            Shape(dc, at.X + i * 18 * k, at.Y - 62 * k, 0, k, col, 0.8, (-6, 6), (0, -14 - Math.Abs(i) * -3), (6, 6));
        // The eye, watching the ship.
        double look = Math.Atan2(g.Ship.Y - at.Y, g.Ship.X - at.X);
        dc.DrawEllipse(null, Pen(Alpha(White, 0.9), 2.4), new Point(at.X, at.Y - 12 * k), 30 * k, 20 * k);
        dc.DrawEllipse(Brush(Alpha(Red, 0.9)), null, new Point(at.X + Math.Cos(look) * 12 * k, at.Y - 12 * k + Math.Sin(look) * 7 * k), 9 * k, 9 * k);
        dc.DrawEllipse(Brush(0xFF000000), null, new Point(at.X + Math.Cos(look) * 13 * k, at.Y - 12 * k + Math.Sin(look) * 8 * k), 4 * k, 4 * k);
        // The mouth, working as it speaks.
        double open = talking ? 6 + 6 * Math.Abs(Math.Sin(_clock * 16)) : 3;
        Shape(dc, at.X, at.Y + 30 * k, 0, k, col, 1, (-22, 0), (-8, -3), (8, -3), (22, 0), (8, open), (-8, open));
        // Feelers.
        for (int s = -1; s <= 1; s += 2)
        {
            double w = Math.Sin(_clock * 3 + s) * 10;
            Glow(dc, new Point(at.X + s * 44 * k, at.Y + 40 * k), new Point(at.X + s * (60 + w) * k, at.Y + 80 * k), col, 0.9);
            Glow(dc, new Point(at.X + s * (60 + w) * k, at.Y + 80 * k), new Point(at.X + s * (52 - w) * k, at.Y + 104 * k), col, 0.9);
        }
    }

    private void DrawCommanderSpeech(DrawingContext dc)
    {
        var g = Engine;
        DrawCommander(dc, new Point(512, 290), 1.2, true);
        int shown = Math.Min(g.Taunt.Length, (int)(g.PhaseTime * 30) + 1);
        Text(dc, g.Taunt[..shown], new Point(512, 470), 34, CommanderGreen, center: true);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 40 * t) * e.Size;
                for (int k = 0; k < 12; k++)
                {
                    double a = k * Math.PI / 6 + e.X;
                    Glow(dc, new Point(e.X + Math.Cos(a) * r * 0.5, e.Y + Math.Sin(a) * r * 0.5), new Point(e.X + Math.Cos(a) * r, e.Y + Math.Sin(a) * r), e.Colour, 1 - t, 1.5);
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 24), 26, Alpha(e.Colour, 1 - t * t), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 30), 32, Alpha(e.Colour, 1 - t * t), center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(1008, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(1008, 48), 30, Dim, right: true);
        if (g.State is not (GameState.Playing or GameState.Attract or GameState.Paused)) return;
        Text(dc, $"WAVE {g.Wave}", new Point(512, 24), 34, White, center: true);
        string sub = g.Phase == Phase.Dock ? $"DOCK IN {g.DockLeft:0}" : g.Ship.Module != Module.None ? GameEngine.NameOf(g.Ship.Module) : "";
        Text(dc, sub, new Point(512, 56), 24, g.Phase == Phase.Dock ? Yellow : 0xFFFFD84A, center: true);
        int spare = Math.Max(0, g.Lives - (g.Ship.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawShip(dc, 210 + i * 30, 40, 0, Module.None, false, 0.8);
        dc.DrawLine(Pen(0xFF203048, 1.5), new Point(0, Field.Top - 2), new Point(1024, Field.Top - 2));
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg || g.Phase == Phase.Start && g.Message.StartsWith("WAVE")) return;
        double cx = 512, cy = 170;
        var (w, h) = (Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xA0000010), new Rect(cx - w / 2, cy - 48, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 12 ? 46 : 58, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = 512;
        Text(dc, "AVA SPACE FURY", new Point(cx, 96), 104, MarauderPink, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 164), 28, Dim, center: true);
        DrawCommander(dc, new Point(cx, 280), 0.95, (int)(_clock / 2) % 2 == 0);
        Text(dc, "\"SURRENDER, EARTHLING. MY FLEET ASSEMBLES.\"", new Point(cx, 400), 28, CommanderGreen, center: true);
        // Three pieces, a Raider, a Marauder.
        double y = 470;
        for (int i = 0; i < 3; i++) DrawFragment(dc, cx - 360 + i * 30, y, _clock + i, i, false, 1.3);
        Glow(dc, new Point(cx - 260, y), new Point(cx - 220, y), Dim, 0.8);
        DrawRaider(dc, cx - 170, y, 0, false, false, 1.3);
        Glow(dc, new Point(cx - 120, y), new Point(cx - 80, y), Dim, 0.8);
        DrawRaider(dc, cx - 20, y, 0, true, false, 1.0);
        Text(dc, $"PIECES {GameEngine.FragmentPoints}", new Point(cx - 330, y + 40), 22, FragGreen, center: true);
        Text(dc, $"RAIDER {GameEngine.RaiderPoints}", new Point(cx - 170, y + 40), 22, RaiderOrange, center: true);
        Text(dc, $"MARAUDER {GameEngine.MarauderPoints}", new Point(cx - 20, y + 40), 22, MarauderPink, center: true);
        DrawShip(dc, cx + 170, y, 0, Module.Spread, false, 1.3);
        DrawShip(dc, cx + 260, y, 0, Module.Twin, false, 1.3);
        DrawShip(dc, cx + 350, y, 0, Module.Tail, false, 1.3);
        Text(dc, "DOCK WITH MODULES", new Point(cx + 260, y + 40), 22, Yellow, center: true);
        Text(dc, "SHOOT THE PIECES BEFORE THEY BUILD A RAIDER - TWO RAIDERS MAKE A MARAUDER", new Point(cx, 556), 24, White, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 620), 46, Yellow, center: true);
        Text(dc, "LEFT/RIGHT: TURN    UP: THRUST    SPACE: FIRE    P: PAUSE", new Point(cx, 680), 25, Dim, center: true);
        Text(dc, $"EXTRA SHIP AT {GameEngine.FirstExtraLife} AND EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 714), 23, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE DEFENDERS", new Point(cx, 130), 72, MarauderPink, center: true);
        Text(dc, "RANK NAME    SCORE   WAVE", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
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
