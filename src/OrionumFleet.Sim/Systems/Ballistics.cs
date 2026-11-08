using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Ce qu'une conduite de tir vise : position, vitesse et rayon de la cible.</summary>
public readonly record struct RoundTarget(float3 Pos, float3 Vel, float Radius);

/// <summary>Guidage d'un projectile (missile, bombe) : vitesse de croisière, virage, délai avant prise de guidage.</summary>
public readonly record struct Guidance(float Speed, float Turn, float Delay = 0);

public enum PathOutcome : byte
{
    Target,
    Core,
    None,
}

public readonly record struct PathResult(PathOutcome Outcome, float Closest, float3 Miss);

/// <summary>Solution de tir. <see cref="Ok"/> faux : aucun chemin (cible cachée derrière un cœur).</summary>
public readonly record struct AimSolution(float3 Dir, float Speed, bool Ok, bool Arc, float Delay);

/// <summary>
/// Balistique à travers le champ de gravité, port de la seconde moitié de <c>terrain.ts</c>
/// (segments, ligne de tir bloquée, intégration d'un projectile, conduite de tir, tir courbe).
/// </summary>
public static class Ballistics
{
    /// <summary>Part du champ que ressentent les projectiles guidés (leurs moteurs luttent contre lui).</summary>
    public const float GuidedFactor = 0.35f;

    /// <summary>Charges du tir courbe (fraction de la vitesse de bouche), essayées dans l'ordre.</summary>
    static readonly float[] ArcCharges = [0.45f, 0.35f, 0.27f];

    /// <summary>Plus long vol permis à un obus lobé (s).</summary>
    const float ArcMaxFlight = 9;

    public static float SegmentDist(float3 a, float3 b, float3 p)
    {
        var ab = b - a;
        var l2 = Vec.LenSq(ab);
        var t = l2 > 1e-9f ? MathF.Max(0, MathF.Min(1, Vec.Dot(p - a, ab) / l2)) : 0;
        return Vec.Dist(a + Vec.Scale(ab, t), p);
    }

