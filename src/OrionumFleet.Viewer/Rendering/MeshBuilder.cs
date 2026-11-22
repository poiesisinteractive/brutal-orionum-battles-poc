using Brutal.Numerics;
using OrionumFleet.Sim;

namespace OrionumFleet.Viewer.Rendering;

/// <summary>
/// Construction de maillages à facettes (normales par face). Chaque primitive connaît un point intérieur et oriente
/// ses faces vers l'extérieur : pas besoin de soigner l'ordre des sommets, et l'éclairage reste juste.
/// </summary>
sealed class MeshBuilder
{
    readonly List<Vertex> _vertices = new(4096);

    public int Count => _vertices.Count;

    public Vertex[] ToArray() => [.. _vertices];

    /// <summary>Un triangle, sa normale tournée à l'opposé de <paramref name="inside"/>.</summary>
    public void Tri(float3 a, float3 b, float3 c, float3 inside, float4 color, float emissive = 0)
    {
        var n = Vec.Cross(b - a, c - a);
        var len = Vec.Len(n);
        if (len < 1e-9f)
            return;
        n /= len;
        if (Vec.Dot(n, (a + b + c) / 3 - inside) < 0)
        {
            n = -n;
            (b, c) = (c, b);
        }
        _vertices.Add(new Vertex(a, n, color, emissive));
        _vertices.Add(new Vertex(b, n, color, emissive));
        _vertices.Add(new Vertex(c, n, color, emissive));
    }

    public void Quad(float3 a, float3 b, float3 c, float3 d, float3 inside, float4 color, float emissive = 0)
    {
        Tri(a, b, c, inside, color, emissive);
        Tri(a, c, d, inside, color, emissive);
    }

    /// <summary>Pavé aligné sur les axes du maillage.</summary>
    public void Box(float3 center, float3 half, float4 color, float emissive = 0)
    {
        Span<float3> p = stackalloc float3[8];
        for (var i = 0; i < 8; i++)
            p[i] = center + new float3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
        Quad(p[0], p[1], p[3], p[2], center, color, emissive); // -Z
        Quad(p[4], p[5], p[7], p[6], center, color, emissive); // +Z
        Quad(p[0], p[1], p[5], p[4], center, color, emissive); // -Y
        Quad(p[2], p[3], p[7], p[6], center, color, emissive); // +Y
        Quad(p[0], p[2], p[6], p[4], center, color, emissive); // -X
        Quad(p[1], p[3], p[7], p[5], center, color, emissive); // +X
    }

    /// <summary>Coin : un pavé dont la face +Z est rétrécie (proues, ailerons, mâchoires).</summary>
    public void Wedge(float3 center, float3 half, float frontScaleX, float frontScaleY, float4 color, float frontDrop = 0)
    {
        Span<float3> p = stackalloc float3[8];
        for (var i = 0; i < 8; i++)
        {
            var front = (i & 4) != 0;
            var sx = front ? frontScaleX : 1;
            var sy = front ? frontScaleY : 1;
            p[i] = center + new float3(((i & 1) == 0 ? -half.X : half.X) * sx, ((i & 2) == 0 ? -half.Y : half.Y) * sy - (front ? frontDrop : 0),
                (i & 4) == 0 ? -half.Z : half.Z);
        }
        Quad(p[0], p[1], p[3], p[2], center, color);
        Quad(p[4], p[5], p[7], p[6], center, color);
        Quad(p[0], p[1], p[5], p[4], center, color);
        Quad(p[2], p[3], p[7], p[6], center, color);
        Quad(p[0], p[2], p[6], p[4], center, color);
        Quad(p[1], p[3], p[7], p[5], center, color);
    }

