using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// La bataille des capitaux (décisions du 2026-11-21), en mètres et en secondes : par camp, une frégate (~150 m),
/// un croiseur (~600 m), un cuirassé (~1,5 km), un vaisseau amiral (~3 km) et un porteur (~1 km). Les capitaux
/// portent des batteries de tourelles placées sur la coque (bordées, tourelles dorsales, tubes à missiles,
/// lances, canon axial), qu'on détruit une à une.
///
/// La Coalition est lisse et précise (boucliers forts, lances, tourelles rapides) ; les Clans sont bruts et
/// blindés (coque épaisse, boucliers faibles, obus lourds et nuées de missiles).
/// </summary>
public static class CapitalData
{
    static float Deg(double d) => (float)(d * Math.PI / 180);

    // --- Placement des tourelles ------------------------------------------------
    // Repère de la coque (+Z nez, +X bâbord, +Y dorsal), en mètres.

    /// <summary>Une rangée de <paramref name="n"/> tourelles le long de la coque, de z0 à z1.</summary>
    static float3[] Row(int n, float x, float y, float z0, float z1)
    {
        var points = new float3[n];
        for (var i = 0; i < n; i++)
            points[i] = new float3(x, y, n == 1 ? (z0 + z1) / 2 : z0 + (z1 - z0) * i / (n - 1));
        return points;
    }

    /// <summary>Tourelles de défense ponctuelle réparties sur la coque (alternance bâbord, tribord, dorsal, ventral).</summary>
    static float3[] Scatter(int n, float halfWidth, float halfHeight, float halfLength)
    {
        var points = new float3[n];
        for (var i = 0; i < n; i++)
        {
            var z = -halfLength + 2 * halfLength * (i + 0.5f) / n;
            points[i] = (i % 4) switch
            {
                0 => new float3(halfWidth, 0, z),
                1 => new float3(-halfWidth, 0, z),
                2 => new float3(0, halfHeight, z),
                _ => new float3(0, -halfHeight, z),
            };
        }
        return points;
    }

    // --- Armes --------------------------------------------------------------------

    /// <summary>Bordée : une rangée de tourelles lourdes d'un flanc (obus cinétiques).</summary>
    static WeaponDef Broadside(Mount side, float3[] hardpoints, float damage, float rof, float range, float speed, float turretHp) => new()
    {
        Id = WeaponId.HeavyTurret, Kind = WeaponKind.Kinetic, Damage = damage, Rof = rof, Range = range, Speed = speed,
        Arc = 95, Mount = side, Spread = 0.0012f, Tracking = 0.06f, Hardpoints = hardpoints, TurretHp = turretHp,
    };

    /// <summary>Tourelles dorsales à feu tous azimuts.</summary>
    static WeaponDef DorsalTurrets(float3[] hardpoints, float damage, float rof, float range, float speed, float turretHp, bool laser = false) => new()
    {
        Id = WeaponId.HeavyTurret, Kind = laser ? WeaponKind.Laser : WeaponKind.Kinetic, Damage = damage, Rof = rof, Range = range,
        Speed = speed, Arc = 180, Mount = Mount.Turret, Spread = 0.0015f, Tracking = laser ? 0.2f : 0.08f, Hardpoints = hardpoints,
        TurretHp = turretHp, VsShield = laser ? 1.5f : null, VsHull = laser ? 0.7f : null,
    };

    /// <summary>Lance : rayon continu (Damage = total d'une passe de <paramref name="duration"/> s).</summary>
    static WeaponDef Lance(float3[] hardpoints, float damage, float duration, float rof, float range, float turretHp) => new()
    {
        Id = WeaponId.Lance, Kind = WeaponKind.Laser, Beam = true, BeamDuration = duration, Damage = damage, Rof = rof,
        Range = range, Speed = 0, Arc = 180, Mount = Mount.Turret, Tracking = 0.05f, VsShield = 1.6f, VsHull = 0.9f,
        Hardpoints = hardpoints, TurretHp = turretHp,
    };

    /// <summary>Canon axial : une salve dans l'axe, il faut pointer le vaisseau entier.</summary>
    static WeaponDef Spinal(float noseZ, float damage, float rof, float range, float speed, bool kinetic) => new()
    {
        // Coalition : un rayon à particules instantané ; Clans : un obus colossal qui voyage.
        Id = WeaponId.Spinal, Kind = kinetic ? WeaponKind.Kinetic : WeaponKind.Laser, Beam = kinetic ? null : true, Damage = damage, Rof = rof, Range = range,
        Speed = speed, Arc = 2.5f, Mount = Mount.Nose, Spread = 0.0004f, Hardpoints = [new float3(0, 0, noseZ)],
        TurretHp = damage, VsShield = kinetic ? null : 1.3f, VsHull = kinetic ? null : 1f, Pierce = kinetic ? 0.35f : null,
    };

