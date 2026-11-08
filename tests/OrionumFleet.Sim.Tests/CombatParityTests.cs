using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Parité statistique du combat avec le proto TS (tools/ts-reference/dump-combat.ts) : des batailles
/// scriptées sans IA. Trois scénarios :
/// <list type="bullet">
/// <item>raid : Raid sur Wolf 359, hangars vides, les deux flottes engagées par le commandement de flotte, 180 s ;</item>
/// <item>wells : Champ de puits, hangars vides, la Coalition en tir courbe attaque la Tanière masquée par un puits, 240 s ;</item>
/// <item>raid-drones : le raid avec les hangars pleins (S4).</item>
/// </list>
/// Le TS calcule en double et nous en float, et une même graine diverge vite : on compare des moyennes,
/// à l'erreur type près.
/// </summary>
public class CombatParityTests(ITestOutputHelper output)
{
    static readonly WeaponKind[] Kinds = [WeaponKind.Kinetic, WeaponKind.Missile, WeaponKind.Laser, WeaponKind.Bomb];

    sealed record Battle(Dictionary<string, double> Metrics);

    static Battle Run(uint seed, string scenario)
    {
        var wells = scenario == "wells";
        var w = new World(seed, new WorldOptions { Scenario = wells ? ScenarioId.WellField : ScenarioId.Wolf359Raid });
        if (scenario != "raid-drones")
            foreach (ref var s in w.Ships.AsSpan())
                s.Hangar.Stock = 0;
        if (wells)
        {
            foreach (ref readonly var s in w.Ships.AsSpan())
                if (s.Side == Side.Coalition)
                    w.SetArcFire(s.Id, true);
            var coalition = w.Fleet(Side.Coalition);
            foreach (var g in coalition.Groups)
                FleetSystem.CommandGroup(coalition, g, x => x.TargetId = w.DepotId);
            var clans = w.Fleet(Side.Clans);
            foreach (var g in clans.Groups)
                FleetSystem.CommandGroup(clans, g, x => x.AutoTarget = true);
            w.Run(240);
        }
        else
        {
            foreach (var side in (ReadOnlySpan<Side>)[Side.Coalition, Side.Clans])
                foreach (var g in w.Fleet(side).Groups)
                    FleetSystem.CommandGroup(w.Fleet(side), g, x => x.AutoTarget = true);
            w.Run(180);
        }

        double Per(Side side, Func<Ship, double> f)
        {
            double sum = 0;
            foreach (ref readonly var s in w.Ships.AsSpan())
                if (s.Side == side && s.ClassId != ShipClassId.Depot)
                    sum += f(s);
            return sum;
        }
        double ByKind(ref ShotCounters counters, WeaponKind kind)
        {
            double sum = 0;
            foreach (var c in Enum.GetValues<ShipClassId>())
                sum += counters[ShotCounters.Key(c, kind)];
            return sum;
        }

        var depot = w.ShipById(w.DepotId);
        var metrics = new Dictionary<string, double>
        {
            ["damage.coalition"] = Per(Side.Coalition, s => s.Stats.DamageDealt),
            ["damage.clans"] = Per(Side.Clans, s => s.Stats.DamageDealt),
            ["lost.coalition"] = Per(Side.Coalition, s => s.IsActive ? 0 : 1),
            ["lost.clans"] = Per(Side.Clans, s => s.IsActive ? 0 : 1),
            ["hullLeft.coalition"] = Per(Side.Coalition, s => s.Alive ? s.Hull : 0),
            ["hullLeft.clans"] = Per(Side.Clans, s => s.Alive ? s.Hull : 0),
            ["depotHull"] = depot.Alive ? depot.Hull : 0,
            ["bombsFired"] = w.Stats.BombsFired,
            ["bombsDetonated"] = w.Stats.BombsDetonated,
            ["blockedShots"] = w.Stats.BlockedShots,
            ["arcShots"] = w.Stats.ArcShots.Coalition + w.Stats.ArcShots.Clans,
            ["swallowed"] = w.Stats.Swallowed,
            ["bombsIntercepted"] = w.Stats.BombsIntercepted,
            ["dronesLaunched"] = w.Stats.DronesLaunched,
            ["droneShipHits"] = w.Stats.DroneShipHits,
            ["droneDroneShots"] = w.Stats.DroneDroneShots,
            ["droneSubsystemKills"] = w.Stats.DroneSubsystemKills,
            ["dronesLostToPd"] = w.Stats.DronesLostToPd,
            ["dronesAlive"] = w.Drones.AsSpan().ToArray().Count(d => d.Alive),
        };
        foreach (var kind in Kinds)
        {
            var name = kind.ToString().ToLowerInvariant();
            metrics[$"shots.{name}"] = ByKind(ref w.Stats.Shots, kind);
            metrics[$"hits.{name}"] = ByKind(ref w.Stats.Hits, kind);
        }
        return new Battle(metrics);
    }

