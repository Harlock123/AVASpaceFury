namespace AVASpaceFury.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). Between waves it flies to the nearest module
/// and docks. In a wave it thrusts away from anything about to hit it; otherwise it turns to lead the nearest
/// enemy (a lone fragment or a Raider closing in first) and fires when it's lined up, keeping its speed down.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    private int _tick;

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var s = g.Ship;
        if (!s.Alive) return default;
        double speed = Math.Sqrt(s.Vx * s.Vx + s.Vy * s.Vy);

        if (g.Phase == Phase.Dock)
        {
            var pod = g.Pods.MinBy(p => Field.Distance(s.X, s.Y, p.X, p.Y));
            if (pod == null) return default;
            double t = Field.Distance(s.X, s.Y, pod.X, pod.Y) / 260;
            double px = pod.X + Math.Cos(pod.Orbit) * 240 * 0.25 * t, py = pod.Y + Math.Sin(pod.Orbit) * 200 * 0.25 * t;
            Doing = "dock";
            return Steer(s, Field.Bearing(s.X, s.Y, px, py), thrust: speed < 260, fire: false);
        }

        // Anything about to run into us: get out of its way.
        double soonest = 99, awayFrom = 0;
        void Threat(double x, double y, double vx, double vy, double reach)
        {
            for (double t = 0; t <= 0.8; t += 0.05)
            {
                double tx = x + vx * t, ty = y + vy * t, sx = s.X + s.Vx * t, sy = s.Y + s.Vy * t;
                if (Field.Distance(tx, ty, sx, sy) < reach)
                {
                    if (t < soonest) { soonest = t; awayFrom = Field.Bearing(x, y, s.X, s.Y); }
                    return;
                }
            }
        }
        if (s.Safe <= 0.3)
        {
            foreach (var shot in g.Shots.Where(x => !x.Mine)) Threat(shot.X, shot.Y, shot.Vx, shot.Vy, 22);
            foreach (var f in g.Fragments) Threat(f.X, f.Y, f.Vx, f.Vy, 30);
            foreach (var r in g.Raiders) Threat(r.X, r.Y, r.Vx, r.Vy, r.Radius + 20);
        }
        if (soonest < 0.55)
        {
            // Across its path, whichever way round is quicker.
            double a1 = awayFrom + Math.PI / 2, a2 = awayFrom - Math.PI / 2;
            double away = Math.Abs(Field.AngleDiff(a1, s.Angle)) < Math.Abs(Field.AngleDiff(a2, s.Angle)) ? a1 : a2;
            Doing = "evade";
            return Steer(s, away, thrust: true, fire: true, loose: 0.9);
        }

        // The best target, led so the shot meets it.
        double best = double.MaxValue, aim = s.Angle;
        bool any = false, near = false;
        void Consider(double x, double y, double vx, double vy, double bias)
        {
            double d = Field.Distance(s.X, s.Y, x, y);
            double t = d / GameEngine.ShotSpeed;
            double ax = x + vx * t - s.Vx * t, ay = y + vy * t - s.Vy * t;
            double a = Field.Bearing(s.X, s.Y, ax, ay);
            double cost = d + Math.Abs(Field.AngleDiff(a, s.Angle)) * 120 - bias;
            if (cost < best) { best = cost; aim = a; any = true; near = d < 480; }
        }
        foreach (var f in g.Fragments) Consider(f.X, f.Y, f.Vx, f.Vy, f.Lone ? 250 : 0);
        foreach (var r in g.Raiders) Consider(r.X, r.Y, r.Vx, r.Vy, 150);
        if (!any)
        {
            Doing = "wait";
            return Steer(s, s.Angle, thrust: false, fire: false);
        }
        Doing = "attack";
        bool lined = Math.Abs(Field.AngleDiff(aim, s.Angle)) < 0.07 && near;
        // Creep closer if everything's far off, but never get going too fast.
        bool thrust = !near && speed < 120 && Math.Abs(Field.AngleDiff(aim, s.Angle)) < 0.3;
        return Steer(s, aim, thrust, fire: lined && _tick % 2 == 0);
    }

    private static Controls Steer(Ship s, double want, bool thrust, bool fire, double loose = 0.04)
    {
        double diff = Field.AngleDiff(want, s.Angle);
        int dx = Math.Abs(diff) > 0.03 ? Math.Sign(diff) : 0;
        return new Controls(dx, thrust && Math.Abs(diff) < loose + 0.4 ? -1 : 0, fire);
    }
}
