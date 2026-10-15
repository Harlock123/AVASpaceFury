namespace AVASpaceFury.Engine;

/// <summary>Rotate left/right (Dx), thrust (Dy -1), and fire.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>Space: the whole screen below the score bar, wrapping at every edge.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Top = 70;
    public const double PlayHeight = Height - Top;
    public const double CentreX = Width / 2, CentreY = Top + PlayHeight / 2;

    public static double WrapX(double x) => ((x % Width) + Width) % Width;
    public static double WrapY(double y) => (((y - Top) % PlayHeight) + PlayHeight) % PlayHeight + Top;

    /// <summary>The shortest offset from one point to another, round the wrap if that's nearer.</summary>
    public static (double X, double Y) Delta(double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0;
        if (dx > Width / 2) dx -= Width; else if (dx < -Width / 2) dx += Width;
        if (dy > PlayHeight / 2) dy -= PlayHeight; else if (dy < -PlayHeight / 2) dy += PlayHeight;
        return (dx, dy);
    }

    public static double Distance(double x0, double y0, double x1, double y1)
    {
        var (dx, dy) = Delta(x0, y0, x1, y1);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>The heading from one point toward another: 0 up, growing clockwise.</summary>
    public static double Bearing(double x0, double y0, double x1, double y1)
    {
        var (dx, dy) = Delta(x0, y0, x1, y1);
        return Math.Atan2(dx, -dy);
    }

    public static double AngleDiff(double target, double current) => Math.IEEERemainder(target - current, Math.PI * 2);
}

/// <summary>What a docked module adds to the ship's guns.</summary>
public enum Module
{
    None,
    /// <summary>Two cannons side by side.</summary>
    Twin,
    /// <summary>A fan of three.</summary>
    Spread,
    /// <summary>A gun firing backwards too.</summary>
    Tail,
    /// <summary>Quicker fire, and more shots in the air.</summary>
    Rapid,
}

public sealed class Ship
{
    public const double Radius = 13;
    public double X = Field.CentreX, Y = Field.CentreY, Vx, Vy, Angle;
    public bool Alive = true, Thrusting;
    public Module Module;
    public double FireCool, Safe;
}

public sealed class Shot
{
    public double X, Y, Vx, Vy, Life;
    public bool Mine, Dead;
}

/// <summary>One of the three pieces that fly together to make a Raider.</summary>
public sealed class Fragment
{
    public double X, Y, Vx, Vy, Angle, Spin;
    /// <summary>Which three it belongs with, and which of the three it is.</summary>
    public int Triad, Piece;
    /// <summary>Where the three are to meet.</summary>
    public double MeetX, MeetY;
    /// <summary>The others were shot: it comes straight for the ship instead.</summary>
    public bool Lone, Dead;
}

/// <summary>An assembled enemy ship: it hunts the player and fires. Two can merge into a Marauder.</summary>
public sealed class Raider
{
    public double X, Y, Vx, Vy, Angle, T, FireIn = 1.5, Hurt;
    public int Hp = 1;
    public bool Marauder, Dead;
    public double Radius => Marauder ? 30 : 19;
}

/// <summary>An add-on module floating in to be docked with, between waves.</summary>
public sealed class Pod
{
    public Module Kind;
    public double X, Y, Angle, Orbit;
}

/// <summary>A line of something destroyed, spinning away.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Angle, Spin, Length, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
