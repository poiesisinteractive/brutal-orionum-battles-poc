using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Type d'événement, port des étiquettes <c>t</c> de <c>SimEvent</c> (types.ts).</summary>
public enum SimEventType : byte
{
    Fire,
    Beam,
    /// <summary>Détonation de bombe : souffle de zone (alliés compris).</summary>
    Blast,
    ShieldHit,
    /// <summary>Une face de bouclier vient de tomber à zéro.</summary>
    ShieldBreak,
    HullHit,
    SubsystemDown,
    Explosion,
    ShipDestroyed,
    /// <summary>Coque à zéro : l'agonie commence, avant la rupture du réacteur.</summary>
    ShipCrippled,
    /// <summary>Explosion secondaire sur une coque mourante (<see cref="SimEvent.Tear"/> : une pièce est arrachée).</summary>
    Secondary,
    /// <summary>Rupture du réacteur : le souffle final, dangereux (alliés compris).</summary>
    CoreBreach,
    Orionum,
    Resonance,
    WellCollapse,
    Flung,
    Swallowed,
    EmpBlast,
    EmpHit,
    Withdrawn,
    Launch,
}

/// <summary>Type de rayon d'un événement <see cref="SimEventType.Beam"/>.</summary>
public enum BeamKind : byte
{
    Pd,
    Drone,
    Turbolaser,
}

/// <summary>
/// Un événement de la sim, lu par le rendu et l'audio, jamais relu par la sim. Struct « union » :
/// seuls les champs utiles à son <see cref="Type"/> sont remplis (correspondance avec le TS en commentaire).
/// </summary>
public struct SimEvent
{
    public SimEventType Type;
    public Side Side;
    /// <summary>shipId, ou carrierId (launch).</summary>
    public int ShipId;
    /// <summary>ownerId (fire, beam), by (shipDestroyed, shipCrippled), droneId (launch), wellId (flung, swallowed).</summary>
    public int OtherId;
    /// <summary>at, ou from (fire, beam).</summary>
    public float3 At;
    /// <summary>dir (fire), to (beam), from (orionum : où était la coque avant de rembobiner).</summary>
    public float3 Vector;
    public bool HasVector;
    /// <summary>size, radius, amount ou duration selon le type.</summary>
    public float Value;
    public WeaponKind Weapon;
    public WeaponId WeaponId;
    public BeamKind Beam;
    public Face Face;
    public Subsystem Subsystem;
    public OrionumAbility Ability;
    public ZoneKind Zone;
    public bool Tear;
}
