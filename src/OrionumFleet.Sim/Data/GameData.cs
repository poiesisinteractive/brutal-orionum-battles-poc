using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// Chiffres du jeu, port de <c>data.ts</c> (commit 3eae78c) limité au périmètre : les 8 classes de la v0,
/// la Tanière, le Raid sur Wolf 359 et le Champ de puits. Distances en unités de sim (1 u ≈ 10 m),
/// vitesses en u/s. Les valeurs sont recopiées telles quelles ; les calculs (degrés, ×1,5 des grosses
/// coques) sont faits en double comme dans le TS, puis arrondis en float.
/// </summary>
public static class GameData
{
    static float Deg(double d) => (float)(d * Math.PI / 180);

    // --- Armes -----------------------------------------------------------------

    static readonly WeaponDef Railgun = new()
    {
        Id = WeaponId.Railgun, Kind = WeaponKind.Kinetic, Damage = 130, Rof = 1f / 8, Range = 1450, Speed = 1500,
        Arc = 10, Mount = Mount.Nose, Spread = Deg(0.25), Tracking = 0.06f,
    };

    static WeaponDef BatteryPort(float damage = 20, float range = 900) => new()
    {
        Id = WeaponId.Battery, Kind = WeaponKind.Kinetic, Damage = damage, Rof = 0.5f, Range = range, Speed = 650,
        Arc = 65, Mount = Mount.Port, Spread = Deg(0.9), Tracking = 0.16f,
    };

    static WeaponDef BatteryStarboard(float damage = 20, float range = 900) =>
        BatteryPort(damage, range) with { Mount = Mount.Starboard };

    static WeaponDef BatteryTurret(float damage = 14, float range = 800) => new()
    {
        Id = WeaponId.Battery, Kind = WeaponKind.Kinetic, Damage = damage, Rof = 0.5f, Range = range, Speed = 650,
        Arc = 180, Mount = Mount.Turret, Spread = Deg(0.9), Tracking = 0.16f,
    };

    static WeaponDef Autocannon(float damage = 6, float rof = 1.5f) => new()
    {
        Id = WeaponId.Autocannon, Kind = WeaponKind.Kinetic, Damage = damage, Rof = rof, Range = 380, Speed = 620,
        Arc = 150, Mount = Mount.Turret, Spread = Deg(1.6), Tracking = 0.8f,
    };

    static WeaponDef MissileRack(float damage, float salvo, float rof, float range = 1100) => new()
    {
        Id = WeaponId.Missiles, Kind = WeaponKind.Missile, Damage = damage, Rof = rof, Range = range, Speed = 210,
        Arc = 180, Mount = Mount.Turret, Salvo = salvo,
    };

    // Les lasers arrachent les boucliers (×1,6) mais rayent à peine le blindage (×0,6).
    static WeaponDef LaserBolt(float damage = 7, float rof = 1.5f, float range = 450) => new()
    {
        Id = WeaponId.Laser, Kind = WeaponKind.Laser, Damage = damage, Rof = rof, Range = range, Speed = 1100,
        Arc = 150, Mount = Mount.Turret, Spread = Deg(1.2), Tracking = 0.9f, VsShield = 1.6f, VsHull = 0.6f,
    };

    static WeaponDef Turbolaser(float damage = 23, float range = 950) => new()
    {
        Id = WeaponId.Turbolaser, Kind = WeaponKind.Laser, Beam = true, AimLag = 0.6f, Damage = damage, Rof = 0.5f,
        Range = range, Speed = 0, Arc = 180, Mount = Mount.Turret, Spread = Deg(0.9), Tracking = 0.16f,
        VsShield = 1.5f, VsHull = 0.7f,
    };

    // Lente, peu guidée, souffle de zone qui touche aussi les alliés ; la moitié ignore les boucliers.
    static WeaponDef Bomb(float damage, float rof, float blast, float hp, float speed = 110, float range = 1300,
        WeaponId id = WeaponId.Bomb) => new()
    {
        Id = id, Kind = WeaponKind.Bomb, Damage = damage, Rof = rof, Range = range, Speed = speed, Arc = 180,
        Mount = Mount.Turret, Blast = blast, Pierce = 0.5f, Hp = hp, Turn = 0.15f,
    };

