using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Doctrine d'engagement d'un groupe.</summary>
public record struct Doctrine(EngageRange Range, FirePriority Priority, float RetreatAt, HangarMode Drones)
{
    public static readonly Doctrine Default = new(EngageRange.Optimal, FirePriority.Capital, 0, HangarMode.Attack);
}

/// <summary>
/// Groupe tactique (commandement de flotte), port de <c>Group</c>. C'est une classe : quelques groupes
/// par camp, avec une liste de membres de taille variable dont l'ordre compte (postes de formation,
/// départage de l'ancre). <see cref="Clone"/> en fait une copie profonde pour les projections.
/// </summary>
public sealed class Group
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public List<int> Members { get; set; } = [];
    public Formation Formation;
    public Doctrine Doctrine;
    /// <summary>Cible explicite ; 0 + <see cref="AutoTarget"/> = choisir selon la priorité de la doctrine.</summary>
    public int TargetId;
    public bool AutoTarget;
    public float3? MovePoint;
    /// <summary>Le groupe est sous commandement de flotte (sinon ses coques ne suivent que les ordres directs).</summary>
    public bool Active;
    public float3? Bearing;
    public float? SpeedCap;

    public Group Clone()
    {
        var copy = (Group)MemberwiseClone();
        copy.Members = [.. Members];
        return copy;
    }
}

public enum ManeuverType : byte
{
    Pincer,
    Sync,
    FocusFace,
    Echelon,
}

/// <summary>Manœuvre coordonnée, port de l'union <c>Maneuver</c>.</summary>
public sealed class Maneuver
{
    public required ManeuverType Type { get; init; }
    /// <summary>Cible (tenailles, face visée).</summary>
    public int TargetId { get; init; }
    public required int[] Groups { get; init; }
    /// <summary>Échelon : groupe qui se replie, et début de sa phase.</summary>
    public int Moving;
    public double PhaseStart;

    public Maneuver Clone() => (Maneuver)MemberwiseClone();
}

/// <summary>État du commandement de flotte d'un camp, port de <c>FleetState</c>.</summary>
public sealed class FleetState
{
    public required Side Side { get; init; }
    public List<Group> Groups { get; set; } = [];
    /// <summary>Coques sorties du contrôle de leur groupe par un ordre direct.</summary>
    public List<int> Detached { get; set; } = [];
    public Maneuver? Maneuver;
    /// <summary>Secondes avant le prochain tour de commandement (en double, comme le TS : même cadence).</summary>
    public double Clock;

    public FleetState Clone() => new()
    {
        Side = Side,
        Groups = Groups.ConvertAll(g => g.Clone()),
        Detached = [.. Detached],
        Maneuver = Maneuver?.Clone(),
        Clock = Clock,
    };
}
