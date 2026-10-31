using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>Repère orthonormé d'une coque : nez, droite, haut.</summary>
public readonly record struct Frame(float3 Fwd, float3 Right, float3 Up);

/// <summary>
/// Maths vectorielles de la sim, port de <c>vec.ts</c>. On garde les formules et l'ordre des
/// opérations du TS (et ses replis : <see cref="Normalize"/> rend +Z pour un vecteur nul) plutôt
/// que les helpers de Brutal.Numerics, pour rester comparable au proto.
/// </summary>
public static class Vec
{
    /// <summary>Haut du monde : les coques s'inclinent librement mais gardent une préférence pour cet axe.</summary>
    public static readonly float3 Up = new(0, 1, 0);

    public static float Dot(float3 a, float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    public static float3 Cross(float3 a, float3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    public static float LenSq(float3 a) => a.X * a.X + a.Y * a.Y + a.Z * a.Z;

    public static float Len(float3 a) => MathF.Sqrt(LenSq(a));

    public static float DistSq(float3 a, float3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public static float Dist(float3 a, float3 b) => MathF.Sqrt(DistSq(a, b));

    public static float3 Scale(float3 a, float s) => new(a.X * s, a.Y * s, a.Z * s);

    /// <summary>Vecteur unitaire, ou +Z si le vecteur est (quasi) nul.</summary>
    public static float3 Normalize(float3 a)
    {
        var l = Len(a);
        return l > 1e-9f ? Scale(a, 1 / l) : new float3(0, 0, 1);
    }

    public static float3 Lerp(float3 a, float3 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    public static float3 ClampLen(float3 a, float max)
    {
        var l = Len(a);
        return l > max ? Scale(a, max / l) : a;
    }

    /// <summary>Tourne le vecteur unitaire <paramref name="from"/> vers <paramref name="to"/> d'au plus <paramref name="maxAngle"/> radians.</summary>
    public static float3 RotateToward(float3 from, float3 to, float maxAngle)
    {
        var d = MathF.Min(1, MathF.Max(-1, Dot(from, to)));
        var angle = MathF.Acos(d);
        if (angle <= maxAngle || angle < 1e-6f)
            return Normalize(to);

        var axis = Cross(from, to);
        if (LenSq(axis) < 1e-12f)
        {
            // Vecteurs opposés : n'importe quel axe perpendiculaire.
            axis = MathF.Abs(from.Y) < 0.9f ? Cross(from, Up) : Cross(from, new float3(1, 0, 0));
        }
        axis = Normalize(axis);
        return Normalize(RotateAxis(from, axis, maxAngle));
    }

    /// <summary>Rotation de Rodrigues de <paramref name="v"/> autour de l'axe unitaire <paramref name="axis"/>.</summary>
    public static float3 RotateAxis(float3 v, float3 axis, float angle)
    {
        var c = MathF.Cos(angle);
        var s = MathF.Sin(angle);
        var kxv = Cross(axis, v);
        var kdv = Dot(axis, v);
        return new float3(
            v.X * c + kxv.X * s + axis.X * kdv * (1 - c),
            v.Y * c + kxv.Y * s + axis.Y * kdv * (1 - c),
            v.Z * c + kxv.Z * s + axis.Z * kdv * (1 - c)
        );
    }

    /// <summary>Repère orthonormé à partir d'un nez, roulis aligné sur le haut du monde.</summary>
    public static Frame FrameFromForward(float3 fwd)
    {
        var f = Normalize(fwd);
        var right = Cross(f, Up);
        if (LenSq(right) < 1e-8f)
            right = new float3(1, 0, 0);
        right = Normalize(right);
        var up = Normalize(Cross(right, f));
        return new Frame(f, right, up);
    }

    public static float AngleBetween(float3 a, float3 b) =>
        MathF.Acos(MathF.Min(1, MathF.Max(-1, Dot(Normalize(a), Normalize(b)))));

    /// <summary>Direction d'un point vers un autre (<c>dirTo</c> de world.ts).</summary>
    public static float3 DirTo(float3 from, float3 to) => Normalize(to - from);
}
