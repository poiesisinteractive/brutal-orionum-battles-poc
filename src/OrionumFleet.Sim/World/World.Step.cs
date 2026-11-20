using System.Diagnostics;
using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Phases d'un pas de sim, pour le chronométrage (<see cref="World.PhaseMs"/>).</summary>
public enum StepPhase
{
    Fleet,
    Movement,
    Weapons,
    Drones,
    Projectiles,
    Bookkeeping,
}

public sealed partial class World
{
    /// <summary>Tampon réutilisé à chaque pas : drones vivants par id de porteur.</summary>
    readonly Dictionary<int, int> _activeByCarrier = [];

    /// <summary>État transitoire des drones entre les passes d'un pas (DroneSystem).</summary>
    internal DroneFrame[] DroneFrames = [];

    /// <summary>État transitoire des coques entre les passes du mouvement.</summary>
    Movement.ShipFrame[] _shipFrames = [];

    /// <summary>Les passes sur les coques se répartissent dès ce nombre (plus coûteuses par entité que les drones).</summary>
    const int ShipPassFrom = 128;

    /// <summary>Durée de chaque phase au dernier pas (ms), indexée par <see cref="StepPhase"/>.</summary>
    public double[] PhaseMs { get; } = new double[Enum.GetValues<StepPhase>().Length];

    long _phaseStart;

    // Index spatiaux, reconstruits au début de chaque pas (voir EntityIndex pour les marges). Coques et drones :
    // la croisière les déplace de 150 m par pas (3 km/s).
    readonly EntityIndex _shipGrid = new(cellSize: 500, pad: 160);
    readonly EntityIndex _droneGrid = new(cellSize: 300, pad: 160);
    readonly EntityIndex _projectileGrid = new(cellSize: 300, pad: 150);
    bool _inStep;

    void BuildIndexes()
    {
        _shipGrid.Build(Ships, static s => s.Pos);
        _droneGrid.Build(Drones, static d => d.Pos);
        _projectileGrid.Build(Projectiles, static p => p.Pos);
    }

    /// <summary>
    /// Index des coques qui peuvent être à moins de <paramref name="radius"/> de <paramref name="center"/>, triés
    /// par index croissant. L'appelant refait le test exact. Hors d'un pas (tests), l'index est reconstruit.
    /// </summary>
    public ReadOnlySpan<int> ShipsNear(float3 center, float radius, ref int[]? buffer)
    {
        if (!_inStep)
            BuildIndexes();
        return _shipGrid.Query(center, radius, Ships.Count, ref buffer);
    }

    /// <summary>Comme <see cref="ShipsNear"/>, pour les drones.</summary>
    public ReadOnlySpan<int> DronesNear(float3 center, float radius, ref int[]? buffer)
    {
        if (!_inStep)
            BuildIndexes();
        return _droneGrid.Query(center, radius, Drones.Count, ref buffer);
    }

    /// <summary>Comme <see cref="ShipsNear"/>, pour les projectiles.</summary>
    public ReadOnlySpan<int> ProjectilesNear(float3 center, float radius, ref int[]? buffer)
    {
        if (!_inStep)
            BuildIndexes();
        return _projectileGrid.Query(center, radius, Projectiles.Count, ref buffer);
    }

    void EndPhase(StepPhase phase)
    {
        var now = Stopwatch.GetTimestamp();
        PhaseMs[(int)phase] = Stopwatch.GetElapsedTime(_phaseStart, now).TotalMilliseconds;
        _phaseStart = now;
    }

    /// <summary>
    /// Un pas fixe de simulation (port de <c>World.step</c>). Les phases sont appelées dans l'ordre du TS ;
    /// celles qui ne sont pas encore portées sont marquées par leur jalon (voir docs/PORTAGE.md).
    /// </summary>
    public void Step()
    {
        if (Result is not null)
            return;
        Time += DtExact;
        Tick++;
        _phaseStart = Stopwatch.GetTimestamp();
        BuildIndexes();
        _inStep = true;
        try
        {
            StepPhases();
        }
        finally
        {
            _inStep = false;
        }
    }

