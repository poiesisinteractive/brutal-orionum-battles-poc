using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// Index spatial d'une table d'entités, reconstruit une fois par pas (après l'élagage du pas précédent).
/// Une requête rend tous les candidats qui peuvent être à moins de r du centre :
/// <list type="bullet">
/// <item>ceux de la grille, avec une marge <see cref="Pad"/> qui couvre le plus grand déplacement possible
/// depuis la construction (les positions bougent pendant le pas) ;</item>
/// <item>puis toutes les entités créées depuis la construction (elles sont en fin de table).</item>
/// </list>
/// Les appelants refont le test de distance exact sur les positions courantes : on trouve les mêmes entités
/// qu'avec une boucle sur toute la table. L'ordre, lui, est celui des seaux de la grille (pas de tri : une
/// requête dense rend des milliers d'entrées) ; il reste déterministe, mais les égalités et les sommes de
/// flottants peuvent différer du TS. Une petite table (moins de <see cref="GridFrom"/> entrées) n'est pas
/// indexée : la requête rend tout, dans l'ordre des index, comme le TS.
/// </summary>
public sealed class EntityIndex(float cellSize, float pad, int buckets = 8192)
{
    /// <summary>En dessous, parcourir toute la table coûte moins que construire et interroger la grille.</summary>
    const int GridFrom = 64;

    readonly SpatialGrid _grid = new(cellSize, buckets);
    float3[] _positions = new float3[256];
    int _built;
    bool _gridded;

    /// <summary>Plus grand déplacement d'une entité pendant un pas (u).</summary>
    public float Pad { get; } = pad;

    public void Build<T>(EntityTable<T> table, Func<T, float3> position) where T : struct
    {
        _built = table.Count;
        _gridded = _built >= GridFrom;
        if (!_gridded)
            return;
        if (_positions.Length < _built)
            _positions = new float3[Math.Max(_built, _positions.Length * 2)];
        var span = table.AsSpan();
        for (var i = 0; i < span.Length; i++)
            _positions[i] = position(span[i]);
        _grid.Build(_positions.AsSpan(0, _built));
    }

    /// <summary>Candidats à moins de <paramref name="radius"/> de <paramref name="center"/> parmi les <paramref name="count"/> entités de la table.</summary>
    public ReadOnlySpan<int> Query(float3 center, float radius, int count, ref int[]? buffer)
    {
        buffer ??= new int[64];
        var found = _gridded ? _grid.QuerySphere(center, radius + Pad, ref buffer, sorted: false) : 0;
        var from = _gridded ? _built : 0;
        var extra = Math.Max(0, count - from);
        if (found + extra > buffer.Length)
            Array.Resize(ref buffer, Math.Max(found + extra, buffer.Length * 2));
        for (var i = from; i < count; i++)
            buffer[found++] = i;
        return buffer.AsSpan(0, found);
    }
}
