using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Répartition d'un coup entre boucliers et coque (les lasers arrachent les boucliers, les bombes les percent).</summary>
public readonly record struct DamageProfile(float? VsShield = null, float? VsHull = null, float? Pierce = null);

/// <summary>
/// Combat, port de <c>combat.ts</c> : dégâts directionnels, défenses (boucliers, réparations, menace),
/// conduite de tir, défense ponctuelle et bombe à énergie. Les coques sont désignées par leur index dans
/// <see cref="World.Ships"/>.
/// </summary>
public static class Combat
{
    /// <summary>Mines qu'un poseur garde au plus en vie (lot 1).</summary>
    public const int MineFieldMax = 18;
    /// <summary>La défense ponctuelle ne repère une mine que d'aussi près.</summary>
    public const float MineSpot = 130;
    /// <summary>Cadence de tir pendant le tir courbe.</summary>
    public const float ArcFireRof = 0.85f;
    /// <summary>Les bombes sont trop lentes pour les escortes et les frégates : elles visent capitaux, porteurs et stations.</summary>
    const float BombMinRadius = 25;
    /// <summary>Au-delà de cette dispersion supplémentaire (rad), une tourelle renonce à une cible qu'elle ne peut pas suivre.</summary>
    const float TrackingGiveUp = 0.05f;

    static readonly Subsystem[] Subsystems = [Subsystem.Engines, Subsystem.Weapons, Subsystem.Shields];

    // -------------------------------------------------------------------------
    // Dégâts

    /// <summary>La face de bouclier sur laquelle tombe un coup venant de <paramref name="fromDir"/> (monde, unitaire, coque → source).</summary>
    public static Face FaceFor(in Ship s, float3 fromDir)
    {
        var f = Vec.FrameFromForward(s.Fwd);
        var a = Vec.Dot(fromDir, f.Fwd);
        var r = Vec.Dot(fromDir, f.Right);
        var u = Vec.Dot(fromDir, f.Up);
        var aa = MathF.Abs(a);
        var ar = MathF.Abs(r);
        var au = MathF.Abs(u);
        if (aa >= ar && aa >= au)
            return a >= 0 ? Face.Front : Face.Back;
        if (ar >= au)
            return r >= 0 ? Face.Starboard : Face.Port;
        return u >= 0 ? Face.Dorsal : Face.Ventral;
    }

    /// <summary>Maximum actuel d'une face, répartition d'énergie et focus de la posture Défense compris.</summary>
    public static float FaceMax(in Ship s, Face face)
    {
        if (s.Subsystems[(int)Subsystem.Shields].Offline)
            return 0;
        var max = s.Class.ShieldFace * CombatRules.ShieldMult(s);
        if (s.Posture == Posture.Defense && face == MostThreatenedFace(s))
            return max * (1 + GameData.Tuning.DefenseFocusBonus);
        return max;
    }

    public static Face MostThreatenedFace(in Ship s)
    {
        var best = 0;
        for (var i = 1; i < 6; i++)
            if (s.Threat[i] > s.Threat[best])
                best = i;
        return (Face)best;
    }

    public static void ApplyDamage(World w, int shipIndex, float amount, float3 fromDir, Subsystem? aimed, int attackerId,
        DamageProfile profile = default)
    {
        ref var s = ref w.Ships[shipIndex];
        if (!s.IsActive || amount <= 0 || s.TimeScale == 0)
            return; // figée dans une stase : hors de portée du temps
        var face = FaceFor(s, fromDir);
        s.Threat[(int)face] += amount;
        var attacker = attackerId != 0 ? w.ShipIndex(attackerId) : -1;
        if (attacker >= 0)
            w.Ships[attacker].Stats.DamageDealt += amount;
        var at = s.Pos + Vec.Scale(fromDir, s.Class.Radius);

        var vsShield = profile.VsShield ?? 1;
        var vsHull = profile.VsHull ?? 1;
        var pierced = amount * (profile.Pierce ?? 0);
        var onShield = (amount - pierced) * vsShield;
        var absorbed = MathF.Min(s.Faces[(int)face], onShield);
        s.Faces[(int)face] -= absorbed;
        if (absorbed > 0)
        {
            w.Emit(new SimEvent { Type = SimEventType.ShieldHit, ShipId = s.Id, At = at, Face = face, Value = absorbed });
            if (s.Faces[(int)face] <= 0)
                w.Emit(new SimEvent { Type = SimEventType.ShieldBreak, ShipId = s.Id, At = at, Face = face });
        }

        var overflow = (pierced + (onShield - absorbed) / vsShield) * vsHull;
        if (overflow <= 0)
            return;
        s.Hull -= overflow;
        w.Emit(new SimEvent { Type = SimEventType.HullHit, ShipId = s.Id, At = at, Value = overflow });

        var target = aimed;
        var share = GameData.Tuning.AimedSubsystemShare;
        if (target is null && w.Rng.Chance(GameData.Tuning.RandomSubsystemChance))
        {
            target = w.Rng.Pick<Subsystem>(Subsystems);
            share = GameData.Tuning.RandomSubsystemShare;
        }
        if (target is { } t)
        {
            ref var ss = ref s.Subsystems[(int)t];
            ss.Hp = MathF.Max(0, ss.Hp - overflow * share);
            if (ss.Hp <= 0 && !ss.Offline)
            {
                ss.Offline = true;
                w.Emit(new SimEvent { Type = SimEventType.SubsystemDown, ShipId = s.Id, Subsystem = t });
            }
        }

        if (s.Hull <= 0)
            w.Cripple(shipIndex, attackerId);
    }

