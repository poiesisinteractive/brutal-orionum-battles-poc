using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// Grille spatiale uniforme hachée, reconstruite à chaque tick. Elle répond aux requêtes de voisinage
/// (ciblage, défense ponctuelle, souffles, mines, évitement) à la place des boucles en O(n²) du TS.
///
/// Construction par tri par comptage : <c>Build</c> ne fait aucune allocation une fois les tableaux
/// dimensionnés. Les requêtes rendent les index <b>triés par ordre croissant</b>, c'est-à-dire dans
/// l'ordre de création des entités, comme les boucles du TS : les égalités se départagent pareil.
/// </summary>
public sealed class SpatialGrid
{
    /// <summary>Au-delà de ce nombre de cellules couvertes, une requête balaie tous les seaux.</summary>
    const int MaxVisitedCells = 512;

    readonly float _cellSize;
    readonly float _inverseCell;
    readonly int _bucketMask;
    readonly int[] _bucketStart;
    readonly int[] _bucketCount;
    int[] _entries = [];
    int[] _entryBucket = [];
    float3[] _positions = [];
    int _count;

    /// <param name="cellSize">Côté d'une cellule (u). Idéalement de l'ordre du rayon de requête courant.</param>
    /// <param name="buckets">Nombre de seaux de hachage (puissance de deux).</param>
    public SpatialGrid(float cellSize, int buckets = 4096)
    {
        if ((buckets & (buckets - 1)) != 0)
            throw new ArgumentException("Le nombre de seaux doit être une puissance de deux", nameof(buckets));

        _cellSize = cellSize;
        _inverseCell = 1 / cellSize;
        _bucketMask = buckets - 1;
        _bucketStart = new int[buckets + 1];
        _bucketCount = new int[buckets];
    }

    public int Count => _count;

    /// <summary>Indexe des positions ; l'index d'une entrée est sa position dans <paramref name="positions"/>.</summary>
    public void Build(ReadOnlySpan<float3> positions)
    {
        _count = positions.Length;
        if (_entries.Length < _count)
        {
            var size = Math.Max(_count, 64);
            _entries = new int[size];
            _entryBucket = new int[size];
            _positions = new float3[size];
        }

        Array.Clear(_bucketCount);
        for (var i = 0; i < _count; i++)
        {
            var p = positions[i];
            _positions[i] = p;
            var bucket = BucketOf(CellOf(p.X), CellOf(p.Y), CellOf(p.Z));
            _entryBucket[i] = bucket;
            _bucketCount[bucket]++;
        }

        _bucketStart[0] = 0;
        for (var b = 0; b <= _bucketMask; b++)
            _bucketStart[b + 1] = _bucketStart[b] + _bucketCount[b];

        // Remplissage dans l'ordre des index : chaque seau reste trié par index croissant.
        Array.Clear(_bucketCount);
        for (var i = 0; i < _count; i++)
        {
            var bucket = _entryBucket[i];
            _entries[_bucketStart[bucket] + _bucketCount[bucket]++] = i;
        }
    }

    /// <summary>
    /// Index des entrées à moins de <paramref name="radius"/> de <paramref name="center"/>, triés par ordre
    /// croissant, écrits dans <paramref name="results"/> (agrandi si besoin). Rend le nombre trouvé.
    /// </summary>
    public int QuerySphere(float3 center, float radius, ref int[] results)
    {
        var found = 0;
        var radiusSq = radius * radius;
        int x0 = CellOf(center.X - radius), x1 = CellOf(center.X + radius);
        int y0 = CellOf(center.Y - radius), y1 = CellOf(center.Y + radius);
        int z0 = CellOf(center.Z - radius), z1 = CellOf(center.Z + radius);

        // Une grande requête couvre trop de cellules : on balaie alors chaque seau une fois.
        var cells = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        if (cells > Math.Min(_bucketMask + 1, MaxVisitedCells))
        {
            for (var b = 0; b <= _bucketMask; b++)
                CollectBucket(b, center, radiusSq, ref results, ref found);
        }
        else
        {
            // Plusieurs cellules peuvent tomber dans le même seau : on ne visite chaque seau qu'une fois.
            Span<int> visited = stackalloc int[(int)cells];
            var visitedCount = 0;
            for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
            for (var z = z0; z <= z1; z++)
            {
                var bucket = BucketOf(x, y, z);
                if (visited[..visitedCount].Contains(bucket))
                    continue;
                visited[visitedCount++] = bucket;
                CollectBucket(bucket, center, radiusSq, ref results, ref found);
            }
        }

        results.AsSpan(0, found).Sort();
        return found;
    }

    void CollectBucket(int bucket, float3 center, float radiusSq, ref int[] results, ref int found)
    {
        var end = _bucketStart[bucket + 1];
        for (var e = _bucketStart[bucket]; e < end; e++)
        {
            var index = _entries[e];
            if (Vec.DistSq(_positions[index], center) > radiusSq)
                continue;
            if (found == results.Length)
                Array.Resize(ref results, Math.Max(16, results.Length * 2));
            results[found++] = index;
        }
    }

    int CellOf(float v) => (int)MathF.Floor(v * _inverseCell);

    int BucketOf(int x, int y, int z) =>
        (int)(((uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u) & (uint)_bucketMask);

    public float CellSize => _cellSize;
}