    /// <summary>Index du puits dont le cœur (élargi de <paramref name="pad"/>) coupe le segment a→b, ou -1.</summary>
    public static int CoreOnSegment(ReadOnlySpan<GravityWell> wells, float3 a, float3 b, float pad = 0)
    {
        for (var i = 0; i < wells.Length; i++)
        {
            ref readonly var g = ref wells[i];
            if (g.Core <= 0)
                continue;
            if (SegmentDist(a, b, g.Pos) <= Terrain.WellCore(g) + pad)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Test rapide de ligne de tir : un tir droit à <paramref name="speed"/> de a à b tombe-t-il dans un cœur ?
    /// Le rayon de capture grandit pour les projectiles lents (ils se courbent plus) : r·√(1 + 2·pull·r / v²).
    /// </summary>
    public static bool LineBlocked(World w, float3 a, float3 b, float speed)
    {
        foreach (ref readonly var g in w.Wells.AsSpan())
        {
            if (g.Core <= 0)
                continue;
            var r = Terrain.WellCore(g);
            var v = MathF.Max(speed, 1);
            var capture = speed <= 0 ? r * 1.1f : r * MathF.Sqrt(1 + 2 * Terrain.WellPull(g) * r / (v * v));
            if (SegmentDist(a, b, g.Pos) <= capture)
                return true;
        }
        return false;
    }

    /// <summary>Un puits peut-il seulement courber un tir de a à b ? (pré-test bon marché avant de simuler)</summary>
    public static bool FieldOnPath(World w, float3 a, float3 b)
    {
        foreach (ref readonly var g in w.Wells.AsSpan())
            if (SegmentDist(a, b, g.Pos) < g.Reach)
                return true;
        return false;
    }

    /// <summary>
    /// Effet de fronde sur un projectile dans un œil : plus rapide dans sa propre direction (les guidés peuvent
    /// dépasser leur croisière). Rend vrai la première fois que ce projectile est relancé.
    /// </summary>
    public static bool FlingRound(World w, float3 pos, ref float3 vel, float speed, ref bool flung, float dt)
    {
        foreach (ref readonly var g in w.Wells.AsSpan())
        {
            if (g.Eye <= 0 || Vec.Dist(pos, g.Pos) > g.Eye)
                continue;
            var v = Vec.Len(vel);
            var cap = speed * GameData.Terrain.Sling.SpeedMult;
            if (v > 1e-3f && v < cap)
                vel = Vec.Scale(vel, MathF.Min(cap, v + GameData.Terrain.Sling.Fling * dt) / v);
            var first = !flung;
            flung = true;
            return first;
        }
        return false;
    }

    /// <summary>Intègre un projectile à travers le champ (plus grossier que la sim, assez précis pour viser).</summary>
    public static PathResult SimulateRound(World w, float3 from, float3 vel, float ttl, RoundTarget? target, Guidance? guided = null)
    {
        // Même pas et même intégrateur que la sim (Euler semi-implicite à 0,05 s) pour que la prédiction tienne
        // près d'un cœur, où le champ change vite ; les longs vols prennent des pas plus grossiers.
        var steps = Math.Min(200, Math.Max(8, (int)MathF.Ceiling(ttl / 0.05f)));
        var h = ttl / steps;
        var p = from;
        var v = vel;
        var k = guided is not null ? GuidedFactor : 1;
        var roundSpeed = guided is { } gd ? gd.Speed : Vec.Len(vel);
        var flung = false;
        var closest = float.PositiveInfinity;
        var miss = float3.Zero;
        var wells = w.Wells.AsSpan();

        var t = h;
        for (var i = 1; i <= steps; i++, t += h)
        {
            if (guided is { } g)
            {
                var dir = Vec.Normalize(v);
                if (target is { } tg && t >= g.Delay)
                {
                    var tp = tg.Pos + Vec.Scale(tg.Vel, t);
                    dir = Vec.RotateToward(dir, Vec.Normalize(tp - p), g.Turn * h);
                }
                var speed = MathF.Min(g.Speed * (flung ? GameData.Terrain.Sling.SpeedMult : 1), Vec.Len(v) + g.Speed * 0.8f * h);
                v = Vec.Scale(dir, speed);
            }
            v += Vec.Scale(Terrain.FieldAt(wells, p, guided is null), k * h);
            FlingRound(w, p, ref v, roundSpeed, ref flung, h);
            var next = p + Vec.Scale(v, h);
            var core = CoreOnSegment(wells, p, next);
            if (target is { } t2)
            {
                var tp = t2.Pos + Vec.Scale(t2.Vel, t);
                var d = SegmentDist(p, next, tp);
                if (d < closest)
                {
                    closest = d;
                    miss = next - tp;
                }
                if (d <= t2.Radius && core < 0)
                    return new PathResult(PathOutcome.Target, closest, miss);
            }
            if (core >= 0)
                return new PathResult(PathOutcome.Core, closest, miss);
            p = next;
        }
        return new PathResult(PathOutcome.None, closest, miss);
    }

    /// <summary>
    /// Conduite de tir à travers le champ de gravité. Projectiles non guidés : l'avance en ligne droite est
    /// corrigée jusqu'à ce que la trajectoire courbée rencontre la cible. Si le champ avale toutes les
    /// solutions droites et que le tir courbe est permis, les obus sont lancés hors de la ligne (jusqu'à
    /// <see cref="GameData.Terrain.ArcMaxDeg"/>) pour que le puits lui-même les ramène sur la cible.
    /// Projectiles guidés : la trajectoire guidée est vérifiée et, en tir courbe, lobée autour du cœur avant
    /// que le guidage ne prenne la main.
    /// </summary>
    public static AimSolution SolveAim(World w, float3 from, RoundTarget target, float speed, float ttl, bool arcFire, Guidance? guided = null)
    {
        var d0 = Vec.Dist(from, target.Pos);
        var lead = target.Pos + Vec.Scale(target.Vel, d0 / MathF.Max(speed, 1));
        var straight = Vec.Normalize(lead - from);
        if (w.Wells.Count == 0 || !FieldOnPath(w, from, lead))
            return new AimSolution(straight, speed, true, false, 0);

        if (guided is { } g)
        {
            var r = SimulateRound(w, from, Vec.Scale(straight, g.Speed * 0.5f), ttl, target, g with { Delay = 0 });
            if (r.Outcome != PathOutcome.Core)
                return new AimSolution(straight, speed, true, false, 0);
            if (!arcFire)
                return new AimSolution(straight, speed, false, false, 0);
            foreach (var dir in ArcCandidates(w, from, straight))
            {
                foreach (var delay in (ReadOnlySpan<float>)[2.5f, 5, 8])
                {
                    var c = SimulateRound(w, from, Vec.Scale(dir, g.Speed * 0.5f), ttl, target, g with { Delay = delay });
                    if (c.Outcome == PathOutcome.Target)
                        return new AimSolution(dir, speed, true, true, delay);
                }
            }
            return new AimSolution(straight, speed, false, false, 0);
        }

        // Non guidé : corriger le point visé de l'erreur (quelques itérations).
        var aim = lead;
        var aimDir = straight;
        var swallowed = false;
        for (var i = 0; i < 4; i++)
        {
            var r = SimulateRound(w, from, Vec.Scale(aimDir, speed), ttl, target);
            if (r.Outcome == PathOutcome.Target)
                return new AimSolution(aimDir, speed, true, false, 0);
            if (r.Outcome == PathOutcome.Core)
            {
                swallowed = true;
                break;
            }
            if (!float.IsFinite(r.Closest))
                break;
            aim -= r.Miss;
            aimDir = Vec.Normalize(aim - from);
        }
        if (!swallowed)
            return new AimSolution(aimDir, speed, true, false, 0);
        if (!arcFire)
            return new AimSolution(straight, speed, false, false, 0);

        // Tir courbe : lober l'obus à charge réduite, hors de la ligne, et laisser le puits le ramener.
        // (À pleine vitesse la courbure est bien trop faible pour contourner un cœur : la lentille focalise dedans.)
        var range = Vec.Dist(from, target.Pos);
        foreach (var charge in ArcCharges)
        {
            var v = speed * charge;
            var t = MathF.Min(ttl / charge, ArcMaxFlight);
            float3? bestDir = null;
            var bestClosest = float.PositiveInfinity;
            foreach (var c in ArcCandidates(w, from, straight))
            {
                var r = SimulateRound(w, from, Vec.Scale(c, v), t, target);
                if (r.Outcome == PathOutcome.Target)
                    return new AimSolution(c, v, true, true, 0);
                if (r.Outcome == PathOutcome.None && r.Closest < target.Radius * 4 && (bestDir is null || r.Closest < bestClosest))
                    (bestDir, bestClosest) = (c, r.Closest);
            }
            if (bestDir is not { } candidate)
                continue;
            // Affiner le meilleur raté par petites corrections (en restant hors du cœur).
            for (var i = 0; i < 4; i++)
            {
                var r = SimulateRound(w, from, Vec.Scale(candidate, v), t, target);
                if (r.Outcome == PathOutcome.Target)
                    return new AimSolution(candidate, v, true, true, 0);
                if (r.Outcome == PathOutcome.Core)
                    break;
                candidate = Vec.Normalize(Vec.Scale(candidate, range) - Vec.Scale(r.Miss, 0.7f));
            }
        }
        return new AimSolution(straight, speed, false, false, 0);
    }

    /// <summary>Directions de lancement hors de la ligne droite, en s'écartant d'abord du cœur bloquant le plus proche.</summary>
    static float3[] ArcCandidates(World w, float3 from, float3 straight)
    {
        // Le puits bloquant : le plus proche de la ligne de tir.
        var well = -1;
        var bd = float.PositiveInfinity;
        var wells = w.Wells.AsSpan();
        for (var i = 0; i < wells.Length; i++)
        {
            if (wells[i].Core <= 0)
                continue;
            var d = SegmentDist(from, from + Vec.Scale(straight, 4000), wells[i].Pos);
            if (d < bd)
                (well, bd) = (i, d);
        }
        var side = well >= 0
            ? from + Vec.Scale(straight, Vec.Dot(wells[well].Pos - from, straight)) - wells[well].Pos
            : Vec.Cross(straight, Vec.Up);
        if (Vec.LenSq(side) < 1e-6f)
            side = Vec.Cross(straight, Vec.Up);
        if (Vec.LenSq(side) < 1e-6f)
            side = new float3(1, 0, 0);
        side = Vec.Normalize(side);

        // Axes de rotation : d'abord vers `side` (loin du cœur), puis de l'autre côté, puis haut/bas.
        var axisMain = Vec.Normalize(Vec.Cross(straight, side));
        var axisAlt = Vec.Normalize(Vec.Cross(straight, axisMain));
        var output = new float3[20];
        var n = 0;
        foreach (var deg in (ReadOnlySpan<float>)[10, 18, 27, 38, GameData.Terrain.ArcMaxDeg])
        {
            var a = deg * MathF.PI / 180;
            output[n++] = Vec.RotateAxis(straight, axisMain, -a);
            output[n++] = Vec.RotateAxis(straight, axisMain, a);
            output[n++] = Vec.RotateAxis(straight, axisAlt, a);
            output[n++] = Vec.RotateAxis(straight, axisAlt, -a);
        }
        return output;
    }
}
