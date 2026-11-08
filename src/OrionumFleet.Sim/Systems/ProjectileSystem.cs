using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Vol et impact des projectiles, port de <c>projectiles.ts</c>.</summary>
public static class ProjectileSystem
{
    /// <summary>Fusée de proximité d'une mine, au-delà du rayon de la coque (lot 1).</summary>
    const float MineFuse = 90;

    static bool IsGuided(WeaponKind weapon) => weapon is WeaponKind.Missile or WeaponKind.Bomb or WeaponKind.Emp;

    public static void UpdateProjectile(World w, int index, float dt)
    {
        ref var p = ref w.Projectiles[index];
        p.Ttl -= dt;
        if (p.Weapon == WeaponKind.Mine)
        {
            UpdateMine(w, ref p, dt);
            return;
        }
        if (p.Ttl <= 0)
        {
            // Une bombe à court de carburant explose quand même là où elle est.
            if (p.Weapon == WeaponKind.Bomb)
                Detonate(w, ref p);
            if (p.Weapon == WeaponKind.Emp)
                EmpDetonate(w, ref p);
            p.Alive = false;
            return;
        }

        var guided = IsGuided(p.Weapon);
        if (guided)
        {
            var ti = p.TargetId != 0 ? w.ShipIndex(p.TargetId) : -1;
            var dir = Vec.Normalize(p.Vel);
            // Lot 1 : le brouillage (guidage ralenti dans la bulle d'un brouilleur) n'est pas porté.
            if (p.GuideDelay is > 0)
            {
                p.GuideDelay -= dt;
            }
            else if (ti >= 0 && w.Ships[ti].IsActive)
            {
                ref readonly var t = ref w.Ships[ti];
                var horizon = MathF.Min(p.Weapon == WeaponKind.Missile ? 2 : 12, Vec.Len(t.Pos - p.Pos) / p.Speed);
                var lead = t.Pos + Vec.Scale(t.Vel, horizon);
                dir = Vec.RotateToward(dir, Vec.Normalize(lead - p.Pos), (p.Turn ?? GameData.Tuning.MissileTurn) * dt);
            }
            // Missiles et bombes accélèrent jusqu'à leur croisière après le lancement (une fronde la leur fait dépasser).
            var speed = MathF.Min(p.Speed * (p.Flung == true ? GameData.Terrain.Sling.SpeedMult : 1), Vec.Len(p.Vel) + p.Speed * 0.8f * dt);
            p.Vel = Vec.Scale(dir, speed);
        }

        // Terrain : tout projectile ressent les puits (les guidés luttent avec leurs moteurs).
        if (w.Wells.Count > 0)
        {
            // Les guidés traversent les frondes sans être courbés par elles (seulement relancés).
            p.Vel += Vec.Scale(Terrain.FieldAt(w.Wells.AsSpan(), p.Pos, !guided), (guided ? Ballistics.GuidedFactor : 1) * dt);
            var flung = p.Flung == true;
            var first = Ballistics.FlingRound(w, p.Pos, ref p.Vel, p.Speed, ref flung, dt);
            if (flung)
                p.Flung = true;
            if (first && guided)
                w.Emit(new SimEvent { Type = SimEventType.Flung, At = p.Pos, OtherId = -1, Value = p.Weapon == WeaponKind.Missile ? 4 : 10 });
        }

        p.PrevPos = p.Pos;
        p.Pos += Vec.Scale(p.Vel, dt);

        if (w.Wells.Count > 0)
        {
            var g = Ballistics.CoreOnSegment(w.Wells.AsSpan(), p.PrevPos, p.Pos);
            if (g >= 0)
            {
                // Avalé par le cœur : pas de souffle, rien n'en ressort.
                p.Alive = false;
                w.Stats.Swallowed++;
                var size = p.Weapon is WeaponKind.Bomb or WeaponKind.Emp ? 14 : p.Weapon == WeaponKind.Missile ? 6 : 3;
                w.Emit(new SimEvent { Type = SimEventType.Swallowed, At = p.Pos, OtherId = w.Wells[g].Id, Value = size });
                return;
            }
        }

        if (p.Weapon is WeaponKind.Bomb or WeaponKind.Emp)
        {
            // Fusée de proximité : explose près de n'importe quelle coque ennemie.
            var fuse = (p.Blast ?? 0) * (p.Weapon == WeaponKind.Emp ? 0.2f : 0.35f);
            foreach (ref readonly var s in w.Ships.AsSpan())
            {
                if (!s.IsActive || s.Side == p.Side)
                    continue;
                if (Vec.Dist(s.Pos, p.Pos) <= s.Class.Radius + fuse)
                {
                    if (p.Weapon == WeaponKind.Emp)
                        EmpDetonate(w, ref p);
                    else
                        Detonate(w, ref p);
                    p.Alive = false;
                    return;
                }
            }
            return;
        }

        var ships = w.Ships.AsSpan();
        for (var i = 0; i < ships.Length; i++)
        {
            ref readonly var s = ref ships[i];
            if (!s.IsActive || s.Side == p.Side)
                continue;
            if (!SegmentSphereHit(p.PrevPos, p.Pos, s.Pos, s.Class.Radius))
                continue;

            p.Alive = false;
            var owner = w.ShipIndex(p.OwnerId);
            if (owner >= 0)
                w.Stats.Hits[ShotCounters.Key(w.Ships[owner].ClassId, p.Weapon)]++;
            var profile = p.Weapon == WeaponKind.Laser
                ? new DamageProfile(p.VsShield, p.VsHull)
                : p.Pierce is { } pierce and not 0 ? new DamageProfile(Pierce: pierce) : default;
            Combat.ApplyDamage(w, i, p.Damage, Vec.Normalize(Vec.Scale(p.Vel, -1)), p.Subsystem, p.OwnerId, profile);
            var size = p.Weapon == WeaponKind.Missile ? 9
                : p.Weapon == WeaponKind.Laser ? MathF.Min(8, 2 + p.Damage * 0.12f)
                : MathF.Min(14, 3 + p.Damage * 0.08f);
            w.Emit(new SimEvent { Type = SimEventType.Explosion, At = p.Pos, Value = size, Side = p.Side });
            return;
        }
    }