    /// <summary>
    /// Les drones sont des bombardiers de sous-systèmes : trop petits et trop proches pour les faces de
    /// bouclier, ils passent dessous et visent un sous-système. Peu de dégâts de coque, décisifs par l'effet.
    /// </summary>
    public static void DroneStrike(World w, int shipIndex, float amount, float3 fromDir, Subsystem aimed, int attackerId)
    {
        ref var s = ref w.Ships[shipIndex];
        if (!s.IsActive || amount <= 0 || s.TimeScale == 0)
            return;
        var face = FaceFor(s, fromDir);
        s.Threat[(int)face] += amount;
        var attacker = attackerId != 0 ? w.ShipIndex(attackerId) : -1;
        if (attacker >= 0)
            w.Ships[attacker].Stats.DamageDealt += amount;
        var at = s.Pos + Vec.Scale(fromDir, s.Class.Radius * 0.8f);
        var hullDamage = amount * GameData.Tuning.DroneHullShare;
        s.Hull -= hullDamage;
        w.Emit(new SimEvent { Type = SimEventType.HullHit, ShipId = s.Id, At = at, Value = hullDamage });
        ref var ss = ref s.Subsystems[(int)aimed];
        ss.Hp = MathF.Max(0, ss.Hp - amount * GameData.Tuning.DroneSubsystemMult);
        if (ss.Hp <= 0 && !ss.Offline)
        {
            ss.Offline = true;
            w.Stats.DroneSubsystemKills++;
            w.Emit(new SimEvent { Type = SimEventType.SubsystemDown, ShipId = s.Id, Subsystem = aimed });
        }
        if (s.Hull <= 0)
            w.Cripple(shipIndex, attackerId);
    }

    // -------------------------------------------------------------------------
    // Boucliers, réparations, menace

    public static void UpdateDefences(ref Ship s, float dt)
    {
        var cls = s.Class;
        s.ShieldLock = MathF.Max(0, s.ShieldLock - dt);
        var reboot = s.ShieldReboot ? GameData.Tuning.ShieldRebootMult : 1;
        var regen = s.Subsystems[(int)Subsystem.Shields].Offline || s.ShieldLock > 0
            ? 0
            : cls.ShieldRegen * CombatRules.ShieldRegenMult(s) * reboot * dt;
        for (var f = 0; f < 6; f++)
            s.Faces[f] = MathF.Min(FaceMax(s, (Face)f), s.Faces[f] + regen);

        if (s.ShieldReboot && s.ShieldLock <= 0)
        {
            var full = true;
            for (var f = 0; f < 6 && full; f++)
                full = s.Faces[f] >= FaceMax(s, (Face)f) - 1e-6f;
            if (full)
                s.ShieldReboot = false;
        }

        for (var k = 0; k < 3; k++)
        {
            ref var ss = ref s.Subsystems[k];
            var cap = ss.Max * GameData.Tuning.SubsystemRepairCap;
            if (ss.Hp < cap)
                ss.Hp = MathF.Min(cap, ss.Hp + ss.Max * GameData.Tuning.SubsystemRepairRate * dt);
            if (ss.Offline && ss.Hp >= ss.Max * GameData.Tuning.SubsystemRestart)
                ss.Offline = false;
        }

        var decay = MathF.Pow(0.5f, dt / GameData.Tuning.ThreatHalfLife);
        for (var f = 0; f < 6; f++)
            s.Threat[f] *= decay;
    }

