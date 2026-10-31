using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

public class WorldTests
{
    [Fact]
    public void A_clone_shares_no_mutable_state_and_continues_the_same_rng()
    {
        var w = new World(1);
        var c = w.Clone();

        c.Ships[0].Hull = 1;
        c.Ships[0].Faces[2] = 0;
        c.Ships[0].Orders.Push(Order.Hold);
        c.SpawnProjectile(0, WeaponKind.Kinetic, float3.Zero, float3.UnitZ, 10, 0, null, 2, 650);

        Assert.Equal(GameData.Class(ShipClassId.Bastion).Hull, w.Ships[0].Hull);
        Assert.Equal(GameData.Class(ShipClassId.Bastion).ShieldFace, w.Ships[0].Faces[2]);
        Assert.Equal(0, w.Ships[0].Orders.Count);
        Assert.Equal(0, w.Projectiles.Count);
        Assert.Equal(EntityKind.None, w.Get(c.Projectiles[0].Id).Kind);
        Assert.Equal(w.Rng.NextDouble(), c.Rng.NextDouble());
    }

    [Fact]
    public void Prune_drops_the_dead_every_10_ticks_and_keeps_ids_resolvable()
    {
        var w = new World(1);
        var ids = new List<int>();
        for (var i = 0; i < 40; i++)
            ids.Add(w.SpawnProjectile(0, WeaponKind.Kinetic, new float3(i, 0, 0), float3.UnitZ, 10, 0, null, 2, 650).Id);
        for (var i = 0; i < 40; i += 2)
            w.Projectiles[i].Alive = false;

        // Pas encore un tick multiple de 10 : rien n'est retiré.
        w.Step();
        Assert.Equal(40, w.Projectiles.Count);

        for (var t = 1; t < 10; t++)
            w.Step();
        Assert.Equal(20, w.Projectiles.Count);

        for (var i = 0; i < 40; i++)
        {
            var index = w.ProjectileIndex(ids[i]);
            if (i % 2 == 0)
                Assert.Equal(-1, index);
            else
                Assert.Equal(ids[i], w.Projectiles[index].Id);
        }
        // L'ordre de création est gardé.
        for (var i = 1; i < w.Projectiles.Count; i++)
            Assert.True(w.Projectiles[i - 1].Id < w.Projectiles[i].Id);
        // Les coques restent toutes résolubles.
        foreach (ref readonly var s in w.Ships.AsSpan())
            Assert.Equal(s.Id, w.ShipById(s.Id).Id);
    }

    /// <summary>Port de « keeps a bounded ring of snapshots per ship » (sim.test.ts).</summary>
    [Fact]
    public void History_keeps_a_bounded_ring_of_snapshots_per_ship()
    {
        var w = new World(1);
        for (var i = 0; i < 20 * 30; i++)
            w.Step();

        Assert.Equal(GameData.Tuning.HistorySlots, w.HistoryCount(0));
        var span = w.HistoryAt(0, w.HistoryCount(0) - 1).Time - w.HistoryAt(0, 0).Time;
        Assert.Equal(GameData.Tuning.HistoryDepth, span, 1);
    }

    [Fact]
    public void Cripple_starts_the_agony_and_credits_the_killer()
    {
        var w = new World(1);
        var killer = w.Ships[7].Id;
        w.Cripple(0, killer);

        ref var s = ref w.Ships[0];
        Assert.False(s.IsActive);
        Assert.True(s.Alive);
        Assert.Equal(CombatRules.AgonyDuration(s.Class.Radius), s.Dying);
        Assert.Equal(1, w.Ships[7].Stats.Kills);
        var id = s.Id;
        Assert.Contains(w.DrainEvents(), e => e.Type == SimEventType.ShipCrippled && e.ShipId == id);
    }

    [Fact]
    public void Destroying_the_depot_ends_the_raid_for_the_coalition()
    {
        var w = new World(1);
        w.DestroyShip(w.ShipIndex(w.DepotId), 0);
        w.Step();

        Assert.Equal(new BattleResult(Side.Coalition, ResultReason.DepotDestroyed, w.Time), w.Result);
    }

    [Fact]
    public void The_raid_times_out_for_the_clans()
    {
        var w = new World(1, new WorldOptions { TimeLimit = 2 });
        w.Run();

        Assert.Equal(Side.Clans, w.Result!.Value.Winner);
        Assert.Equal(ResultReason.Timeout, w.Result!.Value.Reason);
    }

    [Fact]
    public void Wells_are_placed_only_with_the_terrain_pillar()
    {
        Assert.Equal(3, new World(1).Wells.Count);
        Assert.Equal(0, new World(1, new WorldOptions { Pillars = new Pillars(Horloge: true, Terrain: false) }).Wells.Count);
    }
}