    static Battle Parse(JsonElement b)
    {
        var metrics = new Dictionary<string, double>();
        foreach (var group in (string[])["damage", "lost", "hullLeft"])
            foreach (var side in (string[])["coalition", "clans"])
                metrics[$"{group}.{side}"] = b.GetProperty(group).GetProperty(side).GetDouble();
        foreach (var group in (string[])["shots", "hits"])
            foreach (var kind in Kinds)
            {
                var name = kind.ToString().ToLowerInvariant();
                metrics[$"{group}.{name}"] = b.GetProperty(group).GetProperty(name).GetDouble();
            }
        foreach (var key in (string[])["depotHull", "bombsFired", "bombsDetonated", "blockedShots", "arcShots", "swallowed"])
            metrics[key] = b.GetProperty(key).GetDouble();
        // Métriques des drones (S4) : seulement dans les références qui les contiennent.
        foreach (var key in (string[])["bombsIntercepted", "dronesLaunched", "droneShipHits", "droneDroneShots", "droneSubsystemKills", "dronesLostToPd", "dronesAlive"])
            if (b.TryGetProperty(key, out var v))
                metrics[key] = v.GetDouble();
        return new Battle(metrics);
    }

    static (double Mean, double StdErr) Stats(IReadOnlyList<Battle> battles, string key)
    {
        var values = battles.Select(b => b.Metrics[key]).ToArray();
        var mean = values.Average();
        var variance = values.Sum(v => (v - mean) * (v - mean)) / Math.Max(1, values.Length - 1);
        return (mean, Math.Sqrt(variance / values.Length));
    }

    [Theory]
    [InlineData("raid")]
    [InlineData("wells")]
    [InlineData("raid-drones")]
    public void Scripted_battles_match_the_ts_proto_statistically(string scenario)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reference", $"combat-{scenario}.json")));
        var reference = json.RootElement.EnumerateArray().Select(Parse).ToList();

        var clock = Stopwatch.StartNew();
        var ours = Enumerable.Range(1, reference.Count).Select(seed => Run((uint)seed, scenario)).ToList();
        output.WriteLine($"{scenario} : {ours.Count} batailles C# en {clock.Elapsed.TotalSeconds:F1} s\n");

        output.WriteLine($"{"métrique",-20} {"TS",12} {"C#",12} {"écart",9} {"tolérance",10}");
        var failures = new List<string>();
        foreach (var key in reference[0].Metrics.Keys)
        {
            var (ts, tsErr) = Stats(reference, key);
            var (cs, csErr) = Stats(ours, key);
            // Trois erreurs types combinées, plus un plancher de 2 % (ou 0,5 pour les petits compteurs).
            var tolerance = 3 * Math.Sqrt(tsErr * tsErr + csErr * csErr) + Math.Max(0.5, 0.02 * Math.Abs(ts));
            var gap = Math.Abs(cs - ts);
            output.WriteLine($"{key,-20} {ts,12:F1} {cs,12:F1} {gap,9:F1} {tolerance,10:F1}{(gap > tolerance ? "  <-- HORS TOLÉRANCE" : "")}");
            if (gap > tolerance)
                failures.Add(key);
        }
        Assert.True(failures.Count == 0, $"Hors tolérance : {string.Join(", ", failures)}");
    }
}