    // -------------------------------------------------------------------------
    // Armes

    public static float3 MountDir(in Ship s, WeaponDef def, float3 toTarget)
    {
        var f = Vec.FrameFromForward(s.Fwd);
        return def.Mount switch
        {
            Mount.Nose => f.Fwd,
            Mount.Port => Vec.Scale(f.Right, -1),
            Mount.Starboard => f.Right,
            _ => Vec.Normalize(toTarget),
        };
    }

    public static bool InArc(in Ship s, WeaponDef def, float3 targetPos)
    {
        if (def.Arc >= 180)
            return true;
        var to = targetPos - s.Pos;
        return Vec.AngleBetween(MountDir(s, def, to), to) <= def.Arc * MathF.PI / 180;
    }

    static float ShieldFraction(in Ship s)
    {
        var max = s.Class.ShieldFace * 6;
        if (max <= 0)
            return 0;
        float sum = 0;
        for (var f = 0; f < 6; f++)
            sum += s.Faces[f];
        return sum / max;
    }

    /// <summary>
    /// Là où une bombe à énergie rapporte : une coque ennemie avec d'autres ennemis serrés autour (les grosses
    /// comptent double) et aucune coque amie dans le souffle. Rend l'index de la cible, ou -1.
    /// </summary>
    public static int EmpTarget(World w, int shipIndex, float minWeight = 4)
    {
        ref readonly var s = ref w.Ships[shipIndex];
        var emp = s.Class.Emp;
        if (emp is null || s.EmpAmmo <= 0)
            return -1;
        var blast = emp.Blast ?? 0;
        var ships = w.Ships.AsSpan();
        var best = -1;
        var bestScore = minWeight;
        for (var e = 0; e < ships.Length; e++)
        {
            ref readonly var enemy = ref ships[e];
            if (!enemy.IsActive || enemy.Side == s.Side || Vec.Dist(s.Pos, enemy.Pos) > emp.Range)
                continue;
            float score = 0;
            var friendly = false;
            foreach (ref readonly var o in ships)
            {
                if (!o.IsActive || Vec.Dist(o.Pos, enemy.Pos) > blast * 0.8f + o.Class.Radius)
                    continue;
                if (o.Side == s.Side)
                    friendly = true;
                else
                    score += (o.Class.Role is Role.Capital or Role.Carrier or Role.Depot ? 2 : 1) * (0.3f + 0.7f * ShieldFraction(o));
            }
            if (!friendly && score >= bestScore)
                (best, bestScore) = (e, score);
        }
        return best;
    }

    /// <summary>Sert une demande de bombe à énergie dès que la cible est à portée.</summary>
    static void FireEmp(World w, int shipIndex)
    {
        ref var s = ref w.Ships[shipIndex];
        var emp = s.Class.Emp;
        if (emp is null || s.EmpTargetId == 0)
            return;
        var ti = w.ShipIndex(s.EmpTargetId);
        if (s.EmpAmmo <= 0 || ti < 0 || !w.Ships[ti].IsActive)
        {
            s.EmpTargetId = 0;
            return;
        }
        ref readonly var target = ref w.Ships[ti];
        if (Vec.Dist(s.Pos, target.Pos) > emp.Range)
            return; // garde la demande jusqu'à ce que la coque se rapproche
        var ttl = emp.Range * 1.5f / emp.Speed;
        var aim = Ballistics.SolveAim(w, s.Pos, new RoundTarget(target.Pos, target.Vel, target.Class.Radius + (emp.Blast ?? 0) * 0.2f),
            emp.Speed, ttl, s.ArcFire, new Guidance(emp.Speed, emp.Turn ?? 0.18f));
        if (!aim.Ok)
            return; // un puits l'avalerait : on garde la demande jusqu'à ce qu'un chemin s'ouvre
        var dir = aim.Dir;
        var from = s.Pos + Vec.Scale(dir, s.Class.Radius + 8);
        var targetId = target.Id;
        ref var p = ref w.SpawnProjectile(shipIndex, WeaponKind.Emp, from, Vec.Scale(dir, emp.Speed * 0.5f), 0, targetId, null, ttl, emp.Speed);
        (p.Hp, p.Blast, p.Turn) = (emp.Hp, emp.Blast, emp.Turn);
        if (aim.Delay > 0)
            p.GuideDelay = aim.Delay;
        s.EmpAmmo--;
        s.EmpTargetId = 0;
        w.Emit(new SimEvent
        {
            Type = SimEventType.Fire, Weapon = WeaponKind.Emp, WeaponId = emp.Id, At = from, Vector = dir, HasVector = true,
            Side = s.Side, OtherId = s.Id,
        });
    }

