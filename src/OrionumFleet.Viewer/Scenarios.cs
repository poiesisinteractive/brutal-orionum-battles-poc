using OrionumFleet.Sim;

namespace OrionumFleet.Viewer;

/// <summary>
/// Batailles scriptées jouables dans le visualiseur, en attendant l'IA (S6). Les hangars sont pleins (S4).
/// </summary>
static class Scenarios
{
    public static readonly string[] Names =
    [
        "Raid sur Wolf 359 : engagement général",
        "Champ de puits : tir courbe sur la Tanière",
        "Raid : tenailles sur le Mastodonte",
    ];

    public static World Create(int index, uint seed)
    {
        var w = new World(seed, new WorldOptions { Scenario = index == 1 ? ScenarioId.WellField : ScenarioId.Wolf359Raid });

        switch (index)
        {
            case 0:
                // Mêmes ordres que CombatParityTests « raid » : tous les groupes en ciblage automatique.
                foreach (var side in (ReadOnlySpan<Side>)[Side.Coalition, Side.Clans])
                    foreach (var g in w.Fleet(side).Groups)
                        FleetSystem.CommandGroup(w.Fleet(side), g, x => x.AutoTarget = true);
                break;

            case 1:
                // Mêmes ordres que CombatParityTests « wells » : la Coalition en tir courbe attaque la Tanière.
                foreach (ref readonly var s in w.Ships.AsSpan())
                    if (s.Side == Side.Coalition)
                        w.SetArcFire(s.Id, true);
                var coalition = w.Fleet(Side.Coalition);
                foreach (var g in coalition.Groups)
                    FleetSystem.CommandGroup(coalition, g, x => x.TargetId = w.DepotId);
                var clans = w.Fleet(Side.Clans);
                foreach (var g in clans.Groups)
                    FleetSystem.CommandGroup(clans, g, x => x.AutoTarget = true);
                break;

            default:
                // La manœuvre du runner headless, avec des Clans qui se défendent.
                var mastodonte = 0;
                foreach (ref readonly var s in w.Ships.AsSpan())
                    if (s.ClassId == ShipClassId.Mastodonte)
                    {
                        mastodonte = s.Id;
                        break;
                    }
                FleetSystem.StartManeuver(w, Side.Coalition, new Maneuver { Type = ManeuverType.Pincer, TargetId = mastodonte, Groups = [1, 2] });
                foreach (var g in w.Fleet(Side.Clans).Groups)
                    FleetSystem.CommandGroup(w.Fleet(Side.Clans), g, x => x.AutoTarget = true);
                break;
        }
        return w;
    }
}