    /// <summary>Tubes à missiles : une salve de <paramref name="salvo"/> missiles, interceptables par la défense ponctuelle.</summary>
    static WeaponDef MissileBank(float3[] tubes, float salvo, float damage, float rof, float range, float speed, float turretHp) => new()
    {
        Id = WeaponId.MissileBank, Kind = WeaponKind.Missile, Damage = damage, Rof = rof, Range = range, Speed = speed,
        Arc = 180, Mount = Mount.Turret, Salvo = salvo, Turn = 0.9f, Hardpoints = tubes, TurretHp = turretHp,
    };

    static WeaponDef PointDefence(float3[] mounts, float range = 1800, float rof = 6, float damage = 3, float hitChance = 0.3f) => new()
    {
        Id = WeaponId.Pd, Kind = WeaponKind.Pd, Damage = damage, Rof = rof, Range = range, Speed = 0, Arc = 180,
        Mount = Mount.Turret, HitChance = hitChance, Hardpoints = mounts, TurretHp = 600,
    };

    static WeaponDef LightLasers(float3[] mounts, float damage, float range) => new()
    {
        Id = WeaponId.Laser, Kind = WeaponKind.Laser, Damage = damage, Rof = 0.9f, Range = range, Speed = 5000, Arc = 180,
        Mount = Mount.Turret, Spread = 0.003f, Tracking = 0.35f, VsShield = 1.4f, VsHull = 0.8f, Hardpoints = mounts, TurretHp = 500,
    };

    static WeaponDef Autocannons(float3[] mounts, float damage, float range) => new()
    {
        Id = WeaponId.Autocannon, Kind = WeaponKind.Kinetic, Damage = damage, Rof = 1.4f, Range = range, Speed = 3500, Arc = 180,
        Mount = Mount.Turret, Spread = 0.004f, Tracking = 0.4f, Hardpoints = mounts, TurretHp = 500,
    };

    // --- Escadrilles ------------------------------------------------------------------

    static readonly DroneDef Lancer = new(Hp: 70, Speed: 420, Turn: 2.4f, Damage: 110, Rof: 1, Range: 600);
    static readonly DroneDef LancerScreen = new(Hp: 60, Speed: 480, Turn: 3f, Damage: 40, Rof: 1.6f, Range: 700);
    static readonly DroneDef Wasp = new(Hp: 45, Speed: 480, Turn: 2.8f, Damage: 80, Rof: 1.3f, Range: 500);
    static readonly DroneDef WaspScreen = new(Hp: 40, Speed: 520, Turn: 3.2f, Damage: 35, Rof: 2f, Range: 650);

    static HangarDef Wings(DroneDef strike, DroneDef screen, int stock, int wing, float r) => new()
    {
        Stock = stock, MaxActive = wing * 3, LaunchInterval = 3, Wave = wing, Drone = strike, Interceptors = 0.25f,
        Interceptor = screen, LaunchRange = 40_000, Leash = 25_000,
        Bays =
        [
            new(new float3(0.55f, -0.1f, 0.2f), new float3(1, 0, 0)), new(new float3(-0.55f, -0.1f, 0.2f), new float3(-1, 0, 0)),
            new(new float3(0.55f, -0.1f, -0.2f), new float3(1, 0, 0)), new(new float3(-0.55f, -0.1f, -0.2f), new float3(-1, 0, 0)),
        ],
    };

    // --- Classes ----------------------------------------------------------------------