    /// <param name="dt">Pas local en double (<see cref="World.DtExact"/> × échelle de temps) : il vide les recharges.</param>
    public static void FireWeapons(World w, int shipIndex, double dt)
    {
        ref var s = ref w.Ships[shipIndex];
        if (s.Subsystems[(int)Subsystem.Weapons].Offline || s.TimeScale == 0)
            return;
        FireEmp(w, shipIndex);
        // Tir courbe : la conduite de tir calcule chaque lancer autour des cœurs (cadence réduite).
        var mult = CombatRules.WeaponMult(s) * (s.ArcFire ? ArcFireRof : 1);
        var hasOrder = s.Orders.Count > 0;
        var order = hasOrder ? s.Orders[0] : default;
        var hostile = hasOrder && CombatRules.IsHostileOrder(order.Type);
        var focusId = order.FocusId != 0 ? order.FocusId : hostile ? order.TargetId : 0;
        var aimed = hostile || order.FocusId != 0 ? order.Subsystem : null;
        var cls = s.Class;

        for (var wi = 0; wi < s.WeaponCount; wi++)
        {
            ref var ws = ref s.Weapons[wi];
            var def = cls.Weapons[ws.DefIndex];
            ws.Cooldown -= dt;
            if (ws.Cooldown > 0)
                continue;

            if (def.Kind == WeaponKind.Pd)
            {
                FirePointDefence(w, shipIndex, def);
                ws.Cooldown = 1 / (def.Rof * mult);
                continue;
            }

            var ti = PickShipTarget(w, shipIndex, ref ws, def, focusId);
            if (ti < 0)
            {
                ws.TargetId = 0;
                ws.Cooldown = 0.25f;
                continue;
            }
            ref readonly var target = ref w.Ships[ti];
            ws.TargetId = target.Id;
            var subsystem = target.Id == focusId ? aimed : null;
            var damage = def.Damage * GameData.Tuning.DamageScale;

            if (def.Kind == WeaponKind.Kinetic || (def.Kind == WeaponKind.Laser && def.Beam != true))
            {
                // Projectile qui voyage : obus cinétique ou trait laser. La conduite de tir vise à travers les puits.
                var d = Vec.Dist(s.Pos, target.Pos);
                var ttl = def.Range * 1.25f / def.Speed;
                var aim = Ballistics.SolveAim(w, s.Pos, new RoundTarget(target.Pos, target.Vel, target.Class.Radius), def.Speed, ttl, s.ArcFire);
                if (!aim.Ok)
                {
                    // Cachée derrière un cœur : chercher une autre cible la prochaine fois.
                    ws.TargetId = 0;
                    ws.BlockedId = target.Id;
                    ws.BlockedUntil = w.Time + 3;
                    w.Stats.BlockedShots++;
                    ws.Cooldown = 0.5f;
                    continue;
                }
                var dir = Jitter(w, aim.Dir, (def.Spread ?? 0) + TrackingPenalty(s, target, def, d));
                var from = s.Pos + Vec.Scale(dir, cls.Radius * 0.8f);
                var life = aim.Arc ? MathF.Min(ttl * (def.Speed / aim.Speed), 9.5f) : ttl;
                ref var p = ref w.SpawnProjectile(shipIndex, def.Kind, from, Vec.Scale(dir, aim.Speed), damage, 0, subsystem, life, aim.Speed);
                if (def.Kind == WeaponKind.Laser)
                    (p.VsShield, p.VsHull) = (def.VsShield, def.VsHull);
                if (def.Pierce is { } pierce and not 0)
                    p.Pierce = pierce;
                if (aim.Arc)
                    w.Stats.ArcShots[s.Side]++;
                w.Emit(new SimEvent
                {
                    Type = SimEventType.Fire, Weapon = def.Kind, WeaponId = def.Id, At = from, Vector = dir, HasVector = true,
                    Side = s.Side, OtherId = s.Id,
                });
            }
            else if (def.Kind == WeaponKind.Laser)
            {
                FireTurbolaser(w, shipIndex, ti, def, damage, subsystem);
            }
            else if (def.Kind == WeaponKind.Missile)
            {
                var n = (int)(def.Salvo ?? 1);
                var targetId = target.Id;
                var targetPos = target.Pos;
                for (var i = 0; i < n; i++)
                {
                    var dir = Vec.Normalize(Vec.Normalize(targetPos - s.Pos) + Vec.Scale(w.Rng.Unit(), 0.6f));
                    var from = s.Pos + Vec.Scale(dir, cls.Radius);
                    w.SpawnProjectile(shipIndex, WeaponKind.Missile, from, Vec.Scale(dir, def.Speed * 0.6f), damage, targetId, subsystem,
                        def.Range * 1.6f / def.Speed, def.Speed);
                }
                w.Emit(new SimEvent
                {
                    Type = SimEventType.Fire, Weapon = WeaponKind.Missile, WeaponId = def.Id, At = s.Pos, Vector = s.Fwd, HasVector = true,
                    Side = s.Side, OtherId = s.Id,
                });
            }
            else if (def.Kind == WeaponKind.Bomb)
            {
                // Lente et peu guidée : lancée vers le point d'interception prévu.
                if (!BombSafe(w, s, target, def.Blast ?? 0))
                {
                    ws.Cooldown = 1;
                    continue;
                }
                var ttl = def.Range * 1.4f / def.Speed;
                var aim = Ballistics.SolveAim(w, s.Pos,
                    new RoundTarget(target.Pos, target.Vel, target.Class.Radius + (def.Blast ?? 0) * 0.35f),
                    def.Speed, ttl, s.ArcFire, new Guidance(def.Speed, def.Turn ?? 0.15f));
                if (!aim.Ok)
                {
                    ws.TargetId = 0;
                    ws.BlockedId = target.Id;
                    ws.BlockedUntil = w.Time + 3;
                    w.Stats.BlockedShots++;
                    ws.Cooldown = 1;
                    continue;
                }
                var dir = aim.Dir;
                var from = s.Pos + Vec.Scale(dir, cls.Radius + 6);
                ref var p = ref w.SpawnProjectile(shipIndex, WeaponKind.Bomb, from, Vec.Scale(dir, def.Speed * 0.5f), damage, target.Id, null, ttl, def.Speed);
                (p.Hp, p.Blast, p.Pierce, p.Turn) = (def.Hp, def.Blast, def.Pierce, def.Turn);
                if (aim.Delay > 0)
                    p.GuideDelay = aim.Delay;
                if (aim.Arc)
                    w.Stats.ArcShots[s.Side]++;
                w.Stats.BombsFired++;
                w.Emit(new SimEvent
                {
                    Type = SimEventType.Fire, Weapon = WeaponKind.Bomb, WeaponId = def.Id, At = from, Vector = dir, HasVector = true,
                    Side = s.Side, OtherId = s.Id,
                });
            }
            // Lot 1 : les mines (poseur Semeur) ne sont pas portées.
            ws.Cooldown = 1 / (def.Rof * mult);
        }
    }