    // Bombe à énergie : une par capital. Boucliers à zéro dans le rayon (alliés compris), sans dégâts de coque.
    static readonly WeaponDef EmpBomb = new()
    {
        Id = WeaponId.Emp, Kind = WeaponKind.Emp, Damage = 0, Rof = 0, Range = 1400, Speed = 190, Arc = 180,
        Mount = Mount.Turret, Blast = 350, Hp = 160, Turn = 0.18f,
    };

    public const float EmpShieldLock = 5;

    static WeaponDef Pd(float range = 200, float rof = 5, float damage = 2.5f, float hitChance = 0.18f) => new()
    {
        Id = WeaponId.Pd, Kind = WeaponKind.Pd, Damage = damage, Rof = rof, Range = range, Speed = 0, Arc = 180,
        Mount = Mount.Turret, HitChance = hitChance,
    };

    /// <summary>Les grosses coques (capitaux, porteurs, station) tirent 1,5 fois plus loin, avec des obus plus rapides.</summary>
    static WeaponDef Long(WeaponDef w) => w with { Range = (float)(w.Range * 1.5), Speed = (float)(w.Speed * 1.25) };

    // --- Drones ----------------------------------------------------------------

    static readonly DroneDef CoalitionDrone = new(Hp: 8, Speed: 140, Turn: 3.2f, Damage: 4, Rof: 1.2f, Range: 85);
    static readonly DroneDef ClansDrone = new(Hp: 4, Speed: 170, Turn: 4, Damage: 1.8f, Rof: 1.5f, Range: 70);
    static readonly DroneDef CoalitionInterceptor = new(Hp: 7, Speed: 190, Turn: 4.4f, Damage: 6, Rof: 1.6f, Range: 90);
    static readonly DroneDef ClansInterceptor = new(Hp: 4, Speed: 210, Turn: 5, Damage: 4, Rof: 2, Range: 80);

    static HangarBay Bay(float px, float py, float pz, float nx, float ny, float nz) =>
        new(new float3(px, py, pz), new float3(nx, ny, nz));

    // --- Classes ---------------------------------------------------------------

