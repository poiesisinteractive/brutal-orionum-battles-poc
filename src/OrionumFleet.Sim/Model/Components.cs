using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Répartition de l'énergie entre les trois pôles. La somme vaut toujours 1.</summary>
public record struct Energy(float Weapons, float Shields, float Engines);

public record struct SubsystemState(float Hp, float Max, bool Offline);

/// <summary>État d'une arme montée. Sa définition est l'arme d'index <see cref="DefIndex"/> de la classe.</summary>
public struct WeaponState
{
    public byte DefIndex;
    public float Cooldown;
    public int TargetId;
    /// <summary>Terrain : une cible que la conduite de tir vient de trouver cachée derrière un cœur (ignorée jusqu'à <see cref="BlockedUntil"/>).</summary>
    public int BlockedId;
    public float BlockedUntil;
}

public struct HangarState
{
    public int Stock;
    public float Cooldown;
    public HangarMode Mode;
}

/// <summary>Capacité Orionum ordonnée, exécutée au pas suivant.</summary>
public record struct PendingAbility(OrionumAbility Ability, float3? Point);

public struct OrionumState
{
    /// <summary>0 à 100. Au-delà de 70, une résonance peut éclater ; à 100, elle éclate.</summary>
    public float Instability;
    /// <summary>Secondes pendant lesquelles le module reste coupé après une résonance.</summary>
    public float Offline;
    public float Overclock;
    public PendingAbility? Pending;
    public int Uses;
    public int Resonances;
}

/// <summary>Ce vers quoi la coque pointe en mode <see cref="Pointing.Point"/> : une coque ou un point.</summary>
public record struct PointAt(int TargetId, float3? Point);

/// <summary>Déclencheur d'un ordre conditionnel (délai : instant absolu ; coque sous : fraction).</summary>
public record struct Trigger(TriggerType Type, float Value = 0)
{
    public static readonly Trigger Immediate = new(TriggerType.Immediate);
}

/// <summary>Un ordre, port de <c>Order</c>. Les champs optionnels du TS restent nullables (0 = aucune coque).</summary>
public struct Order
{
    public OrderType Type;
    public int TargetId;
    public float3? Point;
    public float? Distance;
    /// <summary>Sous-système visé (attaque). null = la coque.</summary>
    public Subsystem? Subsystem;
    public Trigger Trigger;
    /// <summary>Engager depuis cette direction (unitaire, monde, de la cible vers la coque) : tenailles, face visée.</summary>
    public float3? Bearing;
    /// <summary>Plafond de vitesse (u/s), par exemple pour une arrivée synchronisée.</summary>
    public float? SpeedCap;
    /// <summary>Coque sur laquelle tirer pendant un ordre non hostile (tenir son poste en formation).</summary>
    public int FocusId;
    /// <summary>Poste de formation : décalage monde depuis l'ancre (ordres station).</summary>
    public float3? Slot;

    public Order(OrderType type) : this() => Type = type;

    public static Order Hold => new(OrderType.Hold);
}

[InlineArray(6)]
public struct FaceValues
{
    float _element;
}

[InlineArray(3)]
public struct SubsystemStates
{
    SubsystemState _element;
}

[InlineArray(MaxWeapons)]
public struct WeaponStates
{
    /// <summary>Le plus armé du périmètre (Mastodonte) a 7 armes ; la Tanière à tourelles du lot 2 en aura 8.</summary>
    public const int MaxWeapons = 8;

    WeaponState _element;
}

/// <summary>File d'ordres à capacité fixe. Le TS n'a pas de limite ; au-delà de 16, les ordres en trop sont ignorés.</summary>
public struct OrderQueue
{
    public const int Capacity = 16;

    [InlineArray(Capacity)]
    public struct Buffer
    {
        Order _element;
    }

    Buffer _items;
    byte _count;

    public readonly int Count => _count;

    [UnscopedRef]
    public Span<Order> AsSpan() => ((Span<Order>)_items)[.._count];

    [UnscopedRef]
    public ref Order this[int index] => ref AsSpan()[index];

    public void Clear() => _count = 0;

    /// <summary>Remplace toute la file par un seul ordre (<c>s.orders = [o]</c>).</summary>
    public void Set(in Order order)
    {
        _items[0] = order;
        _count = 1;
    }

    public bool Push(in Order order)
    {
        if (_count == Capacity)
            return false;
        _items[_count++] = order;
        return true;
    }

    /// <summary>Retire l'ordre d'index donné en gardant l'ordre des suivants (<c>splice</c>).</summary>
    public void RemoveAt(int index)
    {
        var span = AsSpan();
        span[(index + 1)..].CopyTo(span[index..]);
        _count--;
    }
}

public struct ShipStats
{
    public float DamageDealt;
    public int Kills;
}

/// <summary>
/// Une coque, port de <c>Ship</c>. Struct blittable stocké dans une table contiguë et manipulé par
/// <c>ref</c> : le monde se copie d'un bloc. Les champs des lots 1 à 3 (remorquage, renforts) sont gardés à
/// leur valeur neutre pour brancher ces lots plus tard.
/// </summary>
public struct Ship
{
    public int Id;
    public Side Side;
    public ShipClassId ClassId;
    /// <summary>Index du nom dans <see cref="GameData.HullNames"/> (voir <see cref="GameData.HullName"/>).</summary>
    public short NameIndex;
    public bool Alive;
    /// <summary>Multiplicateur du temps local : fondation du pilier Horloge (bulles, surcadençage, stase).</summary>
    public float TimeScale;

    public float3 Pos;
    public float3 PrevPos;
    public float3 Vel;
    public float3 Fwd;
    public float3 PrevFwd;
    /// <summary>Vitesse angulaire (axe monde × rad/s).</summary>
    public float3 AngVel;