    /// <summary>Turbolaser : rayon instantané. La dispersion et le suivi décident s'il touche.</summary>
    static void FireTurbolaser(World w, int shipIndex, int targetIndex, WeaponDef def, float damage, Subsystem? aimed)
    {
        ref readonly var s = ref w.Ships[shipIndex];
        ref readonly var target = ref w.Ships[targetIndex];
        var d = Vec.Dist(s.Pos, target.Pos);
        var lagged = target.Pos - Vec.Scale(target.Vel - s.Vel, def.AimLag ?? 0);
        var dir = Jitter(w, Vec.Normalize(lagged - s.Pos), (def.Spread ?? 0) + TrackingPenalty(s, target, def, d));
        var from = s.Pos + Vec.Scale(dir, s.Class.Radius * 0.8f);
        var along = Vec.Dot(target.Pos - from, dir);
        var closest = from + Vec.Scale(dir, along);
        var r = target.Class.Radius;
        var miss = Vec.Dist(closest, target.Pos);
        var hit = along > 0 && miss <= r;
        var to = hit
            ? closest + Vec.Scale(dir, -MathF.Sqrt(MathF.Max(0, r * r - miss * miss)))
            : from + Vec.Scale(dir, def.Range);
        // La lumière tombe aussi dans un cœur : le rayon s'arrête à son bord.
        var core = w.Wells.Count > 0 ? Ballistics.CoreOnSegment(w.Wells.AsSpan(), from, to) : -1;
        if (core >= 0)
        {
            hit = false;
            ref readonly var g = ref w.Wells[core];
            var along2 = Vec.Dot(g.Pos - from, dir);
            to = from + Vec.Scale(dir, MathF.Max(0, along2 - Terrain.WellCore(g)));
        }
        w.Emit(new SimEvent
        {
            Type = SimEventType.Fire, Weapon = WeaponKind.Laser, WeaponId = def.Id, At = from, Vector = dir, HasVector = true,
            Side = s.Side, OtherId = s.Id,
        });
        w.Emit(new SimEvent { Type = SimEventType.Beam, At = from, Vector = to, HasVector = true, Side = s.Side, Beam = BeamKind.Turbolaser, OtherId = s.Id });
        if (hit)
            ApplyDamage(w, targetIndex, damage, Vec.Normalize(s.Pos - target.Pos), aimed, s.Id, new DamageProfile(def.VsShield, def.VsHull));
    }