    /// <summary>Indexées dans l'ordre de <see cref="ShipClassId"/> à partir de <see cref="ShipClassId.Glaive"/>.</summary>
    public static readonly ShipClassDef[] Classes =
    [
        // Coalition Solarienne : coques effilées, boucliers forts, lances et tourelles rapides.
        new()
        {
            Id = ShipClassId.Glaive, Key = "glaive", Side = Side.Coalition, Role = Role.Brawler,
            LabelFr = "Frégate Glaive", LabelEn = "Glaive Frigate",
            Hull = 2600, ShieldFace = 650, ShieldRegen = 30, SubsystemHp = 500,
            Speed = 190, Accel = 45, TurnRate = Deg(32), Rcs = 0.3f, AngAccel = 0.9f, Radius = 75,
            Weapons = [LightLasers(Row(2, 0, 14, 10, 35), 70, 6000), PointDefence(Row(2, 0, -10, -20, 20), 1500)],
            PreferredRange = 3500, Engage = EngageStyle.Orbit, Mobile = true,
        },
        new()
        {
            Id = ShipClassId.Sentinelle, Key = "sentinelle", Side = Side.Coalition, Role = Role.Line,
            LabelFr = "Croiseur Sentinelle", LabelEn = "Sentinel Cruiser",
            Hull = 48_000, ShieldFace = 6000, ShieldRegen = 140, SubsystemHp = 3500,
            Speed = 95, Accel = 12, TurnRate = Deg(9), Rcs = 0.15f, AngAccel = 0.08f, Radius = 300,
            Weapons =
            [
                Broadside(Mount.Port, Row(4, 70, 20, -150, 170), 280, 0.3f, 18_000, 4000, 1600),
                Broadside(Mount.Starboard, Row(4, -70, 20, -150, 170), 280, 0.3f, 18_000, 4000, 1600),
                Lance(Row(1, 0, 60, 60, 60), 5200, 4, 1f / 16, 15_000, 2500),
                MissileBank(Row(4, 0, 45, -200, -120), 6, 160, 1f / 14, 22_000, 950, 1200),
                PointDefence(Scatter(6, 80, 55, 260)),
            ],
            PreferredRange = 14_000, Engage = EngageStyle.Broadside, Mobile = true, Standoff = 5000,
        },
        new()
        {
            Id = ShipClassId.Rempart, Key = "rempart", Side = Side.Coalition, Role = Role.Capital,
            LabelFr = "Cuirassé Rempart", LabelEn = "Rampart Battleship",
            Hull = 200_000, ShieldFace = 15_000, ShieldRegen = 300, SubsystemHp = 9000,
            Speed = 52, Accel = 5, TurnRate = Deg(4.2), Rcs = 0.1f, AngAccel = 0.02f, Radius = 750,
            Weapons =
            [
                Broadside(Mount.Port, Row(8, 170, 30, -480, 420), 420, 0.25f, 21_000, 4500, 3000),
                Broadside(Mount.Starboard, Row(8, -170, 30, -480, 420), 420, 0.25f, 21_000, 4500, 3000),
                DorsalTurrets(Row(5, 0, 150, -300, 380), 360, 0.3f, 19_000, 5000, 3000, laser: true),
                Lance(Row(2, 0, 175, 120, 260), 9500, 5, 1f / 20, 17_000, 4500),
                MissileBank(Row(6, 0, 120, -560, -420), 8, 200, 1f / 16, 24_000, 1000, 2000),
                PointDefence(Scatter(12, 190, 140, 640)),
            ],
            PreferredRange = 18_000, Engage = EngageStyle.Broadside, Mobile = true, Standoff = 8000,
        },
        new()
        {
            Id = ShipClassId.Souverain, Key = "souverain", Side = Side.Coalition, Role = Role.Capital,
            LabelFr = "Vaisseau amiral Souverain", LabelEn = "Sovereign Flagship",
            Hull = 520_000, ShieldFace = 32_000, ShieldRegen = 560, SubsystemHp = 20_000,
            Speed = 34, Accel = 2.6f, TurnRate = Deg(2.4), Rcs = 0.08f, AngAccel = 0.009f, Radius = 1500,
            Weapons =
            [
                Spinal(1450, 24_000, 1f / 28, 32_000, 0, kinetic: false),
                Broadside(Mount.Port, Row(10, 330, 50, -1000, 850), 520, 0.22f, 24_000, 5000, 4500),
                Broadside(Mount.Starboard, Row(10, -330, 50, -1000, 850), 520, 0.22f, 24_000, 5000, 4500),
                DorsalTurrets(Row(8, 0, 300, -800, 700), 420, 0.28f, 21_000, 5500, 4500, laser: true),
                Lance(Row(3, 0, 340, 150, 550), 12_000, 6, 1f / 18, 18_000, 6000),
                MissileBank(Row(12, 0, 230, -1250, -950), 12, 220, 1f / 15, 26_000, 1050, 3000),
                PointDefence(Scatter(16, 380, 280, 1300)),
            ],
            PreferredRange = 22_000, Engage = EngageStyle.Nose, Mobile = true, Standoff = 10_000,
        },
        new()
        {
            Id = ShipClassId.Firmament, Key = "firmament", Side = Side.Coalition, Role = Role.Carrier,
            LabelFr = "Porteur Firmament", LabelEn = "Firmament Carrier",
            Hull = 100_000, ShieldFace = 11_000, ShieldRegen = 220, SubsystemHp = 6000,
            Speed = 62, Accel = 6, TurnRate = Deg(5), Rcs = 0.12f, AngAccel = 0.03f, Radius = 500,
            Weapons =
            [
                DorsalTurrets(Row(4, 0, 110, -250, 250), 300, 0.3f, 18_000, 5000, 2500, laser: true),
                PointDefence(Scatter(10, 150, 100, 430)),
            ],
            Hangar = Wings(Lancer, LancerScreen, 48, 8, 500),
            PreferredRange = 24_000, Engage = EngageStyle.Standoff, Mobile = true, Standoff = 14_000,
        },

        // Clans de Tartare : masses brutes et blindées, boucliers faibles, obus lourds et nuées de missiles.
        new()
        {
            Id = ShipClassId.Saigneur, Key = "saigneur", Side = Side.Clans, Role = Role.Brawler,
            LabelFr = "Frégate Saigneur", LabelEn = "Bleeder Frigate",
            Hull = 3400, ShieldFace = 300, ShieldRegen = 18, SubsystemHp = 600,
            Speed = 210, Accel = 50, TurnRate = Deg(36), Rcs = 0.32f, AngAccel = 1f, Radius = 70,
            Weapons =
            [
                Autocannons(Row(2, 0, 12, 5, 30), 90, 4500),
                MissileBank(Row(2, 0, 10, -30, -20), 2, 140, 1f / 10, 9000, 1100, 600),
            ],
            PreferredRange = 2500, Engage = EngageStyle.Orbit, Mobile = true,
        },
        new()
        {
            Id = ShipClassId.BriseOs, Key = "briseos", Side = Side.Clans, Role = Role.Line,
            LabelFr = "Croiseur Brise-Os", LabelEn = "Bonebreaker Cruiser",
            Hull = 68_000, ShieldFace = 3000, ShieldRegen = 80, SubsystemHp = 4000,
            Speed = 85, Accel = 11, TurnRate = Deg(8), Rcs = 0.14f, AngAccel = 0.07f, Radius = 320,
            Weapons =
            [
                Broadside(Mount.Port, Row(5, 80, 10, -180, 180), 330, 0.26f, 16_000, 3500, 2000),
                Broadside(Mount.Starboard, Row(5, -80, 10, -180, 180), 330, 0.26f, 16_000, 3500, 2000),
                MissileBank(Row(6, 0, 50, -240, -100), 10, 150, 1f / 12, 21_000, 900, 1300),
                PointDefence(Scatter(4, 90, 60, 280), 1600, 5, 3, 0.26f),
            ],
            PreferredRange = 12_000, Engage = EngageStyle.Broadside, Mobile = true, Standoff = 4500,
        },
        new()
        {
            Id = ShipClassId.Behemoth, Key = "behemoth", Side = Side.Clans, Role = Role.Capital,
            LabelFr = "Cuirassé Béhémoth", LabelEn = "Behemoth Battleship",
            Hull = 270_000, ShieldFace = 7600, ShieldRegen = 160, SubsystemHp = 11_000,
            Speed = 44, Accel = 4.2f, TurnRate = Deg(3.6), Rcs = 0.09f, AngAccel = 0.017f, Radius = 800,
            Weapons =
            [
                Broadside(Mount.Port, Row(10, 200, 0, -560, 440), 520, 0.2f, 19_000, 3800, 3600),
                Broadside(Mount.Starboard, Row(10, -200, 0, -560, 440), 520, 0.2f, 19_000, 3800, 3600),
                DorsalTurrets(Row(4, 40, 170, -250, 300), 480, 0.22f, 18_000, 4000, 3600),
                MissileBank(Row(10, 0, 140, -700, -420), 16, 190, 1f / 13, 24_000, 950, 2200),
                PointDefence(Scatter(10, 210, 150, 680), 1700, 5, 3, 0.26f),
            ],
            PreferredRange = 16_000, Engage = EngageStyle.Broadside, Mobile = true, Standoff = 7000,
        },
        new()
        {
            Id = ShipClassId.Gouffre, Key = "gouffre", Side = Side.Clans, Role = Role.Capital,
            LabelFr = "Vaisseau amiral Gouffre", LabelEn = "Maw Flagship",
            Hull = 680_000, ShieldFace = 16_000, ShieldRegen = 300, SubsystemHp = 24_000,
            Speed = 30, Accel = 2.2f, TurnRate = Deg(2), Rcs = 0.07f, AngAccel = 0.007f, Radius = 1600,
            Weapons =
            [
                // Le Marteau : un obus de plusieurs tonnes, lent, qu'on voit traverser l'espace.
                Spinal(1550, 30_000, 1f / 32, 30_000, 3000, kinetic: true),
                Broadside(Mount.Port, Row(12, 380, 0, -1100, 900), 600, 0.18f, 22_000, 4200, 5000),
                Broadside(Mount.Starboard, Row(12, -380, 0, -1100, 900), 600, 0.18f, 22_000, 4200, 5000),
                DorsalTurrets(Row(6, 60, 330, -700, 600), 560, 0.2f, 20_000, 4300, 5000),
                MissileBank(Row(16, 0, 260, -1400, -900), 24, 200, 1f / 12, 26_000, 1000, 3500),
                PointDefence(Scatter(14, 420, 300, 1400), 1800, 5, 3, 0.26f),
            ],
            PreferredRange = 20_000, Engage = EngageStyle.Nose, Mobile = true, Standoff = 9000,
        },
        new()
        {
            Id = ShipClassId.RucheMere, Key = "ruchemere", Side = Side.Clans, Role = Role.Carrier,
            LabelFr = "Porteur Ruche-Mère", LabelEn = "Hive Mother Carrier",
            Hull = 120_000, ShieldFace = 5600, ShieldRegen = 120, SubsystemHp = 6500,
            Speed = 58, Accel = 5.5f, TurnRate = Deg(4.6), Rcs = 0.11f, AngAccel = 0.028f, Radius = 560,
            Weapons =
            [
                MissileBank(Row(6, 0, 120, -300, 300), 10, 160, 1f / 12, 22_000, 950, 1800),
                PointDefence(Scatter(8, 170, 110, 480), 1600, 5, 3, 0.26f),
            ],
            Hangar = Wings(Wasp, WaspScreen, 72, 12, 560),
            PreferredRange = 24_000, Engage = EngageStyle.Standoff, Mobile = true, Standoff = 13_000,
        },
    ];

