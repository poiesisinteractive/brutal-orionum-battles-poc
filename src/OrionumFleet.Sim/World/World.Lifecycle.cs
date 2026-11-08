using Brutal.Numerics;

namespace OrionumFleet.Sim;

public sealed partial class World
{
    // -------------------------------------------------------------------------
    // Cycle de vie des entités (appelé par les systèmes)

    public ref Projectile SpawnProjectile(
        int ownerIndex, WeaponKind weapon, float3 pos, float3 vel, float damage,
        int targetId, Subsystem? subsystem, float ttl, float speed)
    {
        ref var owner = ref Ships[ownerIndex];
        var projectile = new Projectile
        {
            Id = _nextId++,
            Side = owner.Side,
            Pos = pos,
            PrevPos = pos,
            Vel = vel,
            Alive = true,
            TimeScale = 1,
            Weapon = weapon,
            OwnerId = owner.Id,
            Damage = damage,
            Ttl = ttl,
            TargetId = targetId,
            Subsystem = subsystem,
            Speed = speed,
        };
        Stats.Shots[ShotCounters.Key(owner.ClassId, weapon)]++;

        var index = Projectiles.Count;
        ref var added = ref Projectiles.Add(projectile);
        _byId.Set(added.Id, new EntityRef(EntityKind.Projectile, index));
        return ref added;
    }

    /// <summary>Lance un drone d'une baie de hangar (repère de la coque : +Z nez, +X bâbord, +Y dorsal), en éventail.</summary>
    public ref Drone SpawnDrone(int carrierIndex, DroneRole role = DroneRole.Bomber)
    {
        ref var carrier = ref Ships[carrierIndex];
        var cls = carrier.Class;
        var hangar = cls.Hangar!;
        var radius = cls.Radius;
        var bay = hangar.Bays[Rng.Int(hangar.Bays.Length)];
        var frame = Vec.FrameFromForward(carrier.Fwd);
        float3 ToWorld(float3 v) => Vec.Scale(frame.Right, -v.X) + Vec.Scale(frame.Up, v.Y) + Vec.Scale(frame.Fwd, v.Z);
        var normal = ToWorld(bay.N);
        var dir = Vec.Normalize(normal + Vec.Scale(Rng.Unit(), 0.45f));
        var pos = carrier.Pos + Vec.Scale(ToWorld(bay.P), radius) + Vec.Scale(normal, 4);

        var drone = new Drone
        {
            Id = _nextId++,
            Side = carrier.Side,
            Pos = pos,
            PrevPos = pos,
            Vel = Vec.Scale(dir, 60),
            Alive = true,
            TimeScale = 1,
            CarrierId = carrier.Id,
            Hp = (role == DroneRole.Interceptor ? hangar.Interceptor : hangar.Drone).Hp,
            Cooldown = Rng.NextDouble(), // range(0, 1) du TS, gardé en double
            Fwd = dir,
            Role = role,
        };
        Stats.DronesLaunched++;

        var index = Drones.Count;
        ref var added = ref Drones.Add(drone);
        _byId.Set(added.Id, new EntityRef(EntityKind.Drone, index));
        Emit(new SimEvent { Type = SimEventType.Launch, ShipId = carrier.Id, OtherId = added.Id });
        return ref added;
    }

    public void DamageDrone(ref Drone d, float amount)
    {
        if (!d.Alive)
            return;
        d.Hp -= amount;
        if (d.Hp <= 0)
        {
            d.Alive = false;
            Emit(new SimEvent { Type = SimEventType.Explosion, At = d.Pos, Value = 5, Side = d.Side });
        }
    }

    /// <summary>Défense ponctuelle ou drone sur un missile ou une bombe (un missile meurt d'un coup).</summary>
    public void DamageProjectile(ref Projectile p, float amount)
    {
        if (!p.Alive)
            return;
        p.Hp = (p.Hp ?? 0) - amount;
        if (p.Hp > 0)
            return;
        p.Alive = false;
        if (p.Weapon == WeaponKind.Bomb)
            Stats.BombsIntercepted++;
        Emit(new SimEvent
        {
            Type = SimEventType.Explosion, At = p.Pos, Value = p.Weapon == WeaponKind.Missile ? 4 : 10, Side = p.Side,
        });
    }

