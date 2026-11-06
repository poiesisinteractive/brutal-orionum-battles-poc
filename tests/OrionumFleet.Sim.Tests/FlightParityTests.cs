using System.Text.Json;
using Brutal.Numerics;
using Xunit.Abstractions;

namespace OrionumFleet.Sim.Tests;

/// <summary>
/// Trajectoires comparées au proto TS : les mêmes manœuvres scriptées (sans tir), positions et nez
/// échantillonnés chaque seconde (tools/ts-reference/dump-flight.ts). Le TS calcule en double et nous
/// en float : on tolère un écart, mesuré et affiché.
/// </summary>
public class FlightParityTests(ITestOutputHelper output)
{
    static readonly JsonElement Reference = JsonDocument
        .Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Reference", "flight.json")))
        .RootElement;

    static int Find(World w, ShipClassId classId) => Array.FindIndex(w.Ships.AsSpan().ToArray(), s => s.ClassId == classId);

    static void Isolate(World w, params int[] keep)
    {
        for (var i = 0; i < w.Ships.Count; i++)
            if (!keep.Contains(i) && w.Ships[i].ClassId != ShipClassId.Depot)
                w.Ships[i].Alive = false;
    }

    /// <summary>
    /// Avance la sim et rend l'écart maximal (position, u) et l'écart de nez maximal (1 - cos).
    /// Avec <paramref name="mirrorZ"/>, chaque échantillon est aussi comparé à son symétrique par rapport au
    /// plan z = <paramref name="mirrorZ"/>, et le plus proche des deux compte (voir le test du capital).
    /// </summary>
    (float Position, float Facing) Compare(string scenario, World w, int seconds, float? mirrorZ = null)
    {
        var expected = Reference.GetProperty(scenario);
        var ids = expected.EnumerateObject().Select(p => int.Parse(p.Name)).ToArray();
        float maxPos = 0, maxFacing = 0;
        for (var step = 1; step <= seconds * 20; step++)
        {
            w.Step();
            if (step % 20 != 0)
                continue;
            var sample = step / 20 - 1;
            foreach (var id in ids)
            {
                var e = expected.GetProperty(id.ToString())[sample];
                ref readonly var s = ref w.ShipById(id);
                var pos = new float3((float)e[0].GetDouble(), (float)e[1].GetDouble(), (float)e[2].GetDouble());
                var fwd = new float3((float)e[3].GetDouble(), (float)e[4].GetDouble(), (float)e[5].GetDouble());
                var posError = Vec.Dist(pos, s.Pos);
                var facingError = 1 - Vec.Dot(fwd, s.Fwd);
                if (mirrorZ is { } z0)
                {
                    var mirroredPos = new float3(pos.X, pos.Y, 2 * z0 - pos.Z);
                    var mirroredFwd = new float3(fwd.X, fwd.Y, -fwd.Z);
                    posError = MathF.Min(posError, Vec.Dist(mirroredPos, s.Pos));
                    facingError = MathF.Min(facingError, 1 - Vec.Dot(mirroredFwd, s.Fwd));
                }
                maxPos = MathF.Max(maxPos, posError);
                maxFacing = MathF.Max(maxFacing, facingError);
            }
        }
        output.WriteLine($"{scenario} : écart max {maxPos:F4} u, nez {maxFacing:E2}");
        return (maxPos, maxFacing);
    }

    static void AssertClose((float Position, float Facing) deviation, float positionTolerance)
    {
        Assert.True(deviation.Position < positionTolerance, $"écart de position {deviation.Position} u");
        Assert.True(deviation.Facing < 1e-3f, $"écart de nez {deviation.Facing}");
    }

    /// <summary>
    /// Pour freiner, le capital se retourne de +X vers -X : un demi-tour exact. Le nez voulu est alors pile
    /// opposé au nez actuel, et l'axe de rotation sort d'un produit vectoriel quasi nul dont le signe tient
    /// au bruit d'arrondi : le TS (double) pivote par -Z, nous (float) par +Z. Les deux trajectoires sont
    /// symétriques par rapport au plan de départ ; on compare donc au miroir près. La position en X, elle,
    /// colle au TS à 0,01 u près sur 110 s.
    /// </summary>
    [Fact]
    public void Capital_move_follows_the_ts_trajectory_up_to_the_flip_direction()
    {
        var w = new World(1);
        var i = Find(w, ShipClassId.Bastion);
        Isolate(w, i);
        var start = w.Ships[i].Pos;
        w.Issue(w.Ships[i].Id, new Order(OrderType.Move) { Point = start + new float3(1500, 0, 0) });
        AssertClose(Compare("capital-move", w, 110, mirrorZ: start.Z), 1);
    }

    [Fact]
    public void Frigate_kite_follows_the_ts_trajectory()
    {
        var w = new World(1);
        var s = Find(w, ShipClassId.Hoplite);
        var t = Find(w, ShipClassId.Ecorcheur);
        Isolate(w, s, t);
        w.Ships[t].Pos = w.Ships[s].Pos + new float3(0, 0, 150);
        w.Ships[t].WeaponCount = 0;
        w.Ships[s].WeaponCount = 0;
        w.Issue(w.Ships[s].Id, new Order(OrderType.Kite) { TargetId = w.Ships[t].Id });
        AssertClose(Compare("frigate-kite", w, 12), 1);
    }

    [Fact]
    public void Lancers_line_follows_the_ts_trajectory()
    {
        var w = new World(1);
        var f = w.Fleet(Side.Coalition);
        FleetSystem.CommandGroup(f, f.Groups[1], g => (g.Formation, g.MovePoint) = (Formation.Line, new float3(0, 40, -1500)));
        AssertClose(Compare("lancers-line", w, 40), 1);
    }

    [Fact]
    public void Escort_and_pass_follow_the_ts_trajectory()
    {
        var w = new World(1);
        var vigile = Find(w, ShipClassId.Vigile);
        var bastion = Find(w, ShipClassId.Bastion);
        var hop = Find(w, ShipClassId.Hoplite);
        Isolate(w, vigile, bastion, hop);
        foreach (ref var s in w.Ships.AsSpan())
            s.WeaponCount = 0;
        w.Issue(w.Ships[vigile].Id, new Order(OrderType.Escort) { TargetId = w.Ships[bastion].Id });
        w.Issue(w.Ships[bastion].Id, new Order(OrderType.Move) { Point = new float3(300, 40, -1400) });
        w.Issue(w.Ships[hop].Id, new Order(OrderType.Pass) { TargetId = w.DepotId });
        AssertClose(Compare("escort-and-pass", w, 60), 1);
    }

    [Fact]
    public void Pincer_follows_the_ts_trajectory()
    {
        var w = new World(1);
        foreach (ref var s in w.Ships.AsSpan())
        {
            s.WeaponCount = 0;
            s.Hangar.Stock = 0;
        }
        var mastodonte = w.Ships[Find(w, ShipClassId.Mastodonte)].Id;
        FleetSystem.StartManeuver(w, Side.Coalition, new Maneuver { Type = ManeuverType.Pincer, TargetId = mastodonte, Groups = [1, 2] });
        AssertClose(Compare("pincer", w, 60), 1);
    }
}
