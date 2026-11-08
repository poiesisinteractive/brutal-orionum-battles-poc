using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// Hangars et drones, port de <c>drones.ts</c>. Les drones sont des cores IA bridés (canon § 9.2) : ils
/// partent par vagues quand l'ennemi approche, font des passes d'attaque sur les coques (sous les boucliers,
/// sur un sous-système), chassent les drones ennemis, et les intercepteurs écrantent leur porteur contre
/// drones, missiles et bombes. Lot 1 non porté : le brouilleur.
/// </summary>
public static class DroneSystem
{
    /// <summary>Les porteurs lancent leurs drones quand l'ennemi est proche.</summary>
    /// <param name="activeByCarrier">Drones vivants par id de porteur, comptés avant les lancements de ce pas.</param>
    public static void UpdateHangars(World w, double dt, Dictionary<int, int> activeByCarrier)
    {
        for (var i = 0; i < w.Ships.Count; i++)
        {
            ref var s = ref w.Ships[i];
            if (!s.IsActive || !s.HasHangar)
                continue;
            ref var h = ref s.Hangar;
            var def = s.Class.Hangar!;
            h.Cooldown -= dt;
            if (h.Cooldown > 0 || h.Stock <= 0 || s.Subsystems[(int)Subsystem.Weapons].Offline)
                continue;
            var room = def.MaxActive - activeByCarrier.GetValueOrDefault(s.Id);
            if (room <= 0)
                continue;
            // Réserve : l'essaim reste au hangar tant que le porteur lui-même n'est pas menacé.
            if (!EnemyNear(w, s, h.Mode == HangarMode.Reserve ? GameData.Tuning.DroneReserveRange : GameData.Tuning.DroneLaunchRange))
                continue;
            // Lancement par vagues : une grappe jaillit des baies ensemble.
            var n = Math.Min(def.Wave, Math.Min(room, h.Stock));
            var interceptors = (int)Math.Floor(n * (double)def.Interceptors + 0.5); // Math.round du TS
            for (var k = 0; k < n; k++)
                w.SpawnDrone(i, k < interceptors ? DroneRole.Interceptor : DroneRole.Bomber);
            // SpawnDrone ne touche pas la table des coques : la référence reste valide.
            h.Stock -= n;
            h.Cooldown = def.LaunchInterval * n * (h.Mode == HangarMode.Surge ? 0.25 : 1);
        }
    }

    static bool EnemyNear(World w, in Ship s, float range)
    {
        foreach (ref readonly var o in w.Ships.AsSpan())
            if (o.IsActive && o.Side != s.Side && Vec.Dist(s.Pos, o.Pos) < range)
                return true;
        return false;
    }

