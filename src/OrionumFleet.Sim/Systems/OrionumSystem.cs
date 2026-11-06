using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Pilier Horloge (orionum.ts). Seul ce dont le vol a besoin est porté ici ; le reste arrive en S5.</summary>
public static class OrionumSystem
{
    /// <summary>Une stase est un mur : les coques qui l'approchent sont repoussées (dedans, elles sont figées de toute façon).</summary>
    public static float3? StasisPush(World w, in Ship s)
    {
        float3? push = null;
        var radius = s.Class.Radius;
        foreach (ref readonly var z in w.Zones.AsSpan())
        {
            if (z.Kind is not (ZoneKind.Stasis or ZoneKind.MicroStasis))
                continue;
            var d = Vec.Dist(s.Pos, z.Pos);
            var edge = z.Radius + radius + 40;
            if (d >= edge || d < z.Radius)
                continue;
            var away = Vec.Scale(Vec.Normalize(s.Pos - z.Pos), (edge - d) * 2);
            push = push is { } p ? p + away : away;
        }
        return push;
    }
}
