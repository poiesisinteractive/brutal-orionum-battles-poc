using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Ports des tests TS « orders », « newtonian flight » et « standoff » (sim.test.ts). Le TS construit
/// `new World(1)` avec le préréglage « Bataille » (lot 1) ; ici c'est l'Escarmouche, seul préréglage du
/// périmètre : les coques choisies par classe sont les mêmes.
/// </summary>
public class FlightTests
{
    static int Find(World w, ShipClassId classId)
    {
        for (var i = 0; i < w.Ships.Count; i++)
            if (w.Ships[i].ClassId == classId)
                return i;
        throw new InvalidOperationException($"Pas de {classId}");
    }

    static void Isolate(World w, params int[] keep)
    {
        for (var i = 0; i < w.Ships.Count; i++)
            if (!keep.Contains(i) && w.Ships[i].ClassId != ShipClassId.Depot)
                w.Ships[i].Alive = false;
    }

    [Fact]
    public void Conditional_order_waits_behind_hold_then_preempts_when_its_trigger_fires()
    {
        var w = new World(1);
        var id = w.Ships[0].Id;
        w.Issue(id, new Order(OrderType.Retreat) { Trigger = new Trigger(TriggerType.Delay, 1) });
        Assert.Equal([OrderType.Hold, OrderType.Retreat], w.Ships[0].Orders.AsSpan().ToArray().Select(o => o.Type));

        for (var i = 0; i < 25; i++)
            w.Step();
        Assert.Equal(OrderType.Retreat, w.Ships[0].Orders[0].Type);
    }

    [Fact]
    public void Queued_move_orders_complete_in_sequence()
    {
        var w = new World(1);
        var i = Find(w, ShipClassId.Hoplite);
        var p = w.Ships[i].Pos;
        w.Issue(w.Ships[i].Id, new Order(OrderType.Move) { Point = new float3(p.X + 100, p.Y, p.Z) });
        w.Issue(w.Ships[i].Id, new Order(OrderType.Move) { Point = new float3(p.X + 100, p.Y, p.Z + 200) }, queue: true);
        Assert.Equal(2, w.Ships[i].Orders.Count);

        for (var n = 0; n < 20 * 20 && w.Ships[i].Orders.Count > 1; n++)
            w.Step();
        Assert.Equal(1, w.Ships[i].Orders.Count);
    }

    [Fact]
    public void A_capital_rotates_burns_coasts_flips_and_stops_on_its_mark_without_overshoot()
    {
        var w = new World(1);
        var i = Find(w, ShipClassId.Bastion);
        Isolate(w, i);
        var goal = w.Ships[i].Pos + new float3(1500, 0, 0);
        w.Issue(w.Ships[i].Id, new Order(OrderType.Move) { Point = goal });

        float maxSpeed = 0;
        bool coasting = false, flipped = false;
        for (var n = 0; n < 20 * 110; n++)
        {
            w.Step();
            ref readonly var s = ref w.Ships[i];
            var speed = Vec.Len(s.Vel);
            maxSpeed = MathF.Max(maxSpeed, speed);
            if (speed > 15 && s.Thrust < 0.05f)
                coasting = true;
            if (s.Fwd.X < -0.8f)
                flipped = true;
        }

        ref readonly var ship = ref w.Ships[i];
        Assert.Equal(0, ship.Orders.Count);
        Assert.True(maxSpeed <= ship.Class.Speed * 1.02f);
        Assert.True(coasting);
        Assert.True(flipped);
        Assert.True(MathF.Abs(ship.Pos.X - goal.X) < 120);
        Assert.True(Vec.Len(ship.Vel) < 1);
    }

    [Fact]
    public void Velocity_persists_without_thrust()
    {
        var w = new World(1);
        var i = Find(w, ShipClassId.Hoplite);
        Isolate(w, i);
        w.Ships[i].Vel = new float3(30, 0, 0);
        w.Ships[i].Orders.Clear();
        w.Ships[i].Pointing = Pointing.Movement;
        var x0 = w.Ships[i].Pos.X;

        // Une coque sans ordre tient (freine) : un seul pas ne déplace que d'environ vel × dt.
        w.Step();
        Assert.True(w.Ships[i].Pos.X - x0 > 30 * World.Dt * 0.9f);
    }

    [Fact]
    public void Fighting_withdrawal_backs_away_with_the_nose_on_the_target()
    {
        var w = new World(1);
        var i = Find(w, ShipClassId.Hoplite);
        var t = Find(w, ShipClassId.Ecorcheur);
        Isolate(w, i, t);
        w.Ships[t].Pos = w.Ships[i].Pos + new float3(0, 0, 150);
        w.Ships[t].Orders.Clear();
        w.Issue(w.Ships[i].Id, new Order(OrderType.Kite) { TargetId = w.Ships[t].Id });

        for (var n = 0; n < 20 * 8; n++)
            w.Step();

        var to = w.Ships[t].Pos - w.Ships[i].Pos;
        var d = Vec.Len(to);
        Assert.True(d > 150);
        Assert.True(Vec.Dot(w.Ships[i].Fwd, to) / d > 0.9f);
        Assert.True(-Vec.Dot(w.Ships[i].Vel, to) / d > 0);
    }

    [Fact]
    public void A_capital_backs_off_from_an_enemy_hull_inside_its_standoff_unless_ordered_to_close_in()
    {
        float GapAfter(bool ram)
        {
            var w = new World(1);
            var b = Find(w, ShipClassId.Bastion);
            var r = Find(w, ShipClassId.Ecorcheur);
            Isolate(w, b, r);
            w.Ships[b].HasHangar = false;
            w.Ships[r].Pos = w.Ships[b].Pos + new float3(400, 0, 0);
            w.Ships[r].WeaponCount = 0;
            w.Issue(w.Ships[b].Id, new Order(ram ? OrderType.Ram : OrderType.Attack) { TargetId = w.Ships[r].Id });
            for (var n = 0; n < 40 / World.Dt; n++)
            {
                w.Ships[r].Vel = float3.Zero;
                w.Step();
            }
            var dx = w.Ships[r].Pos.X - w.Ships[b].Pos.X;
            var dz = w.Ships[r].Pos.Z - w.Ships[b].Pos.Z;
            return MathF.Sqrt(dx * dx + dz * dz) - w.Ships[b].Class.Radius - w.Ships[r].Class.Radius;
        }

        Assert.True(GapAfter(ram: false) > 500);
        Assert.True(GapAfter(ram: true) < 250);
    }
}
