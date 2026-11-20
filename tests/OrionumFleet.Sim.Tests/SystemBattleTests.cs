using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

/// <summary>Grande bataille à l'échelle du système de Wolf 359 : départs, escadres, croisière, astres, annihilation.</summary>
public class SystemBattleTests
{
    static World Battle(int perSide, uint seed = 1)
    {
        var w = new World(seed, new WorldOptions
        {
            Scenario = ScenarioId.Wolf359System,
            Coalition = GameData.Fleet(Side.Coalition, perSide),
            Clans = GameData.Fleet(Side.Clans, perSide),
        });
        foreach (var side in (Side[])[Side.Coalition, Side.Clans])
            foreach (var g in w.Fleet(side).Groups)
                FleetSystem.CommandGroup(w.Fleet(side), g, x => x.AutoTarget = true);
        return w;
    }

    [Fact]
    public void Departures_come_from_the_seed_far_apart_and_fleets_face_each_other()
    {
        var homes = new HashSet<(float3, float3)>();
        for (uint seed = 1; seed <= 6; seed++)
        {
            var w = Battle(12, seed);
            var (a, b) = (w.Home(Side.Coalition), w.Home(Side.Clans));
            Assert.True(Vec.Dist(a, b) >= 300_000);
            homes.Add((a, b));
            var toEnemy = Vec.Normalize(new float3(b.X - a.X, 0, b.Z - a.Z));
            foreach (ref readonly var s in w.Ships.AsSpan())
                if (s.Side == Side.Coalition)
                    Assert.True(Vec.Dot(s.Fwd, toEnemy) > 0.999f);
            Assert.Equal(0, w.DepotId);
            Assert.Equal(4, w.Wells.Count);
            Assert.All(w.Wells.AsSpan().ToArray(), g => Assert.True(g.Planet && g.Pull == 0));
        }
        Assert.True(homes.Count > 1, "les départs devraient varier avec la graine");
        // Même graine, mêmes départs.
        Assert.Equal(Battle(12, 4).Home(Side.Clans), Battle(12, 4).Home(Side.Clans));
    }

    [Fact]
    public void Large_role_groups_are_split_into_mixed_squadrons()
    {
        var w = Battle(500);
        var fleet = w.Fleet(Side.Coalition);
        Assert.All(fleet.Groups, g => Assert.True(g.Members.Count <= GameData.Tuning.SquadronSize));
        Assert.Equal(500, fleet.Groups.Sum(g => g.Members.Count));
        Assert.Equal(fleet.Groups.Count, fleet.Groups.Select(g => g.Id).Distinct().Count());
        // La ligne mêle capitaux et escortes dans chaque escadre.
        var line = fleet.Groups.Where(g => g.Name.StartsWith("Ligne")).ToList();
        Assert.True(line.Count > 1);
        Assert.All(line, g => Assert.Contains(g.Members, id => w.ShipById(id).Class.Role == Role.Capital));
    }

    [Fact]
    public void Squadrons_cruise_across_hundreds_of_km_then_drop_out_to_fight()
    {
        var w = Battle(24);
        var cruising = false;
        while (w.Time < 400 && w.Stats.Hits[ShotCounters.Key(ShipClassId.Hoplite, WeaponKind.Kinetic)] == 0
               && w.Stats.DroneShipHits == 0 && w.Result is null)
        {
            w.Step();
            w.DrainEvents();
            foreach (ref readonly var s in w.Ships.AsSpan())
                cruising |= s.Cruise;
        }
        Assert.True(cruising);
        Assert.True(w.Time < 400, "les flottes devraient s'être rencontrées");
        // Une coque qui frappe ou se fait frapper n'est plus en croisière : au contact, au moins l'une est sortie.
        Assert.Contains(w.Ships.AsSpan().ToArray(), s => s.IsActive && !s.Cruise);
    }

    [Fact]
    public void A_moon_stops_a_round_that_flies_into_it()
    {
        var w = Battle(6);
        var ixion = w.Wells.AsSpan().ToArray().First(g => w.WellName(g) == "Ixion");
        var from = ixion.Pos + new float3(0, 0, -(ixion.Core + 500));
        var id = w.SpawnProjectile(0, WeaponKind.Kinetic, from, new float3(0, 0, 1000), 50, 0, null, 5, 1000).Id;
        for (var i = 0; i < 20; i++)
            w.Step();
        Assert.False(w.ProjectileIndex(id) is var p and >= 0 && w.Projectiles[p].Alive);
        Assert.Equal(1, w.Stats.Swallowed);
    }

    [Fact]
    public void The_last_fleet_standing_wins()
    {
        var w = Battle(6);
        for (var i = 0; i < w.Ships.Count; i++)
            if (w.Ships[i].Side == Side.Clans)
                w.Ships[i].Alive = false;
        w.Step();
        Assert.Equal(new BattleResult(Side.Coalition, ResultReason.FleetLost, w.Time), w.Result);
    }
}