    /// <summary>
    /// Coque à zéro : la coque est hors de combat mais pas encore partie. Elle dérive en vrille, en feu,
    /// avec des explosions secondaires, puis son réacteur rompt (S3 : updateDying, coreBreach).
    /// </summary>
    public void Cripple(int shipIndex, int by)
    {
        ref var s = ref Ships[shipIndex];
        if (!s.Alive || s.Dying is not null)
            return;
        var radius = s.Class.Radius;
        s.Hull = 0;
        s.Dying = s.DyingTotal = CombatRules.AgonyDuration(radius);
        s.KillerId = by;
        s.Orders.Clear();
        s.Thrust = 0;
        // Lancée dans une vrille lente ; les petites coques tournent plus vite.
        s.AngVel = Vec.Scale(Rng.Unit(), Rng.Range(0.25f, 0.6f) * MathF.Sqrt(14 / radius));
        var killer = ShipIndex(by);
        if (killer >= 0)
            Ships[killer].Stats.Kills++;
        Emit(new SimEvent { Type = SimEventType.ShipCrippled, ShipId = s.Id, At = s.Pos, OtherId = by, Value = s.Dying.Value });
    }

    /// <summary>Agonie : la coque dérive en vrille, avec des explosions secondaires de plus en plus rapprochées.</summary>
    internal void UpdateDying(int shipIndex, float dt)
    {
        ref var s = ref Ships[shipIndex];
        var radius = s.Class.Radius;
        s.PrevPos = s.Pos;
        s.PrevFwd = s.Fwd;
        s.Pos += Vec.Scale(s.Vel, dt);
        var spin = Vec.Len(s.AngVel);
        if (spin > 1e-6f)
            s.Fwd = Vec.Normalize(Vec.RotateAxis(s.Fwd, Vec.Scale(s.AngVel, 1 / spin), spin * dt));
        s.Dying -= dt;

        // Explosions secondaires en chaîne, plus fréquentes vers la fin ; certaines arrachent une pièce.
        var progress = 1 - s.Dying!.Value / s.DyingTotal;
        if (Rng.Chance(dt * (1.5f + 4 * progress)))
        {
            var at = s.Pos + Vec.Scale(Rng.Unit(), radius * Rng.Range(0.3f, 0.9f));
            var size = radius * Rng.Range(0.25f, 0.6f);
            var tear = Rng.Chance(0.3f);
            Emit(new SimEvent { Type = SimEventType.Secondary, ShipId = s.Id, At = at, Value = size, Tear = tear });
        }
        if (s.Dying <= 0)
            CoreBreach(shipIndex);
    }

    /// <summary>Le réacteur lâche : un souffle de zone qui n'épargne personne (alliés compris), puis la coque se disloque.</summary>
    void CoreBreach(int shipIndex)
    {
        ref var s = ref Ships[shipIndex];
        var radius = CombatRules.BreachRadius(s.Class.Radius);
        var damage = CombatRules.BreachDamage(s.Class.Radius);
        var pos = s.Pos;
        Emit(new SimEvent { Type = SimEventType.CoreBreach, ShipId = s.Id, At = pos, Value = radius, Side = s.Side });
        for (var i = 0; i < Ships.Count; i++)
        {
            ref readonly var o = ref Ships[i];
            if (i == shipIndex || !o.IsActive)
                continue;
            var d = MathF.Max(0, Vec.Dist(o.Pos, pos) - o.Class.Radius);
            if (d > radius)
                continue;
            var from = Vec.LenSq(pos - o.Pos) > 1e-6f ? Vec.Normalize(pos - o.Pos) : o.Fwd;
            Combat.ApplyDamage(this, i, damage * (1 - 0.75f * (d / radius)), from, null, 0, new DamageProfile(Pierce: 0.3f));
        }
        foreach (ref var d in Drones.AsSpan())
            if (d.Alive && Vec.Dist(d.Pos, pos) <= radius)
                DamageDrone(ref d, 1e3f);
        DestroyShip(shipIndex, Ships[shipIndex].KillerId);
    }

    public void DestroyShip(int shipIndex, int by)
    {
        ref var s = ref Ships[shipIndex];
        if (!s.Alive)
            return;
        var crippled = s.Dying is not null;
        s.Alive = false;
        s.Dying = null;
        s.Hull = 0;
        s.Vel = Vec.Scale(s.Vel, 0.3f);
        var killer = ShipIndex(by);
        if (killer >= 0 && !crippled)
            Ships[killer].Stats.Kills++;
        Emit(new SimEvent { Type = SimEventType.Explosion, At = s.Pos, Value = s.Class.Radius * 2.2f, Side = s.Side });
        Emit(new SimEvent { Type = SimEventType.ShipDestroyed, ShipId = s.Id, At = s.Pos, OtherId = by });
        // Lot 3 : lossRefund (rattrapage en DT) quand les renforts seront portés.
    }

    public void WithdrawShip(int shipIndex)
    {
        ref var s = ref Ships[shipIndex];
        if (s.Withdrawn)
            return;
        s.Withdrawn = true;
        // Lot 3 : saveHull (la coque sauvée rend des DT).
        Emit(new SimEvent { Type = SimEventType.Withdrawn, ShipId = s.Id });
    }