    public Pointing Pointing;
    public PointAt? PointAt;
    public Face ExposeFace;
    public float3? LockDir;
    /// <summary>Poussée du moteur principal (0 à 1) et accélération RCS (monde) du dernier pas, pour le rendu.</summary>
    public float Thrust;
    public float3 RcsAcc;

    public float Hull;
    public float HullMax;
    public FaceValues Faces;
    public SubsystemStates Subsystems;
    public Energy Energy;
    public Posture Posture;
    public OrderQueue Orders;
    public WeaponStates Weapons;
    public byte WeaponCount;
    public bool HasHangar;
    public HangarState Hangar;
    /// <summary>Dégâts récents reçus par face, qui décroissent : nourrit la posture Défense et l'IA.</summary>
    public FaceValues Threat;
    public float3 Spawn;

    /// <summary>Secondes avant la rupture du réacteur (coque à zéro, agonie). null = pas en train de mourir.</summary>
    public float? Dying;
    public float DyingTotal;
    public int KillerId;
    /// <summary>Secondes pendant lesquelles les boucliers ne se régénèrent pas (bombe à énergie).</summary>
    public float ShieldLock;
    public bool ShieldReboot;
    public int EmpAmmo;
    public int EmpTargetId;
    public bool HasOrionum;
    public OrionumState Orionum;
    /// <summary>Tir courbe (pilier Terrain).</summary>
    public bool ArcFire;
    /// <summary>Secondes de vitesse de fronde restantes.</summary>
    public float Fling;
    /// <summary>A sauté hors de la bataille (retraite réussie) : pas mort, mais parti.</summary>
    public bool Withdrawn;
    public ShipStats Stats;
    /// <summary>Remorquée par ce remorqueur (lot 1 : toujours 0 ici).</summary>
    public int TowedBy;
    /// <summary>Saut d'urgence en charge : secondes restantes (lot 3 : toujours null ici).</summary>
    public float? JumpCharge;
    /// <summary>Sonnée au sortir d'un saut (lot 3 : toujours 0 ici).</summary>
    public float Daze;

    public readonly ShipClassDef Class => GameData.Class(ClassId);

    public readonly string Name => GameData.HullName(ClassId, NameIndex);

    /// <summary>En jeu : vivante, pas partie, pas à l'agonie (<c>isActive</c> de combat.ts).</summary>
    public readonly bool IsActive => Alive && !Withdrawn && Dying is null;

    [UnscopedRef]
    public Span<WeaponState> WeaponSpan => ((Span<WeaponState>)Weapons)[..WeaponCount];
}

/// <summary>Un drone (core IA bridé), port de <c>Drone</c>.</summary>
public struct Drone
{
    public int Id;
    public Side Side;
    public bool Alive;
    public float TimeScale;
    public float3 Pos;
    public float3 PrevPos;
    public float3 Vel;
    public float3 Fwd;
    public int CarrierId;
    public float Hp;
    public float Cooldown;
    public int TargetId;
    public float RetargetIn;
    /// <summary>Secondes depuis la perte du lien avec le porteur : les drones s'éteignent.</summary>
    public float OrphanTime;
    /// <summary>Passe d'attaque : vrai pendant le dégagement avant la passe suivante.</summary>
    public bool Extending;
    public DroneRole Role;
    public float Fling;
}

/// <summary>Un projectile (obus, missile, bombe, trait laser), port de <c>Projectile</c>.</summary>
public struct Projectile
{
    public int Id;
    public Side Side;
    public bool Alive;
    public float TimeScale;
    public float3 Pos;
    public float3 PrevPos;
    public float3 Vel;
    public WeaponKind Weapon;
    public int OwnerId;
    public float Damage;
    public float Ttl;
    public int TargetId;
    public Subsystem? Subsystem;
    public float Speed;
    // Optionnels du TS : null = absent, pour garder ses `?? valeur`.
    public float? Hp;
    public float? GuideDelay;
    public bool? Flung;
    public float? ArmIn;
    public float? Blast;
    public float? Pierce;
    public float? Turn;
    public float? VsShield;
    public float? VsHull;
}

/// <summary>Une zone où le temps (ou la gravité) se dérègle.</summary>
public struct TimeZone
{
    public int Id;
    public ZoneKind Kind;
    public float3 Pos;
    public float Radius;
    public float Ttl;
    public float Total;
    public Side Side;
    public int SourceId;
}

/// <summary>
/// Un puits de gravité vivant : vestige fixe du scénario (un cœur qui avale) ou fronde ancrée par
/// l'Orionum (sans cœur, elle relance ce qui frôle son œil). <c>ttl = Infinity</c> pour un vestige.
/// </summary>
public struct GravityWell
{
    public int Id;
    /// <summary>Index du puits dans le scénario, ou -1 pour une fronde.</summary>
    public short NameIndex;
    public float3 Pos;
    public float Core;
    public float Pull;
    public float Reach;
    public bool Fixed;
    public float Ttl;
    public float Total;
    public float Eye;
    public int SourceId;
}

/// <summary>Instantané d'une coque pour la fenêtre rétroactive.</summary>
public struct Snapshot
{
    public double Time;
    public float3 Pos;
    public float3 Vel;
    public float3 Fwd;
    public float Hull;
    public FaceValues Faces;
    public SubsystemStates Subsystems;
}

public readonly record struct BattleResult(Side Winner, ResultReason Reason, double Time);

/// <summary>Référence vers une entité : sa table et son index.</summary>
public readonly record struct EntityRef(EntityKind Kind, int Index)
{
    public static readonly EntityRef None = new(EntityKind.None, -1);

    public bool IsNone => Kind == EntityKind.None;
}
