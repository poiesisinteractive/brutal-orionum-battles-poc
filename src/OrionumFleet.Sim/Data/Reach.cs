namespace OrionumFleet.Sim;

/// <summary>Bornes tirées des données, pour dimensionner les requêtes spatiales (un rayon ne doit rien manquer).</summary>
public static class Reach
{
    /// <summary>Plus grand rayon de coque (la Tanière).</summary>
    public static readonly float MaxShipRadius = GameData.Classes.Max(c => c.Radius);

    /// <summary>Plus grande distance de sécurité d'une grosse coque.</summary>
    public static readonly float MaxStandoff = GameData.Classes.Max(c => c.Standoff ?? 0);
}
