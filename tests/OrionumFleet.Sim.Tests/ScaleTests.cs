namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Grandes batailles : le parallélisme (SimJobs) ne doit rien changer. Une même graine donne la même bataille,
/// que les passes tournent sur tous les cœurs ou sur un seul thread.
/// </summary>
public class ScaleTests
{
    static World Grand(int scale, uint seed)
    {
        var w = new World(seed, new WorldOptions
        {
            Coalition = Enumerable.Repeat(GameData.SkirmishCoalition, scale).SelectMany(x => x).ToArray(),
            Clans = Enumerable.Repeat(GameData.SkirmishClans, scale).SelectMany(x => x).ToArray(),
            TimeLimit = 3600,
        });
        foreach (var side in (Side[])[Side.Coalition, Side.Clans])
            foreach (var g in w.Fleet(side).Groups)
                FleetSystem.CommandGroup(w.Fleet(side), g, x => x.AutoTarget = true);
        return w;
    }

    /// <summary>Empreinte de l'état : positions et coques des coques, positions des drones, compteurs.</summary>
    static string Fingerprint(World w)
    {
        var hash = new HashCode();
        foreach (ref readonly var s in w.Ships.AsSpan())
        {
            hash.Add(s.Pos);
            hash.Add(s.Hull);
            hash.Add(s.Alive);
        }
        foreach (ref readonly var d in w.Drones.AsSpan())
        {
            hash.Add(d.Pos);
            hash.Add(d.Alive);
            hash.Add(d.TargetId);
        }
        return $"{hash.ToHashCode():X8} drones={w.Stats.DronesLaunched} frappes={w.Stats.DroneShipHits} duels={w.Stats.DroneDroneShots} rng={w.Rng.NextDouble():F12}";
    }

    [Fact]
    public void A_grand_battle_is_the_same_on_all_cores_and_on_one_thread()
    {
        string Run(bool parallel)
        {
            var previous = SimJobs.Enabled;
            SimJobs.Enabled = parallel;
            try
            {
                var w = Grand(scale: 20, seed: 3);
                var peak = 0;
                while (w.Time < 70)
                {
                    w.Step();
                    w.DrainEvents();
                    peak = Math.Max(peak, w.Drones.Count);
                }
                // La passe parallèle doit s'être enclenchée, sinon le test ne prouve rien.
                Assert.True(peak >= SimJobs.ParallelFrom, $"seulement {peak} drones");
                return Fingerprint(w);
            }
            finally
            {
                SimJobs.Enabled = previous;
            }
        }

        Assert.Equal(Run(parallel: false), Run(parallel: true));
    }
}
