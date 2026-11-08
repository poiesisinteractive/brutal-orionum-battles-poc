using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Ports des tests TS « terrain: gravity wells, curved fire » (sim.test.ts) côté balistique : un Bastion et un
/// Mastodonte seuls autour d'un puits, la Tanière tenue loin du combat. Le contournement des cœurs par les
/// coques est couvert par FlightTests ; la fronde arrive avec l'Orionum (S5).
/// </summary>
public class TerrainTests
{
    sealed record Duel(World W, int Shooter, int Target);

    static Duel MakeDuel()
    {
        var w = new World(5, new WorldOptions { Pillars = new Pillars(Horloge: true, Terrain: false) });
        w.Wells.Add(new GravityWell
        {
            Id = 99, NameIndex = -1, Pos = float3.Zero, Core = 150, Pull = 520, Reach = 1000,
            Fixed = true, Ttl = float.PositiveInfinity, Total = float.PositiveInfinity,
        });
        var ships = w.Ships.AsSpan().ToArray();
        var shooter = Array.FindIndex(ships, s => s.ClassId == ShipClassId.Bastion);
        var target = Array.FindIndex(ships, s => s.ClassId == ShipClassId.Mastodonte);
        for (var i = 0; i < w.Ships.Count; i++)
        {
            ref var s = ref w.Ships[i];
            if (s.ClassId == ShipClassId.Depot)
                s.Pos = new float3(6000, 0, 6000);
            else if (i != shooter && i != target)
                s.Alive = false;
        }
        foreach (var i in (int[])[shooter, target])
        {
            ref var s = ref w.Ships[i];
            s.WeaponCount = 0;
            s.HasHangar = false;
            s.EmpAmmo = 0;
            s.Vel = float3.Zero;
        }
        return new Duel(w, shooter, target);
    }

    static float ShieldsAndHull(in Ship s)
    {
        var sum = s.Hull;
        for (var f = 0; f < 6; f++)
            sum += s.Faces[f];
        return sum;
    }

    static bool Alive(World w, int id) => w.ProjectileIndex(id) is var i and >= 0 && w.Projectiles[i].Alive;

    static float3 Pos(World w, int id) => w.Projectiles[w.ProjectileIndex(id)].Pos;

    [Fact]
    public void A_round_passing_a_well_is_bent_toward_it()
    {
        var (w, shooter, _) = MakeDuel();
        var id = w.SpawnProjectile(shooter, WeaponKind.Kinetic, new float3(-300, 0, -800), new float3(0, 0, 800), 10, 0, null, 3, 800).Id;
        for (var i = 0; i < 40; i++)
            w.Step();
        Assert.True(Pos(w, id).X > -300 + 20);
    }

    [Fact]
    public void A_core_swallows_what_reaches_it_no_hit_no_blast()
    {
        var (w, shooter, target) = MakeDuel();
        w.Ships[target].Pos = new float3(0, 0, 900);
        var before = ShieldsAndHull(w.Ships[target]);
        ref var bomb = ref w.SpawnProjectile(shooter, WeaponKind.Bomb, new float3(0, 0, -700), new float3(0, 0, 110), 400,
            w.Ships[target].Id, null, 40, 110);
        (bomb.Blast, bomb.Hp, bomb.Turn) = (150, 100, 0.15f);
        var id = bomb.Id;
        for (var i = 0; i < 30 / World.Dt && Alive(w, id); i++)
            w.Step();
        Assert.False(Alive(w, id));
        Assert.Equal(1, w.Stats.Swallowed);
        Assert.Equal(0, w.Stats.BombsDetonated);
        Assert.Equal(before, ShieldsAndHull(w.Ships[target]));
    }

    [Fact]
    public void Fire_control_compensates_the_bend_an_aimed_round_crossing_the_field_still_hits()
    {
        var (w, shooter, target) = MakeDuel();
        w.Ships[shooter].Pos = new float3(-350, 0, -900);
        w.Ships[target].Pos = new float3(-350, 0, 800);
        ref readonly var t = ref w.Ships[target];
        var aim = Ballistics.SolveAim(w, w.Ships[shooter].Pos, new RoundTarget(t.Pos, t.Vel, 150), 800, 3, false);
        Assert.True(aim.Ok);
        var before = ShieldsAndHull(w.Ships[target]);
        var id = w.SpawnProjectile(shooter, WeaponKind.Kinetic, w.Ships[shooter].Pos, Vec.Scale(aim.Dir, aim.Speed), 40, 0, null, 4, aim.Speed).Id;
        for (var i = 0; i < 80 && Alive(w, id); i++)
            w.Step();
        Assert.True(ShieldsAndHull(w.Ships[target]) < before);
    }

    [Fact]
    public void A_target_hidden_behind_a_core_is_blocked_for_straight_fire_and_reached_by_curved_fire()
    {
        var (w, shooter, target) = MakeDuel();
        w.Ships[shooter].Pos = new float3(0, 0, -1000);
        w.Ships[target].Pos = new float3(0, 0, 700);
        ref readonly var tgt = ref w.Ships[target];
        var t = new RoundTarget(tgt.Pos, tgt.Vel, 150);
        Assert.False(Ballistics.SolveAim(w, w.Ships[shooter].Pos, t, 812, 3, false).Ok);
        var arc = Ballistics.SolveAim(w, w.Ships[shooter].Pos, t, 812, 3, true);
        Assert.True(arc.Ok && arc.Arc);
        Assert.True(arc.Speed < 812);
        var before = ShieldsAndHull(w.Ships[target]);
        var id = w.SpawnProjectile(shooter, WeaponKind.Kinetic, w.Ships[shooter].Pos, Vec.Scale(arc.Dir, arc.Speed), 40, 0, null, 10, arc.Speed).Id;
        for (var i = 0; i < 220 && Alive(w, id); i++)
            w.Step();
        Assert.True(ShieldsAndHull(w.Ships[target]) < before);
    }

    [Fact]
    public void Beams_stop_at_a_core()
    {
        var (w, shooter, target) = MakeDuel();
        w.Ships[shooter].Pos = new float3(0, 0, -1000);
        w.Ships[target].Pos = new float3(0, 0, 400);
        // Le turbolaser du Bastion seul (le TS annule aussi sa dispersion ; le cœur couvre largement la ligne).
        w.Ships[shooter].WeaponCount = 1;
        w.Ships[shooter].Weapons[0] = new WeaponState { DefIndex = 1 };
        w.Issue(w.Ships[shooter].Id, new Order(OrderType.Attack) { TargetId = w.Ships[target].Id });
        var before = ShieldsAndHull(w.Ships[target]);
        var beams = 0;
        for (var i = 0; i < 40; i++)
        {
            w.Step();
            beams += w.DrainEvents().Count(e => e.Type == SimEventType.Beam && e.OtherId == w.Ships[shooter].Id);
        }
        // Comme dans le TS, la conduite de tir n'engage même pas au rayon une cible masquée par un cœur
        // (pickShipTarget / lineBlocked) : le test TS passe donc sans tir. Le rayon coupé au bord du cœur
        // (fireTurbolaser) ne sert qu'aux cibles frôlant le cœur.
        Assert.Equal(0, beams);
        Assert.Equal(before, ShieldsAndHull(w.Ships[target]));
    }
}