    /// <summary>Définitions des classes, indexées par <see cref="ShipClassId"/>.</summary>
    public static readonly ShipClassDef[] Classes =
    [
        // Coalition Solarienne : l'ordre. Peu de coques, lourdes, artillerie.
        new()
        {
            Id = ShipClassId.Bastion, Key = "bastion", Side = Side.Coalition, Role = Role.Capital,
            LabelFr = "Croiseur Bastion", LabelEn = "Bastion Cruiser",
            Hull = 3716, ShieldFace = 280, ShieldRegen = 6, SubsystemHp = 300,
            Speed = 22, Accel = 5, TurnRate = Deg(14), Rcs = 0.08f, AngAccel = 0.08f, Radius = 135,
            Weapons = [Long(Railgun), Long(Turbolaser()), Long(Turbolaser()), Pd()],
            Orionum = new OrionumModuleDef(Stable: true),
            Emp = EmpBomb,
            Hangar = new()
            {
                Stock = 12, MaxActive = 6, LaunchInterval = 3, Wave = 3, Drone = CoalitionDrone, Interceptors = 1,
                Interceptor = CoalitionInterceptor, Bays = [Bay(0, -0.2f, -0.05f, 0, -1, 0)],
            },
            PreferredRange = 1700, Engage = EngageStyle.Nose, Mobile = true, Standoff = 900,
        },
        new()
        {
            Id = ShipClassId.Egide, Key = "egide", Side = Side.Coalition, Role = Role.Carrier,
            LabelFr = "Porte-drones Égide", LabelEn = "Aegis Carrier",
            Hull = 1858, ShieldFace = 178, ShieldRegen = 5, SubsystemHp = 180,
            Speed = 30, Accel = 7, TurnRate = Deg(18), Rcs = 0.12f, AngAccel = 0.14f, Radius = 64,
            Weapons = [Long(BatteryTurret()), Long(Bomb(300, 1f / 30, 130, 9)), Pd()],
            Hangar = new()
            {
                Stock = 52, MaxActive = 24, LaunchInterval = 1, Wave = 6, Drone = CoalitionDrone, Interceptors = 0.34f,
                Interceptor = CoalitionInterceptor,
                Bays =
                [
                    Bay(0.62f, 0, 0.25f, 1, 0, 0), Bay(-0.62f, 0, 0.25f, -1, 0, 0),
                    Bay(0.62f, 0, -0.15f, 1, 0, 0), Bay(-0.62f, 0, -0.15f, -1, 0, 0),
                ],
            },
            PreferredRange = 1500, Engage = EngageStyle.Standoff, Mobile = true, Standoff = 1100,
        },
        new()
        {
            Id = ShipClassId.Hoplite, Key = "hoplite", Side = Side.Coalition, Role = Role.Brawler,
            LabelFr = "Frégate Hoplite", LabelEn = "Hoplite Frigate",
            Hull = 902, ShieldFace = 114, ShieldRegen = 4, SubsystemHp = 90,
            Speed = 70, Accel = 26, TurnRate = Deg(55), Rcs = 0.3f, AngAccel = 2.6f, Radius = 16,
            Weapons = [LaserBolt(4.8f, 1.5f, 420), LaserBolt(4.8f, 1.5f, 420)],
            PreferredRange = 300, Engage = EngageStyle.Orbit, Mobile = true,
        },
        new()
        {
            Id = ShipClassId.Vigile, Key = "vigile", Side = Side.Coalition, Role = Role.Escort,
            LabelFr = "Escorte Vigile", LabelEn = "Vigil Escort",
            Hull = 1009, ShieldFace = 141, ShieldRegen = 5, SubsystemHp = 100,
            Speed = 55, Accel = 18, TurnRate = Deg(42), Rcs = 0.22f, AngAccel = 1.2f, Radius = 18,
            Weapons = [Pd(220, 6, 3, 0.24f), Pd(220, 6, 3, 0.24f), Pd(220, 6, 3, 0.24f), Autocannon(5, 1.2f)],
            PreferredRange = 250, Engage = EngageStyle.Orbit, Mobile = true,
        },

        // Clans de Tartare : le chaos. Essaims, missiles, coques bricolées.
        new()
        {
            Id = ShipClassId.Mastodonte, Key = "mastodonte", Side = Side.Clans, Role = Role.Capital,
            LabelFr = "Minéralier armé Mastodonte", LabelEn = "Mastodon Armed Hauler",
            Hull = 3536, ShieldFace = 140, ShieldRegen = 4, SubsystemHp = 320,
            Speed = 17, Accel = 4, TurnRate = Deg(11), Rcs = 0.07f, AngAccel = 0.06f, Radius = 150,
            Weapons =
            [
                Long(BatteryPort(18, 850)), Long(BatteryPort(18, 850)),
                Long(BatteryStarboard(18, 850)), Long(BatteryStarboard(18, 850)),
                Long(Bomb(560, 1f / 18, 170, 12, 100, 1200, WeaponId.FusionBomb)), Long(MissileRack(24, 2, 0.1f)), Pd(),
            ],
            Orionum = new OrionumModuleDef(Stable: false),
            Emp = EmpBomb,
            Hangar = new()
            {
                Stock = 14, MaxActive = 7, LaunchInterval = 2.5f, Wave = 4, Drone = ClansDrone, Interceptors = 1,
                Interceptor = ClansInterceptor,
                Bays = [Bay(0.42f, -0.15f, 0.25f, 1, 0, 0), Bay(-0.42f, -0.15f, 0.25f, -1, 0, 0)],
            },
            PreferredRange = 1150, Engage = EngageStyle.Broadside, Mobile = true, Standoff = 900,
        },
        new()
        {
            Id = ShipClassId.Ruche, Key = "ruche", Side = Side.Clans, Role = Role.Carrier,
            LabelFr = "Ruche", LabelEn = "Hive",
            Hull = 1081, ShieldFace = 90, ShieldRegen = 4, SubsystemHp = 140,
            Speed = 36, Accel = 9, TurnRate = Deg(22), Rcs = 0.14f, AngAccel = 0.2f, Radius = 60,
            Weapons = [Long(MissileRack(20, 2, 0.15f, 1000)), Pd()],
            Hangar = new()
            {
                Stock = 56, MaxActive = 28, LaunchInterval = 0.7f, Wave = 7, Drone = ClansDrone, Interceptors = 0.25f,
                Interceptor = ClansInterceptor,
                Bays =
                [
                    Bay(0.72f, 0, 0.1f, 1, 0, 0), Bay(-0.72f, 0, 0.1f, -1, 0, 0),
                    Bay(0, 0.6f, 0.1f, 0, 1, 0), Bay(0, -0.6f, 0.1f, 0, -1, 0),
                ],
            },
            PreferredRange = 1500, Engage = EngageStyle.Standoff, Mobile = true, Standoff = 1100,
        },
        new()
        {
            Id = ShipClassId.Ecorcheur, Key = "ecorcheur", Side = Side.Clans, Role = Role.Brawler,
            LabelFr = "Raider Écorcheur", LabelEn = "Flayer Raider",
            Hull = 540, ShieldFace = 60, ShieldRegen = 3, SubsystemHp = 70,
            Speed = 90, Accel = 32, TurnRate = Deg(65), Rcs = 0.32f, AngAccel = 3.2f, Radius = 14,
            Weapons = [LaserBolt(8, 1.8f, 340), LaserBolt(8, 1.8f, 340), MissileRack(16, 2, 0.1f, 700)],
            PreferredRange = 280, Engage = EngageStyle.Orbit, Mobile = true,
        },
        new()
        {
            Id = ShipClassId.Grele, Key = "grele", Side = Side.Clans, Role = Role.Escort,
            LabelFr = "Escorte Grêle", LabelEn = "Hail Escort",
            Hull = 624, ShieldFace = 80, ShieldRegen = 4, SubsystemHp = 80,
            Speed = 60, Accel = 20, TurnRate = Deg(45), Rcs = 0.22f, AngAccel = 1.3f, Radius = 16,
            Weapons = [Pd(200, 5, 2.5f, 0.22f), Pd(200, 5, 2.5f, 0.22f), Autocannon()],
            PreferredRange = 250, Engage = EngageStyle.Orbit, Mobile = true,
        },
        new()
        {
            Id = ShipClassId.Depot, Key = "depot", Side = Side.Clans, Role = Role.Depot,
            LabelFr = "Dépôt d'Orionum", LabelEn = "Orionum Depot",
            Hull = 9200, ShieldFace = 300, ShieldRegen = 8, SubsystemHp = 400,
            Speed = 0, Accel = 0, TurnRate = 0, Rcs = 0, AngAccel = 0, Radius = 200,
            Weapons = [Pd(), Pd(), Pd(), BatteryTurret(20, 1220), BatteryTurret(20, 1220)],
            PreferredRange = 1350, Engage = EngageStyle.Static, Mobile = false, EmpHardened = true,
        },
    ];