    /// <param name="dt">Pas local en double (<see cref="World.DtExact"/> × échelle de temps) : il vide les minuteries.</param>
    public static void UpdateDrone(World w, int index, double dt)
    {
        ref var d = ref w.Drones[index];
        var fdt = (float)dt;
        var ci = w.ShipIndex(d.CarrierId);
        var linked = ci >= 0 && w.Ships[ci].IsActive;
        if (!linked)
        {
            // Lien perdu : le core bridé garde son dernier comportement, puis s'éteint.
            d.OrphanTime += dt;
            if (d.OrphanTime > GameData.Tuning.DroneOrphanLifetime)
            {
                w.DamageDrone(ref d, d.Hp);
                return;
            }
        }
        var def = w.DroneDefOf(d);

        d.RetargetIn -= dt;
        if (d.RetargetIn <= 0)
        {
            d.RetargetIn = 0.5 + w.Rng.NextDouble() * 0.3;
            d.TargetId = PickDroneTarget(w, d, linked ? ci : -1);
        }

        var target = d.TargetId != 0 ? w.Get(d.TargetId) : default;
        var targetAlive = target.Kind switch
        {
            EntityKind.Ship => w.Ships[target.Index].IsActive,
            EntityKind.Drone => w.Drones[target.Index].Alive,
            EntityKind.Projectile => w.Projectiles[target.Index].Alive,
            _ => false,
        };
        if (!targetAlive)
            d.TargetId = 0;
        var (tPos, tVel) = !targetAlive ? default
            : target.Kind == EntityKind.Ship ? (w.Ships[target.Index].Pos, w.Ships[target.Index].Vel)
            : target.Kind == EntityKind.Drone ? (w.Drones[target.Index].Pos, w.Drones[target.Index].Vel)
            : (w.Projectiles[target.Index].Pos, w.Projectiles[target.Index].Vel);

        float3 desired;
        var phase = d.Id * 2.399f; // angle d'or : l'essaim ne s'effondre pas en un point
        if (targetAlive && target.Kind == EntityKind.Ship)
        {
            // Passes d'attaque : approche en tirant, dégagement au-delà de la coque, extension, demi-tour.
            var to = tPos - d.Pos;
            var dd = Vec.Len(to);
            var r = w.Ships[target.Index].Class.Radius;
            if (!d.Extending && dd < r + def.Range * 0.15f)
                d.Extending = true;
            else if (d.Extending && dd > r + def.Range * 1.9f)
                d.Extending = false;
            if (d.Extending)
            {
                var away = Vec.Normalize(Vec.Scale(to, -1));
                var tangent = Vec.Normalize(Vec.Cross(Vec.Normalize(to), Vec.Up));
                desired = Vec.Normalize(away + Vec.Scale(tangent, MathF.Cos(phase) * 0.8f) + Vec.Scale(Vec.Up, MathF.Sin(phase) * 0.5f));
            }
            else
            {
                desired = Vec.Normalize(tPos + Vec.Scale(tVel, MathF.Min(1.5f, dd / def.Speed)) - d.Pos);
            }
        }
        else if (targetAlive)
        {
            // Duel ou interception de bombe : poursuite pure avec avance.
            var dd = Vec.Len(tPos - d.Pos);
            desired = Vec.Normalize(tPos + Vec.Scale(tVel, MathF.Min(1, dd / def.Speed)) - d.Pos);
        }
        else
        {
            // Sans cible : orbite autour du porteur (ou dérive si orphelin).
            var anchor = linked ? w.Ships[ci].Pos : d.Pos;
            var t = (float)(w.Time * 0.4) + phase;
            var r = (linked ? w.Ships[ci].Class.Radius : 0) + 90;
            var slot = anchor + new float3(MathF.Cos(t) * r, MathF.Sin(phase) * 40, MathF.Sin(t) * r);
            desired = Vec.Normalize(slot - d.Pos);
        }

        // Lot 1 : le brouillage (virage ralenti dans la bulle d'un brouilleur) n'est pas porté.
        d.Fwd = Vec.RotateToward(d.Fwd, desired, def.Turn * fdt);
        var wells = w.Wells.AsSpan();
        if (wells.Length > 0)
        {
            // Le champ courbe le cap (les drones volent dessus) ; leur pilotage les ramène. Les frondes ne
            // les courbent pas (des cores agiles les chevauchent ; elles ne font que les relancer).
            var g = Terrain.FieldAt(wells, d.Pos, false);
            d.Fwd = Vec.Normalize(Vec.Scale(d.Fwd, def.Speed) + Vec.Scale(g, GameData.Terrain.DroneFactor * fdt));
            foreach (ref readonly var well in wells)
                if (well.Eye > 0 && Vec.Dist(d.Pos, well.Pos) < well.Eye)
                    d.Fling = GameData.Terrain.Sling.FlingTime;
        }
        d.Fling = MathF.Max(0, d.Fling - fdt);
        d.Vel = Vec.Scale(d.Fwd, def.Speed * (d.Fling > 0 ? GameData.Terrain.Sling.SpeedMult : 1));
        d.PrevPos = d.Pos;
        d.Pos += Vec.Scale(d.Vel, fdt);
        if (wells.Length > 0)
        {
            var core = Ballistics.CoreOnSegment(wells, d.PrevPos, d.Pos);
            if (core >= 0)
            {
                // Comme le TS : avalé sans passer par le compteur des projectiles.
                d.Alive = false;
                w.Emit(new SimEvent { Type = SimEventType.Swallowed, At = d.Pos, OtherId = wells[core].Id, Value = 5 });
            }
        }

        d.Cooldown -= dt;
        if (d.Cooldown > 0 || !targetAlive)
            return;
        if (target.Kind == EntityKind.Projectile)
        {
            // Les drones d'écran abattent les bombes en approche (canons sur cardan, comme contre les drones).
            var to = tPos - d.Pos;
            if (Vec.Len(to) <= def.Range && Vec.Dot(d.Fwd, Vec.Normalize(to)) > 0)
            {
                d.Cooldown = 1 / (double)def.Rof;
                w.Emit(new SimEvent { Type = SimEventType.Beam, At = d.Pos, Vector = tPos, HasVector = true, Side = d.Side, Beam = BeamKind.Drone });
                if (w.Rng.Chance(0.6f))
                    w.DamageProjectile(ref w.Projectiles[target.Index], def.Damage);
            }
        }
        else
        {
            var to = tPos - d.Pos;
            var isShip = target.Kind == EntityKind.Ship;
            var reach = def.Range + (isShip ? w.Ships[target.Index].Class.Radius : 0);
            // Contre les drones, les canons légers sont sur cardan (hémisphère avant, probabiliste) ;
            // contre les coques, il faut une vraie passe d'attaque.
            var facing = Vec.Dot(d.Fwd, Vec.Normalize(to));
            if (Vec.Len(to) <= reach && facing > (isShip ? 0.4f : 0))
            {
                d.Cooldown = 1 / (double)def.Rof;
                w.Emit(new SimEvent { Type = SimEventType.Beam, At = d.Pos, Vector = tPos, HasVector = true, Side = d.Side, Beam = BeamKind.Drone });
                if (!isShip)
                {
                    w.Stats.DroneDroneShots++;
                    if (w.Rng.Chance(0.6f))
                        w.DamageDrone(ref w.Drones[target.Index], def.Damage * 0.5f);
                }
                else
                {
                    w.Stats.DroneShipHits++;
                    var fromDir = Vec.Normalize(d.Pos - tPos);
                    Combat.DroneStrike(w, target.Index, def.Damage * GameData.Tuning.DamageScale, fromDir, DroneAim(w, ci),
                        linked ? w.Ships[ci].Id : 0);
                }
            }
        }
    }