    public static readonly string[][] HullNames =
    [
        ["Glaive", "Estoc", "Rapière", "Épée", "Flamberge", "Dague", "Fleuret", "Sabre", "Cimeterre", "Lame", "Fer", "Pointe"],
        ["Sentinelle", "Veilleuse", "Guet", "Vigilance", "Phare", "Garde", "Héraut", "Éclaireuse"],
        ["Rempart", "Citadelle", "Bastide", "Muraille", "Donjon", "Courtine"],
        ["Souverain", "Hégémon", "Empyrée"],
        ["Firmament", "Zénith", "Nadir"],
        ["Saigneur", "Égorgeur", "Charogne", "Vautour", "Lame Rouge", "Rouille", "Crochet", "Teigne", "Mange-Os", "Grêlon", "Sangsue", "Tique"],
        ["Brise-Os", "Fracas", "Broyeur", "Concasseur", "Cogneur", "Masse", "Écraseur", "Tonnerre"],
        ["Béhémoth", "Léviathan", "Mammouth", "Colosse", "Golgoth", "Titan Noir"],
        ["Gouffre", "Abîme", "Charnier"],
        ["Ruche-Mère", "Nid Noir", "Essaim-Roi"],
    ];

    /// <summary>
    /// La flotte type d'un camp à l'échelle <paramref name="scale"/> : un amiral, quatre cuirassés, six croiseurs,
    /// deux porteurs et douze frégates (25 coques), chaque nombre multiplié par l'échelle.
    /// </summary>
    public static ShipClassId[] Fleet(Side side, int scale)
    {
        var c = side == Side.Coalition;
        var mix = new (ShipClassId Id, int Count)[]
        {
            (c ? ShipClassId.Souverain : ShipClassId.Gouffre, 1),
            (c ? ShipClassId.Rempart : ShipClassId.Behemoth, 4),
            (c ? ShipClassId.Sentinelle : ShipClassId.BriseOs, 6),
            (c ? ShipClassId.Firmament : ShipClassId.RucheMere, 2),
            (c ? ShipClassId.Glaive : ShipClassId.Saigneur, 12),
        };
        var fleet = new List<ShipClassId>();
        foreach (var (id, count) in mix)
            for (var i = 0; i < count * Math.Max(1, scale); i++)
                fleet.Add(id);
        return [.. fleet];
    }
}
