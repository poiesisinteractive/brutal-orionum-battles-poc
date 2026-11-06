using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Pilier Terrain (terrain.ts) : puits de gravité. Le champ, la balistique et la fronde arrivent en S5.</summary>
public static class Terrain
{
    public static float WellCore(in GravityWell g) => g.Core;

    public static float WellPull(in GravityWell g) => g.Pull;

    /// <summary>Rayon où l'attraction est mesurée : le cœur pour un vestige, l'œil pour une fronde.</summary>
    static float RefRadius(in GravityWell g) => g.Core > 0 ? g.Core : g.Eye;

    /// <summary>Accélération gravitationnelle en <paramref name="pos"/> (u/s²), sommée sur tous les puits.</summary>
    public static float3 FieldAt(ReadOnlySpan<GravityWell> wells, float3 pos, bool slings = true)
    {
        float ax = 0, ay = 0, az = 0;
        foreach (ref readonly var g in wells)
        {
            if (!slings && g.Eye > 0)
                continue;
            var dx = g.Pos.X - pos.X;
            var dy = g.Pos.Y - pos.Y;
            var dz = g.Pos.Z - pos.Z;
            var d2 = dx * dx + dy * dy + dz * dz;
            if (d2 >= g.Reach * g.Reach)
                continue;
            var d = MathF.Sqrt(d2);
            var core = RefRadius(g);
            var r = MathF.Max(d, core);
            var a = WellPull(g) * (core / r) * (core / r);
            var f0 = g.Reach * GameData.Terrain.Fade;
            if (d > f0)
                a *= 1 - (d - f0) / (g.Reach - f0);
            if (d < 1e-3f)
                continue;
            ax += dx / d * a;
            ay += dy / d * a;
            az += dz / d * a;
        }
        return new float3(ax, ay, az);
    }

    /// <summary>Variation de vitesse que ressent une coque pendant ce pas (les grosses résistent).</summary>
    public static float3 ShipGravity(World w, in Ship s, float dt)
    {
        if (w.Wells.Count == 0)
            return float3.Zero;
        var k = GameData.Terrain.ShipFactor(s.Class.Role) ?? 0;
        if (k == 0)
            return float3.Zero;
        return Vec.Scale(FieldAt(w.Wells.AsSpan(), s.Pos), k * dt);
    }

    /// <summary>
    /// Contournement des cœurs, minimal (il ne doit casser ni les formations ni les passes d'attaque) :
    /// une coque dans la marge est repoussée, et une course qui y entrerait avant de pouvoir s'arrêter
    /// (retournement + freinage) est infléchie juste assez pour passer à côté.
    /// </summary>
    public static float3? CorePush(World w, in Ship s, float3 desired)
    {
        if (w.Wells.Count == 0)
            return null;
        var cls = s.Class;
        var r = cls.Radius;
        float3? push = null;
        var horizon = Movement.FlipTime(s) + cls.Speed / MathF.Max(0.5f, cls.Accel) + 2;
        // Les grosses coques gardent plus de marge (elles réagissent lentement).
        var margin = GameData.Terrain.CoreMargin + r * 0.6f;
        // Jamais plus près que là où l'attraction bat la moitié des propulseurs de manœuvre.
        var k = GameData.Terrain.ShipFactor(cls.Role) ?? 0;
        var rcs = MathF.Max(0.05f, cls.Accel * cls.Rcs) * 0.5f;
        var pos = s.Pos;
        var vel = s.Vel;

        void Course(float3 v, float gain, in GravityWell g, float edge, ref float3? push)
        {
            var speed = Vec.Len(v);
            if (speed < 1)
                return;
            var dir = Vec.Scale(v, 1 / speed);
            var along = Vec.Dot(g.Pos - pos, dir);
            if (along < 0 || along > speed * horizon + edge)
                return;
            var closest = pos + Vec.Scale(dir, along);
            var miss = Vec.Dist(closest, g.Pos);
            if (miss >= edge)
                return;
            var side = closest - g.Pos;
            if (Vec.LenSq(side) < 1)
                side = Vec.Cross(dir, Vec.Up);
            if (Vec.LenSq(side) < 1e-6f)
                side = new float3(1, 0, 0);
            var kk = (edge - miss) / edge;
            // À côté du cœur, et (pour la dérive) aussi contre le mouvement actuel.
            var add = Vec.Scale(Vec.Normalize(side), MathF.Max(speed, cls.Speed * 0.5f) * kk * gain);
            push = push is { } p ? p + add : add;
        }

        foreach (ref readonly var g in w.Wells.AsSpan())
        {
            if (g.Core <= 0)
                continue; // frondes : les coques sont faites pour les frôler
            var d = Vec.Dist(pos, g.Pos);
            var core = WellCore(g);
            var edge = MathF.Max(core + r + margin, core * MathF.Sqrt(k * WellPull(g) / rcs) + r);
            if (d < edge && d > 1e-3f)
            {
                var away = Vec.Scale(Vec.Normalize(pos - g.Pos), (edge - d) * 2 + cls.Speed * 0.5f);
                push = push is { } p ? p + away : away;
            }
            Course(desired, 1.2f, g, edge, ref push);
            // Dérive newtonienne : là où la coque va vraiment compte autant que là où elle veut aller.
            Course(vel, 1.5f, g, edge, ref push);
        }
        return push;
    }

    /// <summary>Pose les puits du scénario (pilier Terrain actif).</summary>
    public static void CreateWells(World w)
    {
        if (!w.Pillars.Terrain)
            return;

        var wells = w.Scenario.Wells;
        for (var i = 0; i < wells.Length; i++)
        {
            var d = wells[i];
            w.Wells.Add(new GravityWell
            {
                Id = w.NextWellId++,
                NameIndex = (short)i,
                Pos = d.Pos,
                Core = d.Core,
                Pull = d.Pull,
                Reach = d.Reach,
                Fixed = true,
                Ttl = float.PositiveInfinity,
                Total = float.PositiveInfinity,
                Eye = 0,
                SourceId = 0,
            });
        }
    }

    /// <summary>Repousse un point de destination hors des cœurs (avec la marge de la coque).</summary>
    public static float3 ClearOfCores(World w, in Ship s, float3 p)
    {
        var output = p;
        foreach (ref readonly var g in w.Wells.AsSpan())
        {
            if (g.Core <= 0)
                continue;
            var edge = WellCore(g) + s.Class.Radius + GameData.Terrain.CoreMargin + 40;
            var d = Vec.Dist(output, g.Pos);
            if (d >= edge)
                continue;
            var dir = d > 1e-3f ? Vec.Normalize(output - g.Pos) : Vec.Normalize(s.Pos - g.Pos);
            output = g.Pos + Vec.Scale(dir, edge);
        }
        return output;
    }
}