    static int PickDroneTarget(World w, in Drone d, int carrier)
    {
        if (d.Role == DroneRole.Interceptor)
            return PickInterceptTarget(w, d, carrier);
        var mode = carrier >= 0 ? w.Ships[carrier].Hangar.Mode : (HangarMode?)null;
        var attackMode = carrier >= 0 && w.Ships[carrier].HasHangar && mode is HangarMode.Attack or HangarMode.Surge;
        var leashCenter = d.Pos;
        if (carrier >= 0)
        {
            ref readonly var c = ref w.Ships[carrier];
            leashCenter = c.Pos;
            if (attackMode && c.Orders.Count > 0)
            {
                var o = c.Orders[0];
                if (CombatRules.IsHostileOrder(o.Type) && o.TargetId != 0 && w.ShipIndex(o.TargetId) is var ti and >= 0 && w.Ships[ti].IsActive)
                    leashCenter = w.Ships[ti].Pos;
            }
        }
        var leash = GameData.Tuning.DroneLeash;
        var best = 0;
        var bestScore = float.PositiveInfinity;
        foreach (ref readonly var o in w.Drones.AsSpan())
        {
            if (!o.Alive || o.Side == d.Side || Vec.Dist(o.Pos, leashCenter) > leash)
                continue;
            var dd = Vec.Dist(d.Pos, o.Pos);
            var score = dd * (attackMode ? 1.0f : 0.5f) * (dd < 150 ? 0.4f : 1);
            if (score < bestScore)
                (bestScore, best) = (score, o.Id);
        }
        // Les bombes en approche près du porteur passent en premier pour un écran, en second pour une escadre d'attaque.
        var home = carrier >= 0 ? w.Ships[carrier].Pos : d.Pos;
        foreach (ref readonly var p in w.Projectiles.AsSpan())
        {
            if (!p.Alive || p.Weapon != WeaponKind.Bomb || p.Side == d.Side || Vec.Dist(p.Pos, home) > leash)
                continue;
            var score = Vec.Dist(d.Pos, p.Pos) * (attackMode ? 0.8f : 0.25f);
            if (score < bestScore)
                (bestScore, best) = (score, p.Id);
        }
        foreach (ref readonly var s in w.Ships.AsSpan())
        {
            if (!s.IsActive || s.Side == d.Side || Vec.Dist(s.Pos, leashCenter) > leash + s.Class.Radius)
                continue;
            var score = Vec.Dist(d.Pos, s.Pos) * (attackMode ? 0.55f : 1.3f);
            if (score < bestScore)
                (bestScore, best) = (score, s.Id);
        }
        return best;
    }

    /// <summary>
    /// Les intercepteurs écrantent leur porteur : drones, missiles et bombes ennemis dans la laisse, le plus
    /// proche du porteur d'abord. Ils n'engagent jamais de coque.
    /// </summary>
    static int PickInterceptTarget(World w, in Drone d, int carrier)
    {
        var home = carrier >= 0 ? w.Ships[carrier].Pos : d.Pos;
        var leash = GameData.Tuning.DroneLeash;
        var best = 0;
        var bestScore = float.PositiveInfinity;
        void Consider(in Drone d, int id, float3 pos, float weight)
        {
            var fromHome = Vec.Dist(pos, home);
            if (fromHome > leash)
                return;
            var score = (fromHome * 0.6f + Vec.Dist(d.Pos, pos) * 0.4f) * weight;
            if (score < bestScore)
                (best, bestScore) = (id, score);
        }
        foreach (ref readonly var p in w.Projectiles.AsSpan())
            if (p.Alive && p.Side != d.Side && p.Weapon is WeaponKind.Bomb or WeaponKind.Missile or WeaponKind.Emp)
                Consider(d, p.Id, p.Pos, p.Weapon == WeaponKind.Missile ? 0.7f : 0.3f);
        foreach (ref readonly var o in w.Drones.AsSpan())
            if (o.Alive && o.Side != d.Side)
                Consider(d, o.Id, o.Pos, 1);
        return best;
    }

    /// <summary>Les drones visent le sous-système que leur porteur vise ; les armes par défaut.</summary>
    static Subsystem DroneAim(World w, int carrier)
    {
        if (carrier < 0 || w.Ships[carrier].Orders.Count == 0)
            return Subsystem.Weapons;
        var o = w.Ships[carrier].Orders[0];
        return CombatRules.IsHostileOrder(o.Type) && o.Subsystem is { } aimed ? aimed : Subsystem.Weapons;
    }
}
