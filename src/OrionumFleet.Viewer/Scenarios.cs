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
        "Système de Wolf 359 : bataille des capitaux",
    ];

    public const int SystemBattle = 3;

    /// <param name="fleetScale">Bataille système : la flotte type des capitaux (25 coques par camp) multipliée.</param>
    public static World Create(int index, uint seed, int fleetScale = 2)
    {
        var options = new WorldOptions
        {
            Scenario = index switch
            {
                1 => ScenarioId.WellField,
                SystemBattle => ScenarioId.Wolf359System,
                _ => ScenarioId.Wolf359Raid,
            },
        };
        if (index == SystemBattle)
            options = options with
            {
                Coalition = CapitalData.Fleet(Side.Coalition, fleetScale),
                Clans = CapitalData.Fleet(Side.Clans, fleetScale),
            };
        var w = new World(seed, options);

        switch (index)
        {
            case 0:
            case SystemBattle:
                // Tous les groupes en ciblage automatique ; dans le système, chaque escadre fait route en
                // croisière vers l'ennemi le plus proche puis engage.
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
