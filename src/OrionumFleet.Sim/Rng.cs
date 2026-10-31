using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// PRNG seedé (mulberry32), port bit à bit de <c>src/sim/rng.ts</c> du proto TS.
/// La sim n'appelle jamais de source aléatoire non seedée : même graine + mêmes ordres = même bataille.
/// C'est un struct : copier le monde (projections de l'analyse tactique) copie aussi l'état du RNG.
/// </summary>
public struct Rng
{
    uint _state;

    public Rng(uint seed) => _state = seed != 0 ? seed : 0x9e3779b9;

    /// <summary>Nombre uniforme dans [0, 1), identique au <c>next()</c> du TS.</summary>
    public double NextDouble()
    {
        unchecked
        {
            var t = _state += 0x6d2b79f5;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + (t ^ (t >> 7)) * (t | 61);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    public float Next() => (float)NextDouble();

    public float Range(float min, float max) => (float)(min + (max - min) * NextDouble());

    /// <summary>Entier dans [0, maxExclusive), calculé en double comme le TS.</summary>
    public int Int(int maxExclusive) => (int)Math.Floor(NextDouble() * maxExclusive);

    public bool Chance(float probability) => NextDouble() < probability;

    public T Pick<T>(ReadOnlySpan<T> items) => items[Int(items.Length)];

    /// <summary>Vecteur unitaire uniforme sur la sphère.</summary>
    public float3 Unit()
    {
        var z = Range(-1, 1);
        var angle = Range(0, MathF.Tau);
        var r = MathF.Sqrt(1 - z * z);
        return new float3(r * MathF.Cos(angle), r * MathF.Sin(angle), z);
    }
}
