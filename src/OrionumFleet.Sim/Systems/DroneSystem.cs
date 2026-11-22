using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>État transitoire d'un drone entre les passes d'un pas (voir <see cref="DroneSystem.UpdateDrones"/>).</summary>
internal struct DroneFrame
{
    /// <summary>Le drone n'était pas vivant au début de la passe : rien à faire.</summary>
    public bool Skip;
    /// <summary>Orphelin depuis trop longtemps : il s'éteint (pendant la passe d'action).</summary>
    public bool Kill;
    /// <summary>Index du puits dont le cœur l'a avalé pendant le déplacement, ou -1.</summary>
    public int Core;
    /// <summary>Index du porteur, ou -1 ; et si le lien tient encore.</summary>
    public int Carrier;
    public bool Linked;
    /// <summary>Vitesse calculée à la réflexion, appliquée au déplacement (les autres lisent l'ancienne).</summary>
    public float3 Vel;
    public float Dt;
}

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
            var launchRange = def.LaunchRange ?? GameData.Tuning.DroneLaunchRange;
            var reserveRange = def.LaunchRange is { } lr ? lr * 0.4f : GameData.Tuning.DroneReserveRange;
            if (!EnemyNear(w, s, h.Mode == HangarMode.Reserve ? reserveRange : launchRange))
                continue;
            // Lancement par vagues : une grappe jaillit des baies ensemble, et vole en escadrille derrière son chef.
            var n = Math.Min(def.Wave, Math.Min(room, h.Stock));
            var interceptors = (int)Math.Floor(n * (double)def.Interceptors + 0.5); // Math.round du TS
            var wing = 0;
            for (var k = 0; k < n; k++)
            {
                ref var drone = ref w.SpawnDrone(i, k < interceptors ? DroneRole.Interceptor : DroneRole.Bomber);
                if (k == 0 || k == interceptors)
                    wing = drone.Id; // intercepteurs et chasseurs forment deux escadrilles
                drone.WingId = wing;
            }
            // SpawnDrone ne touche pas la table des coques : la référence reste valide.
            h.Stock -= n;
            h.Cooldown = def.LaunchInterval * n * (h.Mode == HangarMode.Surge ? 0.25 : 1);
        }
    }

    // Tampons des requêtes spatiales (un par site d'appel, par thread ; agrandis au besoin par la requête).
    [ThreadStatic] static int[]? t_ships;
    [ThreadStatic] static int[]? t_drones;
    [ThreadStatic] static int[]? t_rounds;

    static bool EnemyNear(World w, in Ship s, float range)
    {
        var ships = w.Ships.AsSpan();
        foreach (var i in w.ShipsNear(s.Pos, range, ref t_ships))
        {
            ref readonly var o = ref ships[i];
            if (o.IsActive && o.Side != s.Side && Vec.Dist(s.Pos, o.Pos) < range)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Tous les drones d'un pas, en trois passes (le TS les traite un par un, chacun voyant les précédents déjà
    /// déplacés ; ici chaque passe voit l'état de la passe d'avant) :
    /// <list type="number">
    /// <item>réflexion, en parallèle : orphelinage, choix de cible, cap et vitesse. Chaque drone n'écrit que ses
    /// propres champs (sauf <c>Pos</c>, <c>Vel</c> et <c>Alive</c>, que les autres lisent) ;</item>
    /// <item>déplacement, en parallèle : position et cœur de puits traversé ;</item>
    /// <item>action, en séquence dans l'ordre des index : extinction, avalement, tirs (RNG, dégâts, événements).</item>
    /// </list>
    /// Le résultat ne dépend ni du découpage ni du nombre de cœurs.
    /// </summary>
    /// <param name="dt">Pas exact en double ; chaque drone le multiplie par son échelle de temps.</param>
    public static void UpdateDrones(World w, double dt)
    {
        var count = w.Drones.Count;
        if (w.DroneFrames.Length < count)
            w.DroneFrames = new DroneFrame[Math.Max(count, w.DroneFrames.Length * 2)];
        SimJobs.For(count, (start, end) =>
        {
            for (var i = start; i < end; i++)
                Think(w, i, dt);
        });
        SimJobs.For(count, (start, end) =>
        {
            for (var i = start; i < end; i++)
                Move(w, i);
        });
        for (var i = 0; i < count; i++)
            Act(w, i, dt);
    }

    /// <summary>Passe 1 : cible, cap et vitesse voulue (sans déplacer le drone).</summary>
    static void Think(World w, int index, double worldDt)
    {
        ref var d = ref w.Drones[index];
        ref var f = ref w.DroneFrames[index];
        f = new DroneFrame { Skip = !d.Alive, Core = -1, Carrier = -1 };
        if (f.Skip)
            return;
        var dt = worldDt * d.TimeScale;
        var fdt = (float)dt;
        f.Dt = fdt;
        var ci = w.ShipIndex(d.CarrierId);
        var linked = ci >= 0 && w.Ships[ci].IsActive;
        (f.Carrier, f.Linked) = (ci, linked);
        if (!linked)
        {
            // Lien perdu : le core bridé garde son dernier comportement, puis s'éteint.
            d.OrphanTime += dt;
            if (d.OrphanTime > GameData.Tuning.DroneOrphanLifetime)
            {
                f.Kill = true;
                return;
            }
        }
        var def = w.DroneDefOf(d);
        if (linked && w.Ships[ci].Cruise)
        {
            // Le porteur file en croisière : l'essaim l'accompagne à sa vitesse.
            d.Fwd = w.Ships[ci].Fwd;
            f.Vel = w.Ships[ci].Vel;
            return;
        }

        d.RetargetIn -= dt;
        if (d.RetargetIn <= 0)
        {
            // Le TS tire ce délai du RNG global ; ici un hachage (id, pas), pour que la passe reste parallèle.
            d.RetargetIn = 0.5 + Hash01(d.Id, w.Tick) * 0.3;
            d.TargetId = PickDroneTarget(w, d, linked ? ci : -1);
        }

        var (targetAlive, target, tPos, tVel) = Target(w, d.TargetId);
        if (!targetAlive)
            d.TargetId = 0;

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
        f.Vel = Vec.Scale(d.Fwd, def.Speed * (d.Fling > 0 ? GameData.Terrain.Sling.SpeedMult : 1));
    }

    /// <summary>Passe 2 : déplacement, et cœur de puits traversé.</summary>
    static void Move(World w, int index)
    {
        ref var f = ref w.DroneFrames[index];
        if (f.Skip || f.Kill)
            return;
        ref var d = ref w.Drones[index];
        d.Vel = f.Vel;
        d.PrevPos = d.Pos;
        d.Pos += Vec.Scale(d.Vel, f.Dt);
        if (w.Wells.Count > 0)
            f.Core = Ballistics.CoreOnSegment(w.Wells.AsSpan(), d.PrevPos, d.Pos);
    }

    /// <summary>Passe 3, séquentielle : extinction, avalement, puis tir sur la cible si elle vit encore.</summary>
    static void Act(World w, int index, double worldDt)
    {
        ref var f = ref w.DroneFrames[index];
        ref var d = ref w.Drones[index];
        if (f.Skip || !d.Alive) // déjà abattu plus tôt dans cette passe
            return;
        // Escadrille : l'ailier prend la cible de son chef (lue ici, en séquence : déterministe).
        if (d.WingId != 0 && d.WingId != d.Id && w.DroneIndex(d.WingId) is var leader and >= 0 && w.Drones[leader].Alive
            && w.Drones[leader].TargetId != 0)
            d.TargetId = w.Drones[leader].TargetId;
        if (f.Kill)
        {
            w.DamageDrone(ref d, d.Hp);
            return;
        }
        if (f.Core >= 0)
        {
            // Comme le TS : avalé sans passer par le compteur des projectiles, et il peut encore tirer ce pas.
            d.Alive = false;
            w.Emit(new SimEvent { Type = SimEventType.Swallowed, At = d.Pos, OtherId = w.Wells[f.Core].Id, Value = 5 });
        }

        d.Cooldown -= worldDt * d.TimeScale;
        var (targetAlive, target, tPos, _) = Target(w, d.TargetId);
        if (d.Cooldown > 0 || !targetAlive)
            return;
        var def = w.DroneDefOf(d);
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
                    Combat.DroneStrike(w, target.Index, def.Damage * GameData.Tuning.DamageScale, fromDir, DroneAim(w, f.Carrier),
                        f.Linked ? w.Ships[f.Carrier].Id : 0);
                }
            }
        }
    }

    /// <summary>La cible d'un drone (coque, drone ou projectile) : vivante ou non, sa référence, sa position et sa vitesse.</summary>
    static (bool Alive, EntityRef Ref, float3 Pos, float3 Vel) Target(World w, int id)
    {
        var target = id != 0 ? w.Get(id) : default;
        return target.Kind switch
        {
            EntityKind.Ship when w.Ships[target.Index].IsActive => (true, target, w.Ships[target.Index].Pos, w.Ships[target.Index].Vel),
            EntityKind.Drone when w.Drones[target.Index].Alive => (true, target, w.Drones[target.Index].Pos, w.Drones[target.Index].Vel),
            EntityKind.Projectile when w.Projectiles[target.Index].Alive => (true, target, w.Projectiles[target.Index].Pos, w.Projectiles[target.Index].Vel),
            _ => (false, target, default, default),
        };
    }

    /// <summary>Nombre pseudo-aléatoire dans [0, 1) tiré de (id, pas) : déterministe et sans état partagé.</summary>
    static double Hash01(int id, int tick)
    {
        unchecked
        {
            var h = (uint)id * 0x9E3779B1u ^ (uint)tick * 0x85EBCA77u;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            h *= 0x846CA68Bu;
            h ^= h >> 16;
            return h / 4294967296.0;
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
        var leash = Leash(w, d);
        var best = 0;
        var bestScore = float.PositiveInfinity;
        var drones = w.Drones.AsSpan();
        foreach (var i in w.DronesNear(leashCenter, leash, ref t_drones))
        {
            ref readonly var o = ref drones[i];
            if (!o.Alive || o.Side == d.Side || Vec.Dist(o.Pos, leashCenter) > leash)
                continue;
            var dd = Vec.Dist(d.Pos, o.Pos);
            var score = dd * (attackMode ? 1.0f : 0.5f) * (dd < 150 ? 0.4f : 1);
            if (score < bestScore)
                (bestScore, best) = (score, o.Id);
        }
        // Les bombes en approche près du porteur passent en premier pour un écran, en second pour une escadre d'attaque.
        var home = carrier >= 0 ? w.Ships[carrier].Pos : d.Pos;
        var projectiles = w.Projectiles.AsSpan();
        foreach (var i in w.ProjectilesNear(home, leash, ref t_rounds))
        {
            ref readonly var p = ref projectiles[i];
            if (!p.Alive || p.Weapon != WeaponKind.Bomb || p.Side == d.Side || Vec.Dist(p.Pos, home) > leash)
                continue;
            var score = Vec.Dist(d.Pos, p.Pos) * (attackMode ? 0.8f : 0.25f);
            if (score < bestScore)
                (bestScore, best) = (score, p.Id);
        }
        var ships = w.Ships.AsSpan();
        foreach (var i in w.ShipsNear(leashCenter, leash + Reach.MaxShipRadius, ref t_ships))
        {
            ref readonly var s = ref ships[i];
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
        var leash = Leash(w, d);
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
        var projectiles = w.Projectiles.AsSpan();
        foreach (var i in w.ProjectilesNear(home, leash, ref t_rounds))
        {
            ref readonly var p = ref projectiles[i];
            if (p.Alive && p.Side != d.Side && p.Weapon is WeaponKind.Bomb or WeaponKind.Missile or WeaponKind.Emp)
                Consider(d, p.Id, p.Pos, p.Weapon == WeaponKind.Missile ? 0.7f : 0.3f);
        }
        var drones = w.Drones.AsSpan();
        foreach (var i in w.DronesNear(home, leash, ref t_drones))
        {
            ref readonly var o = ref drones[i];
            if (o.Alive && o.Side != d.Side)
                Consider(d, o.Id, o.Pos, 1);
        }
        return best;
    }

    /// <summary>Laisse de l'essaim : celle du hangar du porteur (capitaux, en mètres), sinon celle du proto.</summary>
    static float Leash(World w, in Drone d) =>
        w.ShipIndex(d.CarrierId) is var c and >= 0 && w.Ships[c].Class.Hangar?.Leash is { } leash ? leash : GameData.Tuning.DroneLeash;

    /// <summary>Les drones visent le sous-système que leur porteur vise ; les armes par défaut.</summary>
    static Subsystem DroneAim(World w, int carrier)
    {
        if (carrier < 0 || w.Ships[carrier].Orders.Count == 0)
            return Subsystem.Weapons;
        var o = w.Ships[carrier].Orders[0];
        return CombatRules.IsHostileOrder(o.Type) && o.Subsystem is { } aimed ? aimed : Subsystem.Weapons;
    }
}