    public static ShipClassDef Class(ShipClassId id) => Classes[(int)id];

    /// <summary>Chaque coque est un personnage (esprit Nexus). Noms libres dans le ton du canon.</summary>
    public static readonly string[][] HullNames =
    [
        ["Constance", "Résolue", "Souveraine", "Persévérance"],
        ["Lagrange", "Kepler", "Hubble", "Halley"],
        ["Vesta", "Europe", "Titan", "Io", "Ganymède", "Callisto", "Rhéa", "Téthys", "Dioné", "Mimas"],
        ["Olympus", "Mons", "Tharsis", "Elysium"],
        ["Vieille Dette", "Mauvaise Foi", "Ventre Creux", "Dernière Chance"],
        ["Chienne de Garde", "Marée Rouge", "Fourmilière", "Nid de Guêpes", "Essaim Noir", "Mange-Tout"],
        ["Sans-Merci", "Dernier Mot", "Coupe-Gorge", "Mâchoire", "Rapace", "Saigneur", "Croque-Mort", "Grinçant", "Feu Follet", "Ronce"],
        ["Croc", "Clou", "Épine", "Ortie"],
        ["Tanière"],
    ];

    /// <summary>Nom de la n-ième coque d'une classe, ou « Libellé n+1 » quand la liste est épuisée.</summary>
    public static string HullName(ShipClassId id, int index)
    {
        var names = HullNames[(int)id];
        return index < names.Length ? names[index] : $"{Class(id).LabelFr} {index + 1}";
    }

