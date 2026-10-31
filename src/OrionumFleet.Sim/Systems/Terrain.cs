using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Pilier Terrain (terrain.ts) : puits de gravité. Le champ, la balistique et la fronde arrivent en S5.</summary>
public static class Terrain
{
    public static float WellCore(in GravityWell g) => g.Core;

    public static float WellPull(in GravityWell g) => g.Pull;

    /// <summary>Pose les puits du scénario (pilier Terrain actif).</summary>
    public static void CreateWells(World w)
    {
        if (!w.Pillars.Terrain)
            return;

        var wells = w.Scenario.Wells;
        for (var i = 0; i < wells.Length; i++)
        {
            var d = wells[i];
            w.Wells.Add(new GravityWell
            {
                Id = w.NextWellId++,
                NameIndex = (short)i,
                Pos = d.Pos,
                Core = d.Core,
                Pull = d.Pull,
                Reach = d.Reach,
                Fixed = true,
                Ttl = float.PositiveInfinity,
                Total = float.PositiveInfinity,
                Eye = 0,
                SourceId = 0,
            });
        }
    }

    /// <summary>Repousse un point de destination hors des cœurs (avec la marge de la coque).</summary>
    public static float3 ClearOfCores(World w, in Ship s, float3 p)
    {
        var output = p;
        foreach (ref readonly var g in w.Wells.AsSpan())
        {
            if (g.Core <= 0)
                continue;
            var edge = WellCore(g) + s.Class.Radius + GameData.Terrain.CoreMargin + 40;
            var d = Vec.Dist(output, g.Pos);
            if (d >= edge)
                continue;
            var dir = d > 1e-3f ? Vec.Normalize(output - g.Pos) : Vec.Normalize(s.Pos - g.Pos);
            output = g.Pos + Vec.Scale(dir, edge);
        }
        return output;
    }
}
