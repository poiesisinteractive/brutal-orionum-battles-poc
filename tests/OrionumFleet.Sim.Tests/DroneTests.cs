using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Hangars et drones (S4) : le test TS « drones of a destroyed carrier shut down », puis des tests propres au
/// portage sur le lancement par vagues, la réserve, l'écran des intercepteurs et les passes d'attaque. Les
/// essaims entiers sont comparés au TS en statistiques (CombatParityTests, « raid-drones »).
/// </summary>
public class DroneTests
{
    static int Find(World w, ShipClassId classId) => Array.FindIndex(w.Ships.AsSpan().ToArray(), s => s.ClassId == classId);

    /// <summary>L'Égide (porte-drones de la Coalition) seule face au Mastodonte désarmé, la Tanière tenue loin.</summary>
    static (World W, int Carrier, int Enemy) Duel(float distance)
    {
        var w = new World(1);
        var carrier = Find(w, ShipClassId.Egide);
        var enemy = Find(w, ShipClassId.Mastodonte);
        for (var i = 0; i < w.Ships.Count; i++)
        {
            ref var s = ref w.Ships[i];
            if (s.ClassId == ShipClassId.Depot)
                s.Pos = new float3(8000, 0, 8000);
            else if (i != carrier && i != enemy)
                s.Alive = false;
        }
        foreach (var i in (int[])[carrier, enemy])
        {
            ref var s = ref w.Ships[i];
            s.WeaponCount = 0;
            s.EmpAmmo = 0;
            s.Vel = float3.Zero;
            s.Orders.Clear();
        }
        w.Ships[enemy].HasHangar = false;
        w.Ships[carrier].Pos = float3.Zero;
        w.Ships[enemy].Pos = new float3(0, 0, distance);
        return (w, carrier, enemy);
    }

    static int AliveDrones(World w) => w.Drones.AsSpan().ToArray().Count(d => d.Alive);

    [Fact]
    public void Drones_of_a_destroyed_carrier_shut_down()
    {
        var w = new World(1);
        var carrier = Array.FindIndex(w.Ships.AsSpan().ToArray(), s => s.HasHangar);
        var id = w.SpawnDrone(carrier).Id;
        w.DestroyShip(carrier, 0);
        for (var i = 0; i < (GameData.Tuning.DroneOrphanLifetime + 1) / World.Dt; i++)
            w.Step();
        Assert.False(w.DroneIndex(id) is var d and >= 0 && w.Drones[d].Alive);
    }

    [Fact]
    public void A_carrier_launches_a_wave_once_an_enemy_is_in_launch_range()
    {
        var (w, carrier, enemy) = Duel(GameData.Tuning.DroneLaunchRange + 400);
        for (var i = 0; i < 40; i++)
            w.Step();
        Assert.Equal(0, w.Stats.DronesLaunched);

        w.Ships[enemy].Pos = new float3(0, 0, GameData.Tuning.DroneLaunchRange - 400);
        w.Step();
        var hangar = GameData.Class(ShipClassId.Egide).Hangar!;
        Assert.Equal(hangar.Wave, w.Stats.DronesLaunched);
        Assert.Equal(hangar.Stock - hangar.Wave, w.Ships[carrier].Hangar.Stock);
        // Une part de la vague part en intercepteurs (Math.round du TS).
        var interceptors = w.Drones.AsSpan().ToArray().Count(d => d.Role == DroneRole.Interceptor);
        Assert.Equal((int)Math.Floor(hangar.Wave * (double)hangar.Interceptors + 0.5), interceptors);
        // La vague suivante attend le temps de lancement de toute la vague.
        Assert.Equal(hangar.LaunchInterval * hangar.Wave, w.Ships[carrier].Hangar.Cooldown, 6);
    }

    [Fact]
    public void A_carrier_in_reserve_keeps_its_swarm_until_it_is_threatened()
    {
        var (w, carrier, enemy) = Duel(GameData.Tuning.DroneLaunchRange - 400);
        w.SetHangarMode(w.Ships[carrier].Id, HangarMode.Reserve);
        for (var i = 0; i < 20; i++)
            w.Step();
        Assert.Equal(0, w.Stats.DronesLaunched);

        w.Ships[enemy].Pos = new float3(0, 0, GameData.Tuning.DroneReserveRange - 100);
        w.Step();
        Assert.True(w.Stats.DronesLaunched > 0);
    }

    [Fact]
    public void Interceptors_screen_their_carrier_against_an_incoming_bomb()
    {
        var (w, carrier, enemy) = Duel(2500);
        w.Ships[carrier].Hangar.Stock = 0; // seulement le drone posé à la main
        var droneId = w.SpawnDrone(carrier, DroneRole.Interceptor).Id;
        ref var bomb = ref w.SpawnProjectile(enemy, WeaponKind.Bomb, new float3(0, 0, 350), new float3(0, 0, -60), 300,
            w.Ships[carrier].Id, null, 30, 110);
        (bomb.Blast, bomb.Hp, bomb.Turn) = (130, 9, 0.15f);
        var bombId = bomb.Id;
        w.Step();
        Assert.Equal(bombId, w.Drones[w.DroneIndex(droneId)].TargetId);
    }

    [Fact]
    public void Bombers_make_attack_runs_under_the_shields_and_knock_out_the_weapons()
    {
        var (w, carrier, enemy) = Duel(500);
        var faces = ((ReadOnlySpan<float>)w.Ships[enemy].Faces).ToArray().Sum();
        for (var i = 0; i < 30 / World.Dt; i++)
            w.Step();
        Assert.True(AliveDrones(w) > 0);
        Assert.True(w.Stats.DroneShipHits > 0);
        // Les drones passent sous les boucliers et visent les armes par défaut.
        ref readonly var target = ref w.Ships[enemy];
        Assert.True(target.Subsystems[(int)Subsystem.Weapons].Hp < target.Subsystems[(int)Subsystem.Weapons].Max);
        Assert.Equal(faces, ((ReadOnlySpan<float>)target.Faces).ToArray().Sum(), 1);
        Assert.True(target.Hull < target.HullMax);
    }
}