    // -------------------------------------------------------------------------
    // Entretien de fin de pas

    /// <summary>
    /// Tous les 10 pas, retire les drones et projectiles morts en gardant l'ordre de création
    /// (le <c>filter</c> du TS), puis reconstruit la table des ids. Les coques ne sont jamais retirées.
    /// </summary>
    internal void Prune()
    {
        if (Tick % 10 != 0)
            return;

        var drones = Drones.AsSpan();
        var kept = 0;
        for (var i = 0; i < drones.Length; i++)
            if (drones[i].Alive)
                drones[kept++] = drones[i];
        Drones.Truncate(kept);

        var projectiles = Projectiles.AsSpan();
        kept = 0;
        for (var i = 0; i < projectiles.Length; i++)
            if (projectiles[i].Alive)
                projectiles[kept++] = projectiles[i];
        Projectiles.Truncate(kept);

        RebuildIdMap();
    }

    void RebuildIdMap()
    {
        _byId.Clear();
        var ships = Ships.AsSpan();
        for (var i = 0; i < ships.Length; i++)
            _byId.Set(ships[i].Id, new EntityRef(EntityKind.Ship, i));
        var drones = Drones.AsSpan();
        for (var i = 0; i < drones.Length; i++)
            _byId.Set(drones[i].Id, new EntityRef(EntityKind.Drone, i));
        var projectiles = Projectiles.AsSpan();
        for (var i = 0; i < projectiles.Length; i++)
            _byId.Set(projectiles[i].Id, new EntityRef(EntityKind.Projectile, i));
    }

    /// <summary>Un instantané par coque active toutes les 0,5 s, sur 10 s (fenêtre rétroactive).</summary>
    internal void RecordHistory(double dt)
    {
        _historyClock += dt;
        if (_historyClock < GameData.Tuning.HistoryInterval)
            return;
        _historyClock -= GameData.Tuning.HistoryInterval;

        var slots = GameData.Tuning.HistorySlots;
        var ships = Ships.AsSpan();
        for (var i = 0; i < ships.Length; i++)
        {
            ref var s = ref ships[i];
            if (!s.IsActive)
                continue;

            // Anneau : on écrit à la tête, puis on avance ; au-delà de la profondeur, le plus ancien saute.
            var write = (_historyHead[i] + _historyCount[i]) % slots;
            _history[i * slots + write] = new Snapshot
            {
                Time = Time, Pos = s.Pos, Vel = s.Vel, Fwd = s.Fwd, Hull = s.Hull, Faces = s.Faces, Subsystems = s.Subsystems,
            };
            if (_historyCount[i] < slots)
                _historyCount[i]++;
            else
                _historyHead[i] = (_historyHead[i] + 1) % slots;
        }
    }

    /// <summary>Nombre d'instantanés gardés pour la coque d'index donné.</summary>
    public int HistoryCount(int shipIndex) => _historyCount[shipIndex];

    /// <summary>L'instantané n° <paramref name="age"/> de la coque, du plus ancien (0) au plus récent.</summary>
    public ref readonly Snapshot HistoryAt(int shipIndex, int age)
    {
        var slots = GameData.Tuning.HistorySlots;
        if ((uint)age >= (uint)_historyCount[shipIndex])
            throw new ArgumentOutOfRangeException(nameof(age));
        return ref _history[shipIndex * slots + (_historyHead[shipIndex] + age) % slots];
    }

    void EnsureHistoryCapacity(int shipCount)
    {
        if (_historyHead.Length >= shipCount)
            return;
        var capacity = Math.Max(shipCount, _historyHead.Length * 2);
        Array.Resize(ref _history, capacity * GameData.Tuning.HistorySlots);
        Array.Resize(ref _historyHead, capacity);
        Array.Resize(ref _historyCount, capacity);
    }

    /// <summary>Fin de bataille (raid) : Tanière détruite, flotte Coalition perdue, ou temps écoulé.</summary>
    internal void CheckVictory()
    {
        bool AliveFleet(Side side)
        {
            foreach (ref readonly var s in Ships.AsSpan())
                if (s.Side == side && s.Alive && !s.Withdrawn && s.Class.Role != Role.Convoy && s.Class.Mobile)
                    return true;
            return false;
        }

        var depot = ShipIndex(DepotId);
        if (depot >= 0 && !Ships[depot].Alive)
            Result = new BattleResult(Side.Coalition, ResultReason.DepotDestroyed, Time);
        else if (!AliveFleet(Side.Coalition))
            Result = new BattleResult(Side.Clans, ResultReason.FleetLost, Time);
        else if (Time >= TimeLimit)
            Result = new BattleResult(Side.Clans, ResultReason.Timeout, Time);
    }
}
