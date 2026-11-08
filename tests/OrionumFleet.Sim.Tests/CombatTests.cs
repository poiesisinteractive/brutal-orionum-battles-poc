using Brutal.Numerics;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Ports des tests TS « directional shields », « v0 combat rules », « lasers and bombs » et
/// « destruction and energy bomb » (sim.test.ts), sur l'Escarmouche. Le TS désarme les hangars avec
/// <c>hangar = null</c> ; ici <c>HasHangar = false</c>.
/// </summary>
public class CombatTests
{
    static int Find(World w, ShipClassId classId) =>
        Array.FindIndex(w.Ships.AsSpan().ToArray(), s => s.ClassId == classId);

    static void Isolate(World w, params int[] keep)
    {
        for (var i = 0; i < w.Ships.Count; i++)
            if (!keep.Contains(i) && w.Ships[i].ClassId != ShipClassId.Depot)
                w.Ships[i].Alive = false;
    }

    static float ShieldTotal(in Ship s)
    {
        float sum = 0;
        for (var f = 0; f < 6; f++)
            sum += s.Faces[f];
        return sum;
    }

    // --- Boucliers directionnels -------------------------------------------------

    [Fact]
    public void Maps_incoming_directions_to_the_right_face()
    {
        var w = new World(1);
        w.Ships[0].Fwd = new float3(0, 0, 1);
        ref readonly var s = ref w.Ships[0];
        Assert.Equal(Face.Front, Combat.FaceFor(s, new float3(0, 0, 1)));
        Assert.Equal(Face.Back, Combat.FaceFor(s, new float3(0, 0, -1)));
        // Nez vers +Z, haut vers +Y : le bâbord (gauche) de la coque est +X.
        Assert.Equal(Face.Port, Combat.FaceFor(s, new float3(1, 0, 0)));
        Assert.Equal(Face.Starboard, Combat.FaceFor(s, new float3(-1, 0, 0)));
        Assert.Equal(Face.Dorsal, Combat.FaceFor(s, new float3(0, 1, 0)));
        Assert.Equal(Face.Ventral, Combat.FaceFor(s, new float3(0, -1, 0)));
    }

    [Fact]
    public void Absorbs_on_the_hit_face_only_overflow_goes_to_the_hull()
    {
        var w = new World(1);
        w.Ships[0].Fwd = new float3(0, 0, 1);
        var face0 = w.Ships[0].Faces[(int)Face.Front];
        Combat.ApplyDamage(w, 0, face0 + 50, new float3(0, 0, 1), null, 0);

        ref readonly var s = ref w.Ships[0];
        Assert.Equal(0, s.Faces[(int)Face.Front]);
        Assert.Equal(face0, s.Faces[(int)Face.Back]);
        Assert.Equal(s.HullMax - 50, s.Hull);
    }

    [Fact]
    public void Defense_posture_reinforces_the_most_threatened_face()
    {
        var w = new World(1);
        w.Ships[0].Threat[(int)Face.Port] = 100;
        var basis = Combat.FaceMax(w.Ships[0], Face.Port);
        w.SetPosture(w.Ships[0].Id, Posture.Defense);

        Assert.True(Combat.FaceMax(w.Ships[0], Face.Port) > Combat.FaceMax(w.Ships[0], Face.Starboard));
        Assert.True(Combat.FaceMax(w.Ships[0], Face.Starboard) > basis);
    }

    [Fact]
    public void Aimed_fire_knocks_a_subsystem_offline()
    {
        var w = new World(1);
        ((Span<float>)w.Ships[0].Faces).Clear();
        var weapons = w.Ships[0].Subsystems[(int)Subsystem.Weapons];
        Combat.ApplyDamage(w, 0, weapons.Max / GameData.Tuning.AimedSubsystemShare + 1, new float3(0, 0, 1), Subsystem.Weapons, 0);
        Assert.True(w.Ships[0].Subsystems[(int)Subsystem.Weapons].Offline);
    }

    // --- Règles de combat v0 -----------------------------------------------------

    [Fact]
    public void Drones_slip_under_shields_and_bomb_the_aimed_subsystem()
    {
        var w = new World(1);
        var faces = ((Span<float>)w.Ships[0].Faces).ToArray();
        Combat.DroneStrike(w, 0, 10, new float3(0, 0, 1), Subsystem.Engines, 0);

        ref readonly var s = ref w.Ships[0];
        Assert.Equal(faces, ((ReadOnlySpan<float>)s.Faces).ToArray());
        var engines = s.Subsystems[(int)Subsystem.Engines];
        Assert.Equal(engines.Max - 10 * GameData.Tuning.DroneSubsystemMult, engines.Hp);
        Assert.Equal(s.HullMax - 10 * GameData.Tuning.DroneHullShare, s.Hull, 3);
    }