    /// <summary>
    /// Souffle de zone : toute coque dans le rayon prend des dégâts qui décroissent avec la distance à sa
    /// surface, alliés compris ; une partie ignore les boucliers. Les drones pris dedans sont détruits.
    /// </summary>
    static void Detonate(World w, ref Projectile p)
    {
        var radius = p.Blast ?? 0;
        w.Stats.BombsDetonated++;
        w.Emit(new SimEvent { Type = SimEventType.Blast, At = p.Pos, Value = radius, Side = p.Side });
        w.Emit(new SimEvent { Type = SimEventType.Explosion, At = p.Pos, Value = radius * 0.5f, Side = p.Side });
        for (var i = 0; i < w.Ships.Count; i++)
        {
            ref readonly var s = ref w.Ships[i];
            if (!s.IsActive)
                continue;
            var d = MathF.Max(0, Vec.Dist(s.Pos, p.Pos) - s.Class.Radius);
            if (d > radius)
                continue;
            var amount = p.Damage * (1 - 0.7f * (d / radius));
            var from = Vec.LenSq(p.Pos - s.Pos) > 1e-6f ? Vec.Normalize(p.Pos - s.Pos) : s.Fwd;
            var friendly = s.Side == p.Side;
            if (friendly)
                w.Stats.BombFriendlyDamage += amount;
            else
                w.Stats.BombDamage += amount;
            Combat.ApplyDamage(w, i, amount, from, null, friendly ? 0 : p.OwnerId, new DamageProfile(Pierce: p.Pierce));
        }
        foreach (ref var dr in w.Drones.AsSpan())
            if (dr.Alive && Vec.Dist(dr.Pos, p.Pos) <= radius)
                w.DamageDrone(ref dr, 1e3f);
    }

    /// <summary>Bombe à énergie : toute face de bouclier dans le rayon tombe à zéro et y reste un moment ; les drones grillent.</summary>
    static void EmpDetonate(World w, ref Projectile p)
    {
        var radius = p.Blast ?? 0;
        w.Stats.EmpDetonated++;
        w.Emit(new SimEvent { Type = SimEventType.EmpBlast, At = p.Pos, Value = radius, Side = p.Side });
        foreach (ref var s in w.Ships.AsSpan())
        {
            if (!s.IsActive || MathF.Max(0, Vec.Dist(s.Pos, p.Pos) - s.Class.Radius) > radius)
                continue;
            if (s.Class.EmpHardened)
            {
                for (var f = 0; f < 6; f++)
                    s.Faces[f] *= 0.5f;
            }
            else
            {
                ((Span<float>)s.Faces).Clear();
                s.ShieldLock = GameData.EmpShieldLock;
                s.ShieldReboot = true;
            }
            w.Emit(new SimEvent { Type = SimEventType.EmpHit, ShipId = s.Id });
        }
        foreach (ref var dr in w.Drones.AsSpan())
            if (dr.Alive && Vec.Dist(dr.Pos, p.Pos) <= radius)
                w.DamageDrone(ref dr, 1e3f);
    }

    static bool SegmentSphereHit(float3 a, float3 b, float3 c, float r)
    {
        var ab = b - a;
        var ac = c - a;
        var l2 = Vec.LenSq(ab);
        var t = l2 > 0 ? MathF.Max(0, MathF.Min(1, Vec.Dot(ac, ab) / l2)) : 0;
        var closest = a + Vec.Scale(ab, t);
        return Vec.LenSq(c - closest) <= r * r;
    }

    /// <summary>
    /// Une mine dérive (en ralentissant, courbée par les puits), s'arme après quelques secondes et explose au
    /// contact de n'importe quelle coque, alliés compris (lot 1 : aucune mine n'est posée dans le périmètre).
    /// </summary>
    static void UpdateMine(World w, ref Projectile p, float dt)
    {
        if (p.Ttl <= 0)
        {
            p.Alive = false;
            return;
        }
        p.Vel = Vec.Scale(p.Vel, MathF.Max(0, 1 - 0.25f * dt));
        if (w.Wells.Count > 0)
            p.Vel += Vec.Scale(Terrain.FieldAt(w.Wells.AsSpan(), p.Pos), dt);
        p.PrevPos = p.Pos;
        p.Pos += Vec.Scale(p.Vel, dt);
        if (w.Wells.Count > 0 && Ballistics.CoreOnSegment(w.Wells.AsSpan(), p.PrevPos, p.Pos) >= 0)
        {
            p.Alive = false;
            w.Stats.Swallowed++;
            w.Emit(new SimEvent { Type = SimEventType.Swallowed, At = p.Pos, OtherId = -1, Value = 6 });
            return;
        }
        if ((p.ArmIn ?? 0) > 0)
        {
            p.ArmIn -= dt;
            return;
        }
        foreach (ref readonly var s in w.Ships.AsSpan())
        {
            if (!s.IsActive || !s.Class.Mobile)
                continue;
            if (Vec.Dist(s.Pos, p.Pos) <= s.Class.Radius + MineFuse)
            {
                Detonate(w, ref p);
                p.Alive = false;
                return;
            }
        }
    }
}
