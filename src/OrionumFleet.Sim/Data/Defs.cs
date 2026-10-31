using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Classes de coques du périmètre (v0 + la Tanière). L'ordre sert d'index dans <see cref="GameData.Classes"/>.</summary>
public enum ShipClassId : byte
{
    Bastion,
    Egide,
    Hoplite,
    Vigile,
    Mastodonte,
    Ruche,
    Ecorcheur,
    Grele,
    Depot,
}

// Données statiques (immuables, partagées par tous les mondes et leurs clones). Ce sont des classes :
// elles ne sont pas copiées par entité. Les champs optionnels du TS (`salvo?`, `spread?`...) restent
// nullables pour garder exactement la sémantique des `?? valeur` du proto.

public sealed record WeaponDef
{
    public required WeaponId Id { get; init; }
    public required WeaponKind Kind { get; init; }
    public required float Damage { get; init; }
    /// <summary>Tirs (ou salves) par seconde à énergie nominale.</summary>
    public required float Rof { get; init; }
    public required float Range { get; init; }
    /// <summary>Vitesse du projectile (cinétique, missile).</summary>
    public required float Speed { get; init; }
    /// <summary>Demi-angle de l'arc de tir, en degrés, autour de la direction du montage.</summary>
    public required float Arc { get; init; }
    public required Mount Mount { get; init; }
    public float? Salvo { get; init; }
    /// <summary>Dispersion (radians) appliquée à la visée cinétique.</summary>
    public float? Spread { get; init; }
    /// <summary>Probabilité de toucher par tir (défense ponctuelle).</summary>
    public float? HitChance { get; init; }
    /// <summary>Vitesse de suivi de la tourelle (rad/s) : au-delà, la cible traverse trop vite.</summary>
    public float? Tracking { get; init; }
    /// <summary>Lasers : rayon instantané (turbolaser) au lieu d'un trait qui voyage.</summary>
    public bool? Beam { get; init; }
    /// <summary>Rayons : retard de la tourelle (s).</summary>
    public float? AimLag { get; init; }
    public float? VsShield { get; init; }
    public float? VsHull { get; init; }
    /// <summary>Bombes : rayon de souffle, part qui ignore les boucliers, points de vie.</summary>
    public float? Blast { get; init; }
    public float? Pierce { get; init; }
    public float? Hp { get; init; }
    /// <summary>Vitesse de virage du guidage (rad/s), missiles et bombes.</summary>
    public float? Turn { get; init; }
}

public sealed record DroneDef(float Hp, float Speed, float Turn, float Damage, float Rof, float Range);

/// <summary>Baie de hangar, dans le repère de la coque (+Z nez, +X bâbord, +Y dorsal), en rayons de coque.</summary>
public readonly record struct HangarBay(float3 P, float3 N);

public sealed record HangarDef
{
    public required int Stock { get; init; }
    public required int MaxActive { get; init; }
    /// <summary>Secondes moyennes par drone ; ils partent par vagues de <see cref="Wave"/>.</summary>
    public required float LaunchInterval { get; init; }
    public required int Wave { get; init; }
    public required DroneDef Drone { get; init; }
    /// <summary>Part de chaque vague lancée en intercepteurs (chasse drones, missiles et bombes).</summary>
    public required float Interceptors { get; init; }
    public required DroneDef Interceptor { get; init; }
    public required HangarBay[] Bays { get; init; }
}

/// <summary>Module Orionum : stabilisé (Coalition) ou brut (Clans).</summary>
public sealed record OrionumModuleDef(bool Stable);

public sealed record ShipClassDef
{
    public required ShipClassId Id { get; init; }
    /// <summary>L'identifiant texte du TS (<c>bastion</c>...), pour les logs et la comparaison.</summary>
    public required string Key { get; init; }
    public required Side Side { get; init; }
    public required Role Role { get; init; }
    public required string LabelFr { get; init; }
    public required string LabelEn { get; init; }
    public required float Hull { get; init; }
    public required float ShieldFace { get; init; }
    public required float ShieldRegen { get; init; }
    public required float SubsystemHp { get; init; }
    /// <summary>Vitesse de croisière : plafond de l'ordinateur de vol (pas de frottement dans le vide).</summary>
    public required float Speed { get; init; }
    /// <summary>Accélération du moteur principal, dans l'axe du nez seulement (u/s²).</summary>
    public required float Accel { get; init; }
    /// <summary>Propulseurs de manœuvre, en fraction du moteur principal.</summary>
    public required float Rcs { get; init; }
    /// <summary>Vitesse angulaire max (rad/s).</summary>
    public required float TurnRate { get; init; }
    /// <summary>Accélération angulaire (rad/s²) : les grosses coques démarrent et freinent lentement.</summary>
    public required float AngAccel { get; init; }
    public required float Radius { get; init; }
    public required WeaponDef[] Weapons { get; init; }
    /// <summary>Durci contre la bombe à énergie (la Tanière).</summary>
    public bool EmpHardened { get; init; }
    /// <summary>Les grosses coques gardent au moins cet espace (u, coque à coque) avec tout ennemi.</summary>
    public float? Standoff { get; init; }
    public OrionumModuleDef? Orionum { get; init; }
    public WeaponDef? Emp { get; init; }
    public HangarDef? Hangar { get; init; }
    public required float PreferredRange { get; init; }
    public required EngageStyle Engage { get; init; }
    public required bool Mobile { get; init; }
}

/// <summary>Un puits de gravité tel que le scénario le pose.</summary>
public sealed record WellDef(string Name, float3 Pos, float Core, float Pull, float Reach);

public sealed record ScenarioDef
{
    public required ScenarioId Id { get; init; }
    public required Objective Objective { get; init; }
    public required float TimeLimit { get; init; }
    public required float3 DepotPos { get; init; }
    public required float3 CoalitionSpawn { get; init; }
    public required float3 ClansSpawn { get; init; }
    /// <summary>Rayon autour de son point de départ où une coque en retraite saute hors de la bataille.</summary>
    public required float RetreatRadius { get; init; }
    /// <summary>Puits posés quand le pilier Terrain est actif.</summary>
    public required WellDef[] Wells { get; init; }
}

/// <summary>Piliers Orionum activables pour une bataille.</summary>
public readonly record struct Pillars(bool Horloge, bool Terrain)
{
    public static readonly Pillars All = new(true, true);
}