    [Fact]
    public void Heavy_guns_cannot_track_a_brawler_crossing_close()
    {
        var w = new World(1);
        var capital = Find(w, ShipClassId.Bastion);
        var brawler = Find(w, ShipClassId.Ecorcheur);
        var turbolaser = GameData.Class(ShipClassId.Bastion).Weapons.First(x => x.Id == WeaponId.Turbolaser);
        w.Ships[brawler].Pos = w.Ships[capital].Pos + new float3(200, 0, 0);
        w.Ships[brawler].Vel = new float3(0, 0, 90);

        Assert.True(Combat.TrackingPenalty(w.Ships[capital], w.Ships[brawler], turbolaser, 200) > 0.1f);
        Assert.Equal(0, Combat.TrackingPenalty(w.Ships[capital], w.Ships[brawler], turbolaser, 1200));
    }

    // --- Lasers et bombes --------------------------------------------------------

    [Fact]
    public void Lasers_strip_shields_faster_but_barely_scratch_the_hull()
    {
        var w = new World(1);
        var i = Find(w, ShipClassId.Bastion);
        w.Ships[i].Fwd = new float3(0, 0, 1);
        var laser = new DamageProfile(VsShield: 1.6f, VsHull: 0.6f);

        var face = w.Ships[i].Faces[(int)Face.Front];
        Combat.ApplyDamage(w, i, 10, new float3(0, 0, 1), null, 0, laser);
        Assert.Equal(16, face - w.Ships[i].Faces[(int)Face.Front], 3);

        w.Ships[i].Faces[(int)Face.Front] = 0;
        var hull = w.Ships[i].Hull;
        Combat.ApplyDamage(w, i, 10, new float3(0, 0, 1), null, 0, laser);
        Assert.Equal(6, hull - w.Ships[i].Hull, 3);
    }

    [Fact]
    public void Turbolaser_lag_hits_a_slow_capital_slips_past_a_fast_raider_crossing()
    {
        var w = new World(1);
        var bastion = Find(w, ShipClassId.Bastion);
        var weapons = GameData.Class(ShipClassId.Bastion).Weapons;
        var tl = Array.FindIndex(weapons, x => x.Id == WeaponId.Turbolaser);

        int Shoot(ShipClassId classId, float speed)
        {
            var t = Find(w, classId);
            w.Ships[t].Pos = w.Ships[bastion].Pos + new float3(600, 0, 0);
            w.Ships[t].Vel = new float3(0, 0, speed);
            w.Ships[bastion].Vel = float3.Zero;
            w.Ships[bastion].Fwd = new float3(1, 0, 0);
            var hits = 0;
            for (var n = 0; n < 40; n++)
            {
                w.DrainEvents();
                w.Ships[bastion].Orders.Set(new Order(OrderType.Attack) { TargetId = w.Ships[t].Id });
                var before = w.Ships[t].Hull + ShieldTotal(w.Ships[t]);
                // Ne tirer que ce turbolaser, une fois.
                for (var k = 0; k < w.Ships[bastion].WeaponCount; k++)
                    w.Ships[bastion].Weapons[k].Cooldown = k == tl ? 0 : 99;
                w.Ships[bastion].Weapons[tl].TargetId = w.Ships[t].Id;
                Combat.FireWeapons(w, bastion, World.Dt);
                if (w.Ships[t].Hull + ShieldTotal(w.Ships[t]) < before)
                    hits++;
            }
            return hits;
        }

        Assert.True(Shoot(ShipClassId.Mastodonte, 17) > 30);
        Assert.True(Shoot(ShipClassId.Ecorcheur, 90) < 5);
    }

    [Fact]
    public void A_bomb_blast_hits_every_hull_in_range_allies_included_and_partly_ignores_shields()
    {
        var w = new World(1);
        var egide = Find(w, ShipClassId.Egide);
        var masto = Find(w, ShipClassId.Mastodonte);
        var ally = Find(w, ShipClassId.Hoplite);
        Isolate(w, egide, masto, ally);
        w.Ships[masto].HasHangar = false; // ses intercepteurs abattraient la bombe
        w.Ships[masto].Pos = float3.Zero;
        w.Ships[masto].Vel = float3.Zero;
        w.Ships[ally].Pos = new float3(70, 0, -70); // une frégate amie trop près de la cible
        w.Ships[egide].Pos = new float3(0, 0, -3000);

        ref var bomb = ref w.SpawnProjectile(egide, WeaponKind.Bomb, new float3(0, 0, -200), new float3(0, 0, 100), 300,
            w.Ships[masto].Id, null, 30, 100);
        (bomb.Blast, bomb.Pierce, bomb.Hp, bomb.Turn) = (130, 0.5f, 9, 0.15f);
        var bombId = bomb.Id;
        var hull = w.Ships[masto].Hull;

        bool Alive() => w.ProjectileIndex(bombId) is var i and >= 0 && w.Projectiles[i].Alive;
        for (var n = 0; n < 6 / World.Dt && Alive(); n++)
            w.Step();

        Assert.False(Alive());
        Assert.Equal(1, w.Stats.BombsDetonated);
        Assert.True(w.Ships[masto].Hull < hull - 100); // la moitié perce les boucliers
        Assert.True(w.Stats.BombFriendlyDamage > 0);
    }