    /// <summary>Les bombes touchent aussi les alliés : retenir le tir tant qu'une coque amie (ou le tireur) est près de la cible.</summary>
    static bool BombSafe(World w, in Ship s, in Ship target, float blast)
    {
        foreach (ref readonly var o in w.Ships.AsSpan())
        {
            if (!o.IsActive || o.Side != s.Side)
                continue;
            // Le souffle part à la surface de la cible : on mesure depuis là.
            if (Vec.Dist(o.Pos, target.Pos) < blast + o.Class.Radius + target.Class.Radius + 60)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Conduite de tir : une tourelle garde la cible ordonnée tant qu'elle peut vraiment la suivre ; sinon elle
    /// passe à la meilleure cible qu'elle peut toucher (canons lourds contre une frégate collée). Rend un index ou -1.
    /// </summary>
    static int PickShipTarget(World w, int shipIndex, ref WeaponState ws, WeaponDef def, int focusId)
    {
        ref readonly var s = ref w.Ships[shipIndex];
        var range = def.Range;
        var terrain = w.Wells.Count > 0;
        var beam = def.Beam == true;
        // Lot 1 : le brouilleur (portée réduite dans sa bulle) n'est pas porté : jamRange = 1.
        const float jamRange = 1;
        var blockedId = ws.BlockedId;
        var blockedUntil = ws.BlockedUntil;

        // Terrain : une cible derrière un cœur est hors d'atteinte des rayons, et des obus sauf tir courbe ;
        // une cible que la conduite de tir vient de rater attend un moment.
        bool Shadowed(in Ship s, in Ship t) => terrain && Ballistics.LineBlocked(w, s.Pos, t.Pos, beam ? 0 : def.Speed);
        bool RecentlyBlocked(in Ship t) => blockedId != 0 && blockedId == t.Id && w.Time < blockedUntil;
        bool Usable(in Ship s, in Ship t)
        {
            if (!t.IsActive || t.Side == s.Side)
                return false;
            var d = Vec.Dist(s.Pos, t.Pos);
            if (d > range * jamRange + t.Class.Radius || !InArc(s, def, t.Pos))
                return false;
            // Lot 1 : l'artillerie ne tire que sur ce qu'un allié voit (repérage) ; aucune arme v0 n'en a besoin.
            return !terrain || (!RecentlyBlocked(t) && (!(beam || !s.ArcFire) || !Shadowed(s, t)));
        }
        bool Trackable(in Ship s, in Ship t) =>
            def.Kind == WeaponKind.Bomb
                ? t.Class.Radius >= BombMinRadius
                : TrackingPenalty(s, t, def, Vec.Dist(s.Pos, t.Pos)) < TrackingGiveUp;

        if (focusId != 0 && w.ShipIndex(focusId) is var f and >= 0 && Usable(s, w.Ships[f]) && Trackable(s, w.Ships[f]))
            return f;
        // Garder la cible précédente si elle reste valable (tir stable, moins de changements de cible).
        if (ws.TargetId != 0 && w.ShipIndex(ws.TargetId) is var prev and >= 0 && Usable(s, w.Ships[prev]) && Trackable(s, w.Ships[prev]))
            return prev;

        var best = -1;
        var bestScore = float.PositiveInfinity;
        var ships = w.Ships.AsSpan();
        for (var i = 0; i < ships.Length; i++)
        {
            ref readonly var o = ref ships[i];
            if (!Usable(s, o) || (def.Kind == WeaponKind.Bomb && !Trackable(s, o)))
                continue;
            var d = Vec.Dist(s.Pos, o.Pos) - o.Class.Radius;
            var score = d * (1 + TrackingPenalty(s, o, def, d + o.Class.Radius) * 20) * (terrain && Shadowed(s, o) ? 3 : 1);
            if (score < bestScore)
                (best, bestScore) = (i, score);
        }
        return best;
    }

    static void FirePointDefence(World w, int shipIndex, WeaponDef def)
    {
        ref readonly var s = ref w.Ships[shipIndex];
        var range = def.Range;
        var bestId = 0;
        var bestScore = float.PositiveInfinity;
        var bestPos = float3.Zero;
        foreach (ref readonly var p in w.Projectiles.AsSpan())
        {
            if (!p.Alive || p.Weapon is not (WeaponKind.Missile or WeaponKind.Bomb or WeaponKind.Emp or WeaponKind.Mine) || p.Side == s.Side)
                continue;
            var d = Vec.Dist(s.Pos, p.Pos);
            // Les mines sont dures à repérer : la défense ponctuelle ne les voit que de près.
            if (d > (p.Weapon == WeaponKind.Mine ? MathF.Min(range, MineSpot) : range))
                continue;
            var score = d * (p.Weapon == WeaponKind.Missile ? 0.6f : 0.3f); // bombes, puis missiles, puis drones
            if (score < bestScore)
                (bestScore, bestId, bestPos) = (score, p.Id, p.Pos);
        }
        foreach (ref readonly var dr in w.Drones.AsSpan())
        {
            if (!dr.Alive || dr.Side == s.Side)
                continue;
            var d = Vec.Dist(s.Pos, dr.Pos);
            if (d > range || d >= bestScore)
                continue;
            (bestScore, bestId, bestPos) = (d, dr.Id, dr.Pos);
        }
        if (bestId == 0)
            return;

        var hit = w.Rng.Chance(def.HitChance ?? 0.3f);
        var aimPoint = hit ? bestPos : bestPos + Vec.Scale(w.Rng.Unit(), 12);
        w.Emit(new SimEvent { Type = SimEventType.Beam, At = s.Pos, Vector = aimPoint, HasVector = true, Side = s.Side, Beam = BeamKind.Pd, OtherId = s.Id });
        if (!hit)
            return;
        var e = w.Get(bestId);
        if (e.Kind == EntityKind.Projectile)
        {
            w.DamageProjectile(ref w.Projectiles[e.Index], def.Damage);
        }
        else if (e.Kind == EntityKind.Drone)
        {
            ref var drone = ref w.Drones[e.Index];
            w.DamageDrone(ref drone, def.Damage);
            if (!drone.Alive)
                w.Stats.DronesLostToPd++;
        }
    }

    /// <summary>Dispersion supplémentaire quand la vitesse angulaire de la cible dépasse le suivi de l'arme.</summary>
    public static float TrackingPenalty(in Ship s, in Ship t, WeaponDef def, float d)
    {
        if (def.Tracking is not { } tracking || d < 1)
            return 0;
        var los = Vec.Normalize(t.Pos - s.Pos);
        var rel = t.Vel - s.Vel;
        var radial = Vec.Dot(rel, los);
        var perp = MathF.Sqrt(MathF.Max(0, Vec.Dot(rel, rel) - radial * radial));
        var angular = perp / d;
        return MathF.Max(0, angular - tracking) * 1.5f;
    }

    static float3 Jitter(World w, float3 dir, float spread)
    {
        if (spread <= 0)
            return dir;
        // Ordre des tirages du TS : le vecteur d'abord, puis l'angle.
        var axis = w.Rng.Unit();
        var angle = w.Rng.Range(0, spread);
        return Vec.Normalize(Vec.RotateAxis(dir, axis, angle));
    }
}
