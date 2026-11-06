namespace OrionumFleet.Sim;

public sealed partial class World
{
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

        FleetSystem.Step(this);
        // S5 : Orionum.Step, Terrain.Step.

        for (var i = 0; i < Ships.Count; i++)
        {
            ref var s = ref Ships[i];
            if (!s.IsActive)
                continue;
            if (s.TimeScale == 0)
            {
                // Figée dans une stase : rien ne bouge, rien ne change.
                s.PrevPos = s.Pos;
                s.PrevFwd = s.Fwd;
                continue;
            }
            var ldt = Dt * s.TimeScale;
            Movement.ProcessOrders(this, ref s);
            if (s.TowedBy == 0)
                Movement.UpdateMovement(this, i, ldt);
            // S3 : updateDefences.
        }

        // Lot 1 : supportStep. Lot 3 : reinforceStep.
        // S3 : fireWeapons pour chaque coque active.
        // S4 : updateHangars, updateDrone.
        // S3 : updateProjectile, updateDying.

        Prune();
        RecordHistory(DtExact);
        CheckVictory();
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