    /// <summary>Préréglage « Escarmouche » : exactement les classes de la v0, référence de la parité.</summary>
    public static readonly ShipClassId[] SkirmishCoalition =
    [
        ShipClassId.Bastion, ShipClassId.Egide, ShipClassId.Hoplite, ShipClassId.Hoplite, ShipClassId.Hoplite, ShipClassId.Vigile,
    ];

    public static readonly ShipClassId[] SkirmishClans =
    [
        ShipClassId.Mastodonte, ShipClassId.Ruche, ShipClassId.Ruche, ShipClassId.Ecorcheur, ShipClassId.Ecorcheur, ShipClassId.Grele,
    ];

    // --- Scénarios -------------------------------------------------------------

    static readonly ScenarioDef Raid = new()
    {
        Id = ScenarioId.Wolf359Raid, Objective = Objective.Raid, TimeLimit = 12 * 60,
        DepotPos = new float3(0, -60, 1800), CoalitionSpawn = new float3(0, 40, -2200), ClansSpawn = new float3(0, 0, 1350),
        RetreatRadius = 260,
        // Trois vestiges effondrés des anciens compagnons de Wolf 359 : ils découpent l'approche en couloirs.
        Wells =
        [
            new("Charybde", new float3(-760, 0, -150), Core: 150, Pull: 520, Reach: 1000),
            new("Scylla", new float3(780, 80, 520), Core: 110, Pull: 440, Reach: 820),
            new("Le Gué", new float3(-480, -60, 1320), Core: 80, Pull: 380, Reach: 650),
        ],
    };

    /// <summary>Carte d'essai du Terrain : le même raid, mais les puits dominent (l'un masque la Tanière depuis le sud).</summary>
    static readonly ScenarioDef WellField = Raid with
    {
        Id = ScenarioId.WellField,
        Wells =
        [
            new("Le Puits", new float3(-180, 0, -350), Core: 180, Pull: 580, Reach: 1150),
            new("Charybde", new float3(-1050, 40, 700), Core: 120, Pull: 460, Reach: 900),
            new("Scylla", new float3(1050, -40, 700), Core: 120, Pull: 460, Reach: 900),
            new("Le Voile", new float3(0, -60, 860), Core: 110, Pull: 420, Reach: 700),
        ],
    };

    public static ScenarioDef Scenario(ScenarioId id) => id switch
    {
        ScenarioId.WellField => WellField,
        _ => Raid,
    };

    // --- Réglages --------------------------------------------------------------

    /// <summary>Énergie des postures en un clic.</summary>
    public static Energy PostureEnergy(Posture posture) => posture switch
    {
        Posture.Assault => new Energy(0.6f, 0.25f, 0.15f),
        Posture.Defense => new Energy(0.2f, 0.6f, 0.2f),
        Posture.Flee => new Energy(0.1f, 0.3f, 0.6f),
        _ => new Energy(1f / 3, 1f / 3, 1f / 3),
    };