    [Fact]
    public void Point_defence_and_screening_drones_can_shoot_a_bomb_down()
    {
        var w = new World(1);
        var egide = Find(w, ShipClassId.Egide);
        var masto = Find(w, ShipClassId.Mastodonte);
        ref var bomb = ref w.SpawnProjectile(egide, WeaponKind.Bomb, w.Ships[masto].Pos, float3.Zero, 300, w.Ships[masto].Id, null, 30, 0);
        bomb.Hp = 9;

        w.DamageProjectile(ref bomb, 5);
        Assert.True(bomb.Alive);
        w.DamageProjectile(ref bomb, 5);
        Assert.False(bomb.Alive);
        Assert.Equal(1, w.Stats.BombsIntercepted);
    }

    // --- Destruction et bombe à énergie ------------------------------------------

    [Fact]
    public void A_killed_hull_burns_drifts_then_its_reactor_breaches_and_hurts_whoever_is_close()
    {
        var w = new World(1);
        var masto = Find(w, ShipClassId.Mastodonte);
        var ally = Find(w, ShipClassId.Grele);
        var watcher = Find(w, ShipClassId.Bastion); // empêche la bataille de finir
        Isolate(w, masto, ally, watcher);
        w.Ships[watcher].HasHangar = false;
        w.Ships[watcher].EmpAmmo = 0;
        w.Ships[watcher].WeaponCount = 0;
        w.Ships[masto].HasHangar = false;
        w.Ships[ally].HasHangar = false;
        w.Ships[ally].Pos = w.Ships[masto].Pos + new float3(120, 0, 0);

        Combat.ApplyDamage(w, masto, 1e6f, new float3(0, 0, 1), null, 0);
        Assert.True(w.Ships[masto].Alive);
        Assert.True(w.Ships[masto].Dying > 5);

        var events = new List<SimEventType>();
        var before = w.Ships[ally].Hull + ShieldTotal(w.Ships[ally]);
        for (var n = 0; n < 10 / World.Dt; n++)
        {
            w.Ships[ally].Pos = w.Ships[masto].Pos + new float3(120, 0, 0); // reste dans le danger
            w.Step();
            events.AddRange(w.DrainEvents().Select(e => e.Type));
        }

        Assert.Contains(SimEventType.Secondary, events);
        Assert.Contains(SimEventType.CoreBreach, events);
        Assert.False(w.Ships[masto].Alive);
        Assert.True(w.Ships[ally].Hull + ShieldTotal(w.Ships[ally]) < before - 100);
    }

    [Fact]
    public void An_energy_bomb_drops_every_shield_in_the_radius_allies_too_and_locks_regeneration()
    {
        var w = new World(1);
        var bastion = Find(w, ShipClassId.Bastion);
        var masto = Find(w, ShipClassId.Mastodonte);
        var hoplite = Find(w, ShipClassId.Hoplite);
        Isolate(w, bastion, masto, hoplite);
        w.Ships[bastion].HasHangar = false;
        w.Ships[masto].HasHangar = false;
        for (var k = 0; k < w.Ships[bastion].WeaponCount; k++)
            w.Ships[bastion].Weapons[k].Cooldown = 1e9f;
        for (var k = 0; k < w.Ships[masto].WeaponCount; k++)
            w.Ships[masto].Weapons[k].Cooldown = 1e9f;
        w.Ships[masto].Vel = float3.Zero;
        w.Ships[hoplite].Pos = w.Ships[masto].Pos + new float3(200, 0, 0); // une coque amie prise dedans
        w.Ships[bastion].Pos = w.Ships[masto].Pos + new float3(0, 0, -1000);

        w.OrderEmp(w.Ships[bastion].Id, w.Ships[masto].Id);
        var blast = false;
        for (var n = 0; n < 15 / World.Dt && !blast; n++)
        {
            w.Ships[hoplite].Pos = w.Ships[masto].Pos + new float3(200, 0, 0);
            w.Step();
            blast = w.DrainEvents().Exists(e => e.Type == SimEventType.EmpBlast);
        }

        Assert.True(blast);
        Assert.Equal(0, w.Ships[bastion].EmpAmmo);
        Assert.Equal(0, ShieldTotal(w.Ships[masto]));
        Assert.Equal(0, ShieldTotal(w.Ships[hoplite]));
        for (var n = 0; n < 3 / World.Dt; n++)
            w.Step();
        Assert.Equal(0, ShieldTotal(w.Ships[masto])); // toujours verrouillés
    }
}
