using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// Batteries des capitaux (décisions du 2026-11-21) : une arme à plusieurs tourelles placées sur la coque.
/// <list type="bullet">
/// <item>chaque tourelle vivante tire depuis sa propre position (bordées, tourelles dorsales, tubes) ;</item>
/// <item>la lance tient un rayon continu plusieurs secondes sur sa cible ;</item>
/// <item>le canon axial ne tire que si le nez est pointé (arc de quelques degrés) ;</item>
/// <item>un impact de coque use la tourelle la plus proche puis la détruit : le capital se désarme sous nos yeux.</item>
/// </list>
/// </summary>
public static class Batteries
{
    /// <summary>Position d'une tourelle dans le monde (repère de la coque : +Z nez, +X bâbord, +Y dorsal, en mètres).</summary>
    public static float3 HardpointWorld(in Ship s, float3 local)
    {
        var f = Vec.FrameFromForward(s.Fwd);
        return s.Pos + Vec.Scale(f.Right, -local.X) + Vec.Scale(f.Up, local.Y) + Vec.Scale(f.Fwd, local.Z);
    }

    public static int AliveCount(in WeaponState ws) => System.Numerics.BitOperations.PopCount(ws.Alive);

    public static bool IsAlive(in WeaponState ws, int turret) => (ws.Alive & (1UL << turret)) != 0;

    /// <summary>
    /// Tir d'une batterie sur une cible choisie (la recharge est posée par l'appelant). Rend faux si rien n'est parti
    /// (toutes les tourelles détruites, nez pas pointé pour le canon axial).
    /// </summary>
    internal static bool Fire(World w, int shipIndex, ref WeaponState ws, WeaponDef def, int targetIndex, float damage, Subsystem? aimed)
    {
        if (ws.Alive == 0)
            return false;
        var hardpoints = def.Hardpoints!;
        ref readonly var s = ref w.Ships[shipIndex];
        ref readonly var target = ref w.Ships[targetIndex];

        if (def.BeamDuration is not null)
        {
            // Lance : le rayon part et tient (UpdateLance, à chaque pas).
            ws.BeamLeft = def.BeamDuration.Value;
            ws.TargetId = target.Id;
            return true;
        }

        if (def.Id == WeaponId.Spinal && Vec.AngleBetween(s.Fwd, target.Pos - s.Pos) > def.Arc * MathF.PI / 180)
            return false;

        if (def.Kind == WeaponKind.Missile)
        {
            // Salve répartie sur les tubes vivants.
            var salvo = (int)(def.Salvo ?? 1);
            var tube = 0;
            for (var i = 0; i < salvo; i++)
            {
                tube = NextAlive(ws, tube, hardpoints.Length);
                var from = HardpointWorld(s, hardpoints[tube]);
                var dir = Vec.Normalize(Vec.Normalize(target.Pos - from) + Vec.Scale(w.Rng.Unit(), 0.5f) + Vec.Scale(Vec.FrameFromForward(s.Fwd).Up, 0.4f));
                ref var p = ref w.SpawnProjectile(shipIndex, WeaponKind.Missile, from, Vec.Scale(dir, def.Speed * 0.4f), damage, target.Id, aimed,
                    def.Range * 1.4f / def.Speed, def.Speed);
                p.Turn = def.Turn;
                tube++;
            }
            w.Emit(new SimEvent { Type = SimEventType.Fire, Weapon = WeaponKind.Missile, WeaponId = def.Id, At = s.Pos, Vector = s.Fwd, HasVector = true, Side = s.Side, OtherId = s.Id });
            return true;
        }

        var fired = false;
        for (var t = 0; t < hardpoints.Length; t++)
        {
            if (!IsAlive(ws, t))
                continue;
            var from = HardpointWorld(s, hardpoints[t]);
            if (def.Beam == true)
            {
                // Canon axial à particules : un rayon instantané, colossal.
                FireBeam(w, shipIndex, targetIndex, def, from, damage, aimed, BeamKind.Lance, def.Id);
                fired = true;
                continue;
            }
            var d = Vec.Dist(from, target.Pos);
            var ttl = def.Range * 1.25f / def.Speed;
            var lead = target.Pos + Vec.Scale(target.Vel, d / def.Speed);
            if (w.Wells.Count > 0 && Ballistics.LineBlocked(w, from, lead, def.Speed))
                continue; // cette tourelle ne voit pas la cible (un astre entre les deux)
            var dir = Combat.Jitter(w, Vec.Normalize(lead - from), (def.Spread ?? 0) + Combat.TrackingPenalty(s, target, def, d));
            ref var p = ref w.SpawnProjectile(shipIndex, def.Kind, from + Vec.Scale(dir, 12), Vec.Scale(dir, def.Speed), damage, 0, aimed, ttl, def.Speed);
            if (def.Kind == WeaponKind.Laser)
                (p.VsShield, p.VsHull) = (def.VsShield, def.VsHull);
            if (def.Pierce is { } pierce and not 0)
                p.Pierce = pierce;
            w.Emit(new SimEvent { Type = SimEventType.Fire, Weapon = def.Kind, WeaponId = def.Id, At = from, Vector = dir, HasVector = true, Side = s.Side, OtherId = s.Id });
            fired = true;
        }
        return fired;
    }

