using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

/// <summary>Bataille des capitaux (décisions du 2026-11-21) : batteries de tourelles, dégâts localisés, canon axial, lances, escadrilles.</summary>
public class CapitalTests
{
    /// <summary>Un capital de chaque camp, seuls, face à face à <paramref name="distance"/> m (hors puits, astres loin).</summary>
    static (World W, int Coalition, int Clans) Duel(ShipClassId coalition, ShipClassId clans, float distance)
    {
        var w = new World(3, new WorldOptions
        {
            Scenario = ScenarioId.Wolf359System,
            Coalition = [coalition],
            Clans = [clans],
        });
        var a = 0;
        var b = 1;
        var home = w.Home(Side.Coalition);
        w.Ships[a].Pos = home;
        w.Ships[b].Pos = home + new float3(0, 0, distance);
        w.Ships[a].Fwd = new float3(0, 0, 1);
        w.Ships[b].Fwd = new float3(0, 0, -1);
        foreach (var i in (int[])[a, b])
        {
            w.Ships[i].Vel = float3.Zero;
            w.Ships[i].Cruise = false;
        }
        return (w, a, b);
    }

    static void Attack(World w, int shooter, int target) =>
        w.Issue(w.Ships[shooter].Id, new Order(OrderType.Attack) { TargetId = w.Ships[target].Id });

    [Fact]
    public void Every_turret_of_a_broadside_fires_from_its_own_hardpoint()
    {
        var (w, a, b) = Duel(ShipClassId.Rempart, ShipClassId.Behemoth, 15_000);
        // Le Béhémoth sur le flanc bâbord du Rempart, à portée de bordée.
        w.Ships[b].Pos = w.Ships[a].Pos + new float3(15_000, 0, 0);
        var cls = w.Ships[a].Class;
        var port = Array.FindIndex(cls.Weapons, d => d.Id == WeaponId.HeavyTurret && d.Mount == Mount.Port);
        Assert.Equal(cls.Weapons[port].Hardpoints!.Length, Batteries.AliveCount(w.Ships[a].Weapons[port]));

        var origins = new HashSet<float3>();
        for (var i = 0; i < 200; i++)
        {
            w.Step();
            foreach (var e in w.DrainEvents())
                if (e.Type == SimEventType.Fire && e.OtherId == w.Ships[a].Id && e.WeaponId == WeaponId.HeavyTurret)
                    origins.Add(e.At);
        }
        // Huit tourelles de bordée bâbord, chacune à sa place (et des tourelles dorsales en plus).
        Assert.True(origins.Count >= 8, $"{origins.Count} origines de tir");
    }

    [Fact]
    public void Hits_wear_down_the_nearest_turret_until_it_is_destroyed()
    {
        var (w, a, b) = Duel(ShipClassId.Rempart, ShipClassId.Behemoth, 15_000);
        ref var s = ref w.Ships[a];
        var cls = s.Class;
        var port = Array.FindIndex(cls.Weapons, d => d.Id == WeaponId.HeavyTurret && d.Mount == Mount.Port);
        var turret = Batteries.HardpointWorld(s, cls.Weapons[port].Hardpoints![0]);
        int Alive(World w)
        {
            var n = 0;
            for (var i = 0; i < w.Ships[a].WeaponCount; i++)
                n += Batteries.AliveCount(w.Ships[a].Weapons[i]);
            return n;
        }
        var before = Alive(w);
        // Des coups sur le flanc bâbord, boucliers à plat : la coque prend tout.
        var fromDir = Vec.Normalize(turret - s.Pos);
        var impact = s.Pos + Vec.Scale(fromDir, cls.Radius);
        var destroyed = new List<SimEvent>();
        for (var i = 0; i < 20 && destroyed.Count == 0; i++)
        {
            for (var f = 0; f < 6; f++)
                w.Ships[a].Faces[f] = 0;
            Combat.ApplyDamage(w, a, 1000, fromDir, null, w.Ships[b].Id);
            destroyed.AddRange(w.DrainEvents().Where(e => e.Type == SimEventType.TurretDestroyed));
        }
        // Une seule tourelle saute, celle qui était près de l'impact (le flanc touché perd ses pièces).
        Assert.Single(destroyed);
        Assert.True(Vec.Dist(destroyed[0].At, impact) < cls.Radius * 0.5f);
        Assert.Equal(before - 1, Alive(w));
    }

    [Fact]
    public void The_spinal_cannon_fires_only_along_the_nose()
    {
        var (w, a, b) = Duel(ShipClassId.Souverain, ShipClassId.Behemoth, 25_000);
        w.Ships[a].Weapons[0].Cooldown = 0;
        // Cible plein travers : le nez n'est pas pointé, la salve ne part pas.
        w.Ships[b].Pos = w.Ships[a].Pos + new float3(25_000, 0, 0);
        w.Ships[a].WeaponCount = 1; // le canon axial seul
        w.Ships[a].AngVel = float3.Zero;
        Attack(w, a, b);
        var spinal = 0;
        for (var i = 0; i < 10; i++)
        {
            w.Step();
            spinal += w.DrainEvents().Count(e => e.Type == SimEventType.Fire && e.WeaponId == WeaponId.Spinal);
        }
        Assert.Equal(0, spinal);

        // Dans l'axe : elle part.
        w.Ships[b].Pos = w.Ships[a].Pos + Vec.Scale(w.Ships[a].Fwd, 25_000);
        for (var i = 0; i < 20 && spinal == 0; i++)
        {
            w.Step();
            spinal += w.DrainEvents().Count(e => e.Type == SimEventType.Fire && e.WeaponId == WeaponId.Spinal);
        }
        Assert.Equal(1, spinal);
    }

    [Fact]
    public void A_lance_holds_its_beam_for_seconds()
    {
        var (w, a, b) = Duel(ShipClassId.Rempart, ShipClassId.Behemoth, 12_000);
        var cls = w.Ships[a].Class;
        var lance = Array.FindIndex(cls.Weapons, d => d.Id == WeaponId.Lance);
        // Le Rempart n'a plus que ses lances.
        w.Ships[a].Weapons[0] = w.Ships[a].Weapons[lance];
        w.Ships[a].Weapons[0].Cooldown = 0;
        w.Ships[a].WeaponCount = 1;
        Attack(w, a, b);
        var beamSteps = 0;
        for (var i = 0; i < 200; i++)
        {
            w.Step();
            if (w.DrainEvents().Any(e => e.Type == SimEventType.Beam && e.Beam == BeamKind.Lance && e.OtherId == w.Ships[a].Id))
                beamSteps++;
        }
        var duration = cls.Weapons[lance].BeamDuration!.Value;
        Assert.InRange(beamSteps * World.Dt, duration - 0.2f, duration + 0.2f);
    }

    [Fact]
    public void Fighters_launch_in_wings_that_share_their_leader_target()
    {
        var (w, a, b) = Duel(ShipClassId.Firmament, ShipClassId.Behemoth, 20_000);
        for (var i = 0; i < 200; i++)
            w.Step();
        var drones = w.Drones.AsSpan().ToArray().Where(d => d.Alive).ToArray();
        Assert.NotEmpty(drones);
        foreach (var wing in drones.GroupBy(d => d.WingId))
        {
            Assert.NotEqual(0, wing.Key);
            var leader = drones.FirstOrDefault(d => d.Id == wing.Key);
            if (leader.Id == 0 || leader.TargetId == 0)
                continue;
            Assert.All(wing, d => Assert.True(d.TargetId == leader.TargetId || d.TargetId == 0));
        }
    }
}