    void StepPhases()
    {
        FleetSystem.Step(this);
        // S5 : Orionum.Step, Terrain.Step.
        EndPhase(StepPhase.Fleet);

        // Mouvement : les ordres en séquence, puis le plan et le vol en passes parallèles (voir Movement),
        // puis les retraites (événements) en séquence.
        var shipCount = Ships.Count;
        if (_shipFrames.Length < shipCount)
            _shipFrames = new Movement.ShipFrame[Math.Max(shipCount, _shipFrames.Length * 2)];
        for (var i = 0; i < shipCount; i++)
        {
            ref var s = ref Ships[i];
            ref var f = ref _shipFrames[i];
            f = default;
            if (!s.IsActive)
                continue;
            if (s.TimeScale == 0)
            {
                // Figée dans une stase : rien ne bouge, rien ne change.
                s.PrevPos = s.Pos;
                s.PrevFwd = s.Fwd;
                continue;
            }
            f.Dt = Dt * s.TimeScale;
            Movement.ProcessOrders(this, ref s);
            f.Move = s.TowedBy == 0;
            f.Order.Type = OrderType.Hold; // remplacé par le plan pour les coques qui bougent
        }
        if (shipCount < ShipPassFrom)
        {
            // Petites flottes : l'ordre du TS, coque par coque (chacune voit les précédentes déjà déplacées).
            // Les tests de trajectoire comparent ce chemin au proto.
            for (var i = 0; i < shipCount; i++)
            {
                ref var f = ref _shipFrames[i];
                Movement.Plan(this, i, ref f);
                Movement.Fly(this, i, ref f);
                if (f.Dt > 0)
                    Combat.UpdateDefences(ref Ships[i], f.Dt);
            }
        }
        else
        {
            // Grandes flottes : chaque coque planifie en voyant les autres telles qu'au début du pas, puis toutes
            // volent. Le choix du chemin ne dépend que de la taille de la flotte : même graine, même bataille.
            SimJobs.For(shipCount, ShipPassFrom, (start, end) =>
            {
                for (var i = start; i < end; i++)
                    Movement.Plan(this, i, ref _shipFrames[i]);
            });
            SimJobs.For(shipCount, ShipPassFrom, (start, end) =>
            {
                for (var i = start; i < end; i++)
                {
                    ref var f = ref _shipFrames[i];
                    Movement.Fly(this, i, ref f);
                    if (f.Dt > 0)
                        Combat.UpdateDefences(ref Ships[i], f.Dt);
                }
            });
        }
        for (var i = 0; i < shipCount; i++)
        {
            ref readonly var f = ref _shipFrames[i];
            if (f.Move && f.Order.Type == OrderType.Retreat && Ships[i].IsActive
                && Vec.Dist(Ships[i].Pos, Home(Ships[i].Side)) < Scenario.RetreatRadius)
                WithdrawShip(i);
        }
        EndPhase(StepPhase.Movement);

        // Lot 1 : supportStep. Lot 3 : reinforceStep.
        for (var i = 0; i < Ships.Count; i++)
            if (Ships[i].IsActive)
                Combat.FireWeapons(this, i, DtExact * Ships[i].TimeScale);
        EndPhase(StepPhase.Weapons);

        _activeByCarrier.Clear();
        foreach (ref readonly var d in Drones.AsSpan())
            if (d.Alive)
                _activeByCarrier[d.CarrierId] = _activeByCarrier.GetValueOrDefault(d.CarrierId) + 1;
        DroneSystem.UpdateHangars(this, DtExact, _activeByCarrier);
        // Les drones lancés pendant ce pas volent dès ce pas, comme dans le TS (passes parallèles, voir UpdateDrones).
        DroneSystem.UpdateDrones(this, DtExact);
        EndPhase(StepPhase.Drones);

        // Les projectiles tirés pendant ce pas sont avancés dès ce pas, comme dans le TS.
        for (var i = 0; i < Projectiles.Count; i++)
            if (Projectiles[i].Alive)
                ProjectileSystem.UpdateProjectile(this, i, Dt * Projectiles[i].TimeScale);
        EndPhase(StepPhase.Projectiles);

        for (var i = 0; i < Ships.Count; i++)
            if (Ships[i].Alive && Ships[i].Dying is not null)
                UpdateDying(i, Dt * Ships[i].TimeScale);
        Prune();
        RecordHistory(DtExact);
        CheckVictory();
        EndPhase(StepPhase.Bookkeeping);
    }

    /// <summary>Avance jusqu'à la fin de la bataille ou jusqu'à <paramref name="maxTime"/>.</summary>
    public BattleResult? Run(double maxTime = double.PositiveInfinity, Action<World>? beforeStep = null)
    {
        while (Result is null && Time < maxTime)
        {
            beforeStep?.Invoke(this);
            Step();
        }
        return Result;
    }
}
