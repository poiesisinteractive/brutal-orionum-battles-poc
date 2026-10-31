namespace OrionumFleet.Sim;

// Port des unions de chaînes de types.ts en énumérations : plus aucune chaîne dans la boucle.
// Les ids d'entités commencent à 1 ; partout où le TS a `number | null` ou `?: number`, 0 veut dire « aucun ».

public enum Side : byte
{
    Coalition,
    Clans,
}

/// <summary>Rôle tactique. Les rôles des lots 1 à 3 (ligne, artillerie, soutien, convoi) sont gardés pour les données.</summary>
public enum Role : byte
{
    Brawler,
    Carrier,
    Capital,
    Escort,
    Depot,
    Line,
    Artillery,
    Support,
    Convoy,
}

/// <summary>Sous-système ciblable. Le <c>null</c> du TS (on vise la coque) devient <c>Subsystem?</c> = null.</summary>
public enum Subsystem : byte
{
    Engines,
    Weapons,
    Shields,
}

/// <summary>Faces de bouclier directionnelles, dans le repère de la coque.</summary>
public enum Face : byte
{
    Front,
    Back,
    Port,
    Starboard,
    Dorsal,
    Ventral,
}

public enum Posture : byte
{
    Balanced,
    Assault,
    Defense,
    Flee,
    Custom,
}

public enum WeaponKind : byte
{
    Kinetic,
    Missile,
    Pd,
    Laser,
    Bomb,
    Emp,
    Mine,
}

public enum Mount : byte
{
    Nose,
    Port,
    Starboard,
    Turret,
}

/// <summary>Identifiant d'arme (le champ <c>id</c> des WeaponDef du TS), lu par l'audio et les statistiques.</summary>
public enum WeaponId : byte
{
    Railgun,
    Battery,
    Autocannon,
    Missiles,
    Laser,
    Turbolaser,
    Bomb,
    FusionBomb,
    Emp,
    Pd,
}

public enum DroneRole : byte
{
    Bomber,
    Interceptor,
}

public enum EngageStyle : byte
{
    Nose,
    Broadside,
    Orbit,
    Standoff,
    Static,
}

/// <summary>attack / screen ; reserve = lance seulement sous menace ; surge = lance tout maintenant.</summary>
public enum HangarMode : byte
{
    Attack,
    Screen,
    Reserve,
    Surge,
}

public enum OrderType : byte
{
    Attack,
    Orbit,
    Escort,
    KeepDistance,
    Move,
    Hold,
    Retreat,
    Pass,
    Kite,
    /// <summary>Au contact : le seul ordre qui lève la distance de sécurité d'une grosse coque.</summary>
    Ram,
    /// <summary>Tenir un poste de formation relatif à une coque d'ancrage (commandement de flotte).</summary>
    Station,
    /// <summary>Remorquer un allié aux moteurs coupés (lot 1, non porté).</summary>
    Tow,
}

/// <summary>Comment une coque pointe son nez, indépendamment de son déplacement (vol newtonien).</summary>
public enum Pointing : byte
{
    Auto,
    Target,
    Broadside,
    Movement,
    Shield,
    Point,
    Face,
    Lock,
}

public enum TriggerType : byte
{
    /// <summary>Pas de déclencheur (le <c>trigger</c> absent du TS).</summary>
    None,
    Immediate,
    InRange,
    Delay,
    HullBelow,
}

public enum ModuleLevel : byte
{
    Raw,
    Stable,
    Mastered,
}

public enum OrionumAbility : byte
{
    Bubble,
    Overclock,
    Rewind,
    Stasis,
    Elan,
    Anchor,
}

public enum ScenarioId : byte
{
    Wolf359Raid,
    WellField,
}

/// <summary>Objectif de bataille. Seul le raid est dans le périmètre (les autres viennent du lot 1).</summary>
public enum Objective : byte
{
    Raid,
}

public enum ZoneKind : byte
{
    Bubble,
    Elan,
    Stasis,
    MicroStasis,
    Well,
    Desync,
}

public enum ResultReason : byte
{
    DepotDestroyed,
    Timeout,
    FleetLost,
}

public enum Formation : byte
{
    None,
    Line,
    Wall,
    Screen,
    Wedge,
    Sphere,
}

public enum EngageRange : byte
{
    Max,
    Optimal,
    Melee,
}

public enum FirePriority : byte
{
    Capital,
    Carrier,
    Brawler,
    Weakest,
    Nearest,
    Depot,
}

public enum EntityKind : byte
{
    None,
    Ship,
    Drone,
    Projectile,
}