    public static class Tuning
    {
        /// <summary>Multiplicateur global des dégâts des armes et des drones (bouton de durée des batailles).</summary>
        public const float DamageScale = 0.65f;
        /// <summary>Pas fixe de la simulation (s).</summary>
        public const float Dt = 1f / 20;
        /// <summary>Cadence et profondeur des instantanés de la fenêtre rétroactive.</summary>
        public const float HistoryInterval = 0.5f;
        public const float HistoryDepth = 10;
        public const float SubsystemRestart = 0.3f;
        public const float SubsystemRepairRate = 0.01f;
        public const float SubsystemRepairCap = 0.5f;
        public const float AimedSubsystemShare = 0.8f;
        public const float RandomSubsystemChance = 0.35f;
        public const float RandomSubsystemShare = 0.5f;
        public const float DefenseFocusBonus = 0.5f;
        public const float ThreatHalfLife = 4;
        public const float DroneLeash = 650;
        public const float DroneLaunchRange = 1600;
        public const float DroneReserveRange = 650;
        public const float DroneOrphanLifetime = 12;
        public const float DroneHullShare = 0.25f;
        public const float DroneSubsystemMult = 3;
        public const float KillZone = 0.5f;
        public const float ShieldRebootMult = 6;
        public const float MissileTurn = 2.2f;
        public const float MissileHp = 1;

        /// <summary>Nombre d'instantanés gardés par coque (10 s à 0,5 s, plus l'actuel).</summary>
        public static readonly int HistorySlots = (int)Math.Round(HistoryDepth / HistoryInterval) + 1;
    }

    /// <summary>Pilier Terrain : gravité. Un puits attire tout ; ce qui atteint un cœur est avalé.</summary>
    public static class Terrain
    {
        /// <summary>Part du champ que ressent une coque, par rôle (les petites sont déportées, les grosses résistent).</summary>
        public static float? ShipFactor(Role role) => role switch
        {
            Role.Brawler or Role.Escort => 0.018f,
            Role.Carrier => 0.005f,
            Role.Capital => 0.0025f,
            Role.Depot => 0,
            _ => null,
        };

        public const float DroneFactor = 0.35f;
        public const float CoreDamage = 160;
        public const float CoreMargin = 70;
        public const float Fade = 0.75f;
        public const float ArcMaxDeg = 50;

        /// <summary>Fronde Orionum : un puits tournant sans cœur, qui relance ce qui frôle son œil.</summary>
        public static class Sling
        {
            public const float Eye = 150;
            public const float Pull = 80;
            public const float Reach = 500;
            public const float Duration = 14;
            public const float Fling = 300;
            public const float SpeedMult = 1.2f;
            public const float FlingTime = 3;
        }
    }

    /// <summary>Orionum : jamais gratuit, toujours violet, toujours gravité et temps.</summary>
    public static class Orionum
    {
        public static float Cost(OrionumAbility ability) => ability switch
        {
            OrionumAbility.Bubble => 38,
            OrionumAbility.Overclock => 5,
            OrionumAbility.Rewind => 45,
            OrionumAbility.Stasis => 35,
            OrionumAbility.Elan => 40,
            _ => 40,
        };

        public const float Decay = 1.2f;
        public const float DecayDamaged = 0.5f;
        public const float Range = 1600;

        public static class Bubble { public const float Radius = 280, Duration = 6, Scale = 0.5f; }
        public static class Elan { public const float Radius = 300, Duration = 7, Scale = 1.25f; }
        public static class Overclock { public const float Duration = 8, Scale = 1.75f; }
        public static class Rewind { public const float Seconds = 5; }
        public static class Stasis { public const float Radius = 220, Duration = 6; }
        public static class Resonance { public const float Threshold = 70, Risk = 0.18f, HullLoss = 0.3f, Offline = 30, After = 40; }
        public static class MicroStasis { public const float Radius = 180, Duration = 3.5f; }
        public static class Well { public const float Radius = 260, Duration = 3, Pull = 70, Damage = 320; }
        public static class Desync { public const float Radius = 420, Duration = 8; }

        /// <summary>Coût, puissance et risque selon le niveau du module.</summary>
        public static (float Cost, float Power, float Risk) Faction(ModuleLevel level) => level switch
        {
            ModuleLevel.Stable => (0.6f, 1f, 1f),
            ModuleLevel.Raw => (1.6f, 1.15f, 2.5f),
            _ => (0.45f, 1.08f, 0.8f),
        };
    }
}
