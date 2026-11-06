// Runner headless. Le runner IA contre IA (équivalent de `npm run sim`) arrive au jalon S7 ;
// en attendant, il joue une manœuvre de flotte scriptée pour voir le vol et le commandement en action.
//
//   dotnet run --project src/OrionumFleet.Headless -- [graine]

using Brutal.Numerics;
using OrionumFleet.Sim;

var seed = args.Length > 0 && uint.TryParse(args[0], out var s) ? s : 1u;
var w = new World(seed);

Console.WriteLine($"Raid sur Wolf 359, graine {seed} : {w.Ships.Count} coques, {w.Wells.Count} puits.");
Console.WriteLine("Pas de tir avant S3 : la Coalition prend le Mastodonte en tenailles, les Clans tiennent leurs positions.\n");

var mastodonte = w.Ships.AsSpan().ToArray().First(x => x.ClassId == ShipClassId.Mastodonte);
FleetSystem.StartManeuver(w, Side.Coalition, new Maneuver
{
    Type = ManeuverType.Pincer,
    TargetId = mastodonte.Id,
    Groups = [1, 2],
});

var started = DateTime.UtcNow;
for (var t = 0; t <= 120; t += 20)
{
    while (w.Time < t)
        w.Step();

    Console.WriteLine($"t = {t,3} s");
    foreach (ref readonly var ship in w.Ships.AsSpan())
    {
        if (ship.Side != Side.Coalition)
            continue;
        var order = ship.Orders.Count > 0 ? ship.Orders[0].Type.ToString() : "-";
        var distance = Vec.Dist(ship.Pos, mastodonte.Pos);
        Console.WriteLine(
            $"  {ship.Name,-14} {ship.Class.Key,-8} pos ({ship.Pos.X,7:F0} {ship.Pos.Y,5:F0} {ship.Pos.Z,7:F0})"
            + $"  v {Vec.Len(ship.Vel),5:F1} u/s  à {distance,5:F0} u du Mastodonte  ordre {order}");
    }
}

Console.WriteLine($"\n{w.Tick} pas simulés en {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms.");