    /// <summary>Cylindre (ou prisme) le long de <paramref name="axis"/>, fermé ; l'extrémité avant peut briller (tuyère, bouche).</summary>
    public void Cylinder(float3 start, float3 axis, float radius, float length, int segments, float4 color,
        float4? endColor = null, float endEmissive = 0, float endRadius = -1)
    {
        axis = Vec.Normalize(axis);
        var u = Vec.Normalize(Vec.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vec.Up : new float3(1, 0, 0)));
        var v = Vec.Cross(axis, u);
        var end = start + axis * length;
        var r1 = endRadius < 0 ? radius : endRadius;
        var inside = (start + end) / 2;
        for (var i = 0; i < segments; i++)
        {
            var a0 = MathF.Tau * i / segments;
            var a1 = MathF.Tau * (i + 1) / segments;
            var d0 = u * MathF.Cos(a0) + v * MathF.Sin(a0);
            var d1 = u * MathF.Cos(a1) + v * MathF.Sin(a1);
            Quad(start + d0 * radius, start + d1 * radius, end + d1 * r1, end + d0 * r1, inside, color);
            Tri(start, start + d0 * radius, start + d1 * radius, inside, color);
            Tri(end, end + d0 * r1, end + d1 * r1, inside, endColor ?? color, endEmissive);
        }
    }

    /// <summary>Une section de coque : position le long du nez, demi-largeur, hauteur au-dessus et sous l'axe, chanfrein (0 à 1).</summary>
    public readonly record struct Section(float Z, float HalfWidth, float Top, float Bottom, float Chamfer);

    /// <summary>
    /// Coque lissée de section en section : un octogone chanfreiné par section, des bandes de quadrilatères entre
    /// elles, et les deux bouts fermés. <paramref name="trim"/> colore les flancs (liseré du camp) quand il est donné.
    /// </summary>
    public void Loft(ReadOnlySpan<Section> sections, float3 offset, float4 color, float4? trim = null, float4? belly = null)
    {
        Span<float3> ring0 = stackalloc float3[8];
        Span<float3> ring1 = stackalloc float3[8];
        for (var i = 0; i + 1 < sections.Length; i++)
        {
            Ring(sections[i], offset, ring0);
            Ring(sections[i + 1], offset, ring1);
            var inside = offset + new float3(0, (sections[i].Top - sections[i].Bottom + sections[i + 1].Top - sections[i + 1].Bottom) / 4,
                (sections[i].Z + sections[i + 1].Z) / 2);
            for (var k = 0; k < 8; k++)
            {
                var k1 = (k + 1) % 8;
                // 1-2 dessus, 0/3/4/7 flancs, 5-6 dessous.
                var c = k is 1 ? color : k is 5 ? belly ?? color : trim is { } t && k is 0 or 3 ? t : color;
                Quad(ring0[k], ring0[k1], ring1[k1], ring1[k], inside, c);
            }
        }
        Cap(sections[0], offset, color, ring0);
        Cap(sections[^1], offset, color, ring1);
    }

    void Cap(Section s, float3 offset, float4 color, Span<float3> ring)
    {
        Ring(s, offset, ring);
        var center = offset + new float3(0, (s.Top - s.Bottom) / 2, s.Z);
        var inside = center + new float3(0, 0, s.Z > 0 ? -1 : 1);
        for (var k = 0; k < 8; k++)
            Tri(center, ring[k], ring[(k + 1) % 8], inside, color);
    }

    static void Ring(Section s, float3 offset, Span<float3> ring)
    {
        var c = 1 - s.Chamfer;
        var hw = s.HalfWidth;
        ring[0] = offset + new float3(hw, s.Top * 0.35f, s.Z);
        ring[1] = offset + new float3(hw * c, s.Top, s.Z);
        ring[2] = offset + new float3(-hw * c, s.Top, s.Z);
        ring[3] = offset + new float3(-hw, s.Top * 0.35f, s.Z);
        ring[4] = offset + new float3(-hw, -s.Bottom * 0.35f, s.Z);
        ring[5] = offset + new float3(-hw * c, -s.Bottom, s.Z);
        ring[6] = offset + new float3(hw * c, -s.Bottom, s.Z);
        ring[7] = offset + new float3(hw, -s.Bottom * 0.35f, s.Z);
    }

    /// <summary>Le profil de la coque interpolé à la position <paramref name="z"/> (pour poser socles et détails dessus).</summary>
    public static Section ProfileAt(ReadOnlySpan<Section> sections, float z)
    {
        if (z <= sections[0].Z)
            return sections[0];
        for (var i = 0; i + 1 < sections.Length; i++)
        {
            var a = sections[i];
            var b = sections[i + 1];
            if (z > b.Z)
                continue;
            var t = (z - a.Z) / MathF.Max(1e-3f, b.Z - a.Z);
            return new Section(z, a.HalfWidth + (b.HalfWidth - a.HalfWidth) * t, a.Top + (b.Top - a.Top) * t,
                a.Bottom + (b.Bottom - a.Bottom) * t, a.Chamfer + (b.Chamfer - a.Chamfer) * t);
        }
        return sections[^1];
    }
}