    static int NextAlive(in WeaponState ws, int from, int count)
    {
        for (var k = 0; k < count; k++)
        {
            var t = (from + k) % count;
            if (IsAlive(ws, t))
                return t;
        }
        return 0;
    }

    /// <summary>Lance en cours : chaque émetteur vivant tient son rayon sur la cible et lui inflige sa part, pas après pas.</summary>
    internal static void UpdateLance(World w, int shipIndex, ref WeaponState ws, WeaponDef def, double dt)
    {
        ws.BeamLeft -= dt;
        var ti = ws.TargetId != 0 ? w.ShipIndex(ws.TargetId) : -1;
        ref readonly var s = ref w.Ships[shipIndex];
        if (ti < 0 || !w.Ships[ti].IsActive || Vec.Dist(s.Pos, w.Ships[ti].Pos) > def.Range * 1.15f || ws.Alive == 0)
        {
            ws.BeamLeft = 0;
            return;
        }
        var share = def.Damage * GameData.Tuning.DamageScale * (float)(dt / def.BeamDuration!.Value);
        var hardpoints = def.Hardpoints!;
        for (var t = 0; t < hardpoints.Length; t++)
            if (IsAlive(ws, t))
                FireBeam(w, shipIndex, ti, def, HardpointWorld(s, hardpoints[t]), share, null, BeamKind.Lance, def.Id, emitFire: false);
    }

    /// <summary>Rayon instantané d'une tourelle vers la cible : il touche la coque (sphère) ou s'arrête sur un astre.</summary>
    static void FireBeam(World w, int shipIndex, int targetIndex, WeaponDef def, float3 from, float damage, Subsystem? aimed, BeamKind kind,
        WeaponId id, bool emitFire = true)
    {
        ref readonly var s = ref w.Ships[shipIndex];
        ref readonly var target = ref w.Ships[targetIndex];
        var dir = Vec.Normalize(target.Pos - from);
        var r = target.Class.Radius;
        var along = Vec.Dot(target.Pos - from, dir);
        var to = from + Vec.Scale(dir, MathF.Max(0, along - r * 0.85f));
        var hit = true;
        var core = w.Wells.Count > 0 ? Ballistics.CoreOnSegment(w.Wells.AsSpan(), from, to) : -1;
        if (core >= 0)
        {
            hit = false;
            ref readonly var g = ref w.Wells[core];
            to = from + Vec.Scale(dir, MathF.Max(0, Vec.Dot(g.Pos - from, dir) - Terrain.WellCore(g)));
        }
        if (emitFire)
            w.Emit(new SimEvent { Type = SimEventType.Fire, Weapon = def.Kind, WeaponId = id, At = from, Vector = dir, HasVector = true, Side = s.Side, OtherId = s.Id });
        w.Emit(new SimEvent { Type = SimEventType.Beam, At = from, Vector = to, HasVector = true, Side = s.Side, Beam = kind, OtherId = s.Id, WeaponId = id });
        if (hit)
            Combat.ApplyDamage(w, targetIndex, damage, Vec.Scale(dir, -1), aimed, s.Id, new DamageProfile(def.VsShield, def.VsHull, def.Pierce));
    }

    /// <summary>
    /// Dégâts localisés : un impact de coque en <paramref name="at"/> use la tourelle vivante la plus proche (à moins
    /// d'un demi-rayon de coque) ; au-delà de ses points de vie, elle saute.
    /// </summary>
    internal static void Wear(World w, int shipIndex, float3 at, float amount)
    {
        ref var s = ref w.Ships[shipIndex];
        var cls = s.Class;
        var bestWeapon = -1;
        var bestTurret = -1;
        var bestD = cls.Radius * 0.5f;
        for (var wi = 0; wi < s.WeaponCount; wi++)
        {
            ref readonly var ws = ref s.Weapons[wi];
            var hardpoints = cls.Weapons[ws.DefIndex].Hardpoints;
            if (hardpoints is null || ws.Alive == 0)
                continue;
            for (var t = 0; t < hardpoints.Length; t++)
            {
                if (!IsAlive(ws, t))
                    continue;
                var d = Vec.Dist(HardpointWorld(s, hardpoints[t]), at);
                if (d < bestD)
                    (bestWeapon, bestTurret, bestD) = (wi, t, d);
            }
        }
        if (bestWeapon < 0)
            return;
        ref var hit = ref s.Weapons[bestWeapon];
        var def = cls.Weapons[hit.DefIndex];
        hit.Wear += amount;
        if (hit.Wear < (def.TurretHp ?? float.PositiveInfinity))
            return;
        hit.Wear = 0;
        hit.Alive &= ~(1UL << bestTurret);
        if (hit.Alive == 0)
            hit.BeamLeft = 0;
        w.Emit(new SimEvent
        {
            Type = SimEventType.TurretDestroyed, ShipId = s.Id, At = HardpointWorld(s, def.Hardpoints![bestTurret]), WeaponId = def.Id,
            Value = bestTurret, Side = s.Side,
        });
    }
}
