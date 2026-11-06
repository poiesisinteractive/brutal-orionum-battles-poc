using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

/// <summary>Ports des tests TS « fleet command » (sim.test.ts), sur l'Escarmouche.</summary>
public class FleetCommandTests
{
    /// <summary>
    /// Adapté : le TS attend 5 groupes (Ligne, Lanciers, Hangar, Artillerie, Soutien) avec la flotte
    /// « Bataille » du lot 1. L'Escarmouche n'a ni artillerie ni soutien : seuls les 3 groupes de base existent.
    /// </summary>
    [Fact]
    public void Role_based_groups_a_direct_order_detaches_commanding_the_group_reattaches()
    {
        var w = new World(1);
        var f = w.Fleet(Side.Coalition);
        Assert.Equal(["Ligne", "Lanciers", "Hangar"], f.Groups.Select(g => g.Name));

        var hop = w.Ships.AsSpan().ToArray().First(s => s.ClassId == ShipClassId.Hoplite).Id;
        var g = FleetSystem.GroupOf(f, hop)!;
        FleetSystem.CommandGroup(f, g, x => x.TargetId = w.DepotId);
        w.Issue(hop, Order.Hold);
        Assert.Contains(hop, f.Detached);

        FleetSystem.CommandGroup(f, g, x => x.Formation = Formation.Wedge);
        Assert.DoesNotContain(hop, f.Detached);
    }

    [Fact]
    public void A_battle_line_spreads_members_across_the_threat_axis_and_keeps_station()
    {
        var w = new World(1);
        var f = w.Fleet(Side.Coalition);
        var lancers = f.Groups[1];
        FleetSystem.CommandGroup(f, lancers, g => (g.Formation, g.MovePoint) = (Formation.Line, new float3(0, 40, -1500)));
        for (var i = 0; i < 20 * 40; i++)
            w.Step();

        var ships = lancers.Members.Select(id => w.ShipById(id)).ToArray();
        Assert.Equal(ships.Length - 1, ships.Count(s => s.Orders.Count > 0 && s.Orders[0].Type == OrderType.Station));
        var xs = ships.Select(s => s.Pos.X).Order().ToArray();
        Assert.True(xs[^1] - xs[0] > 150);
    }

    [Fact]
    public void Pincer_sends_two_groups_to_opposite_flanks_of_the_target()
    {
        var w = new World(1);
        var mastodonte = w.Ships.AsSpan().ToArray().First(s => s.ClassId == ShipClassId.Mastodonte).Id;
        FleetSystem.StartManeuver(w, Side.Coalition, new Maneuver { Type = ManeuverType.Pincer, TargetId = mastodonte, Groups = [1, 2] });
        w.Step();

        var groups = w.Fleet(Side.Coalition).Groups;
        Assert.NotNull(groups[0].Bearing);
        Assert.NotNull(groups[1].Bearing);
        var a = groups[0].Bearing!.Value;
        var b = groups[1].Bearing!.Value;
        Assert.True(a.X * b.X + a.Z * b.Z < 0);
    }

    /// <summary>
    /// Version S2 de « world clone replays identically » : sans IA (S6), on rejoue des ordres scriptés.
    /// Le clone doit suivre exactement la même trajectoire, bit pour bit.
    /// </summary>
    [Fact]
    public void A_clone_replays_identically()
    {
        var w = new World(5);
        var f = w.Fleet(Side.Coalition);
        FleetSystem.CommandGroup(f, f.Groups[1], g => (g.Formation, g.MovePoint) = (Formation.Wedge, new float3(200, 0, -900)));
        FleetSystem.StartManeuver(w, Side.Clans, new Maneuver { Type = ManeuverType.Pincer, TargetId = w.Ships[0].Id, Groups = [1, 2] });
        for (var i = 0; i < 20 * 30; i++)
            w.Step();

        var c = w.Clone();
        for (var i = 0; i < 400; i++)
        {
            w.Step();
            c.Step();
        }

        Assert.Equal(w.Time, c.Time);
        for (var i = 0; i < w.Ships.Count; i++)
        {
            Assert.Equal(w.Ships[i].Pos, c.Ships[i].Pos);
            Assert.Equal(w.Ships[i].Vel, c.Ships[i].Vel);
            Assert.Equal(w.Ships[i].Fwd, c.Ships[i].Fwd);
        }
    }
}
