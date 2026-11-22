using Brutal.Numerics;
using OrionumFleet.Sim;
using Section = OrionumFleet.Viewer.Rendering.MeshBuilder.Section;

namespace OrionumFleet.Viewer.Rendering;

/// <summary>Une tourelle posée sur un modèle : arme, index de tourelle, normale de pose (repère de la coque), échelle, maillage.</summary>
readonly record struct TurretMount(int Weapon, int Turret, float3 Normal, float Scale, Mesh Mesh);

/// <summary>Le modèle d'une classe : coque (en mètres, repère +X bâbord, +Y dorsal, +Z nez), gabarit et tourelles.</summary>
sealed class ShipModel
{
    public required Mesh Hull { get; init; }
    public required float HalfWidth { get; init; }
    public required float Top { get; init; }
    public required float Bottom { get; init; }
    public required TurretMount[] Turrets { get; init; }
}

/// <summary>
/// Modèles procéduraux des capitaux (décisions du 2026-11-21), générés une fois par classe à partir des données de
/// la sim : la coque enveloppe les tourelles de la classe, qui reposent sur elle ou sur un socle.
/// <list type="bullet">
/// <item>Coalition : coques effilées et claires en pointe de flèche, arête dorsale, passerelle arrière à hublots,
/// ailerons, nacelles bleues ; l'amiral porte une proue fourchue autour de son canon axial, le porteur un pont d'envol.</item>
/// <item>Clans : caissons bruts et rouillés à proue en bélier, plaques de blindage décalées, tour excentrée, poutrelles
/// apparentes, blocs moteurs orange ; l'amiral ouvre une mâchoire autour du Marteau, le porteur une ruche percée de hangars.</item>
/// </list>
/// </summary>
sealed class ShipModels
{
    enum TurretKind
    {
        Heavy,
        Lance,
        Missile,
        Pd,
        Light,
    }

    readonly Dictionary<ShipClassId, ShipModel> _models = [];
    readonly Dictionary<(Side, TurretKind), Mesh> _turrets = [];

    public ShipModels(Renderer renderer)
    {
        foreach (var side in (Side[])[Side.Coalition, Side.Clans])
            foreach (var kind in Enum.GetValues<TurretKind>())
                _turrets[(side, kind)] = renderer.CreateMesh(BuildTurret(side, kind));
        foreach (var cls in CapitalData.Classes)
            _models[cls.Id] = Build(renderer, cls);
    }

    public ShipModel? For(ShipClassId id) => _models.GetValueOrDefault(id);

    // --- Palette ----------------------------------------------------------------------------

    // Couleurs sRGB ; w = pas des panneaux de blindage (m), 0 = lisse.
    static float4 Mat(float r, float g, float b, float panel) => new(r, g, b, panel);

    static float4 Hull(Side side, float panel) => side == Side.Coalition ? Mat(0.8f, 0.82f, 0.86f, panel) : Mat(0.46f, 0.36f, 0.29f, panel);
    static float4 Dark(Side side, float panel) => side == Side.Coalition ? Mat(0.36f, 0.4f, 0.47f, panel) : Mat(0.25f, 0.22f, 0.21f, panel);
    static float4 Trim(Side side) => side == Side.Coalition ? Mat(0.22f, 0.38f, 0.72f, 0) : Mat(0.78f, 0.42f, 0.1f, 0);
    static float4 Light(Side side) => side == Side.Coalition ? Mat(0.55f, 0.78f, 1f, 0) : Mat(1f, 0.55f, 0.18f, 0);
    static readonly float4 Window = Mat(1f, 0.88f, 0.62f, 0);
    static readonly float4 Void = Mat(0.03f, 0.03f, 0.035f, 0);

    // --- Coques -----------------------------------------------------------------------------

    /// <summary>Gabarit tiré des tourelles : la coque doit envelopper les bordées et rester sous les tourelles dorsales.</summary>
    static (float HalfWidth, float Top, float Bottom, float DorsalMax) Gauge(ShipClassDef cls)
    {
        float side = 0, minTop = float.PositiveInfinity, dorsalMax = 0;
        foreach (var w in cls.Weapons)
            foreach (var p in w.Hardpoints ?? [])
            {
                if (MathF.Abs(p.X) > MathF.Abs(p.Y))
                    side = MathF.Max(side, MathF.Abs(p.X));
                else if (p.Y > 0)
                {
                    minTop = MathF.Min(minTop, p.Y);
                    dorsalMax = MathF.Max(dorsalMax, p.Y);
                }
            }
        var r = cls.Radius;
        var carrier = cls.Role == Role.Carrier;
        var halfWidth = side > 0 ? side * 0.94f : r * (carrier ? 0.34f : 0.26f);
        var top = float.IsFinite(minTop) ? minTop * 0.85f : halfWidth * 0.45f;
        var bottom = top * (cls.Side == Side.Coalition ? 0.72f : 0.95f);
        return (halfWidth, top, bottom, MathF.Max(dorsalMax, top));
    }

    ShipModel Build(Renderer renderer, ShipClassDef cls)
    {
        var b = new MeshBuilder();
        var (hw, top, bottom, dorsal) = Gauge(cls);
        var r = cls.Radius;
        var panel = Math.Clamp(r * 0.018f, 3, 22);
        var rng = new Random((int)cls.Id * 7919 + 17);
        var sections = cls.Side == Side.Coalition
            ? CoalitionHull(b, cls, hw, top, bottom, dorsal, panel, rng)
            : ClansHull(b, cls, hw, top, bottom, dorsal, panel, rng);

        // Socles : chaque tourelle repose sur la coque (ou sur l'arête dorsale) ; le vide est comblé par un fût.
        var turretScale = TurretScale(cls);
        var mounts = new List<TurretMount>();
        for (var wi = 0; wi < cls.Weapons.Length; wi++)
        {
            var def = cls.Weapons[wi];
            if (def.Hardpoints is null || def.Id == WeaponId.Spinal)
                continue;
            var kind = KindOf(def);
            var scale = turretScale * kind switch { TurretKind.Lance => 1.25f, TurretKind.Missile => 0.9f, TurretKind.Pd => 0.42f, _ => 1f };
            for (var t = 0; t < def.Hardpoints.Length; t++)
            {
                var p = def.Hardpoints[t];
                var sideMount = MathF.Abs(p.X) > MathF.Abs(p.Y);
                var normal = sideMount ? new float3(MathF.Sign(p.X), 0, 0) : new float3(0, p.Y >= 0 ? 1 : -1, 0);
                var profile = MeshBuilder.ProfileAt(sections, p.Z);
                var surface = sideMount ? profile.HalfWidth : p.Y >= 0 ? profile.Top : profile.Bottom;
                var reach = sideMount ? MathF.Abs(p.X) : MathF.Abs(p.Y);
                var gap = reach - surface;
                if (gap > scale * 0.15f)
                {
                    var mid = p - normal * (gap / 2 + scale * 0.05f);
                    var half = sideMount ? new float3(gap / 2 + scale * 0.05f, scale * 0.4f, scale * 0.5f)
                        : new float3(scale * 0.4f, gap / 2 + scale * 0.05f, scale * 0.5f);
                    b.Box(mid, half, Dark(cls.Side, panel));
                }
                mounts.Add(new TurretMount(wi, t, normal, scale, _turrets[(cls.Side, kind)]));
            }
        }

        return new ShipModel { Hull = renderer.CreateMesh(b.ToArray()), HalfWidth = hw, Top = top, Bottom = bottom, Turrets = [.. mounts] };
    }

    static float TurretScale(ShipClassDef cls) => Math.Clamp(cls.Radius * 0.045f, 5, 70);

    static TurretKind KindOf(WeaponDef def) => def.Kind switch
    {
        WeaponKind.Pd => TurretKind.Pd,
        WeaponKind.Missile => TurretKind.Missile,
        _ when def.Id == WeaponId.Lance => TurretKind.Lance,
        _ when def.Id is WeaponId.Laser or WeaponId.Autocannon => TurretKind.Light,
        _ => TurretKind.Heavy,
    };

    static Section S(float r, float t, float hw, float top, float bottom, float chamfer) => new(-r + 2 * r * t, hw, top, bottom, chamfer);

    /// <summary>Coalition : pointe de flèche effilée, arête dorsale, passerelle arrière, ailerons, nacelles bleues.</summary>
    static Section[] CoalitionHull(MeshBuilder b, ShipClassDef cls, float hw, float top, float bottom, float dorsal, float panel, Random rng)
    {
        var r = cls.Radius;
        var side = cls.Side;
        var hull = Hull(side, panel);
        var dark = Dark(side, panel);
        var capital = cls.Radius >= 300;
        Section[] sections =
        [
            S(r, 0.00f, hw * 0.62f, top * 0.55f, bottom * 0.6f, 0.4f),
            S(r, 0.05f, hw * 0.82f, top * 0.8f, bottom * 0.82f, 0.38f),
            S(r, 0.18f, hw, top, bottom, 0.35f),
            S(r, 0.55f, hw, top, bottom * 0.95f, 0.35f),
            S(r, 0.74f, hw * 0.74f, top * 0.86f, bottom * 0.76f, 0.38f),
            S(r, 0.90f, hw * 0.36f, top * 0.6f, bottom * 0.45f, 0.45f),
            S(r, 1.00f, hw * 0.05f, top * 0.22f, bottom * 0.15f, 0.5f),
        ];
        b.Loft(sections, float3.Zero, hull, Trim(side), dark);

        // Arête dorsale : porte les tourelles dorsales et les lances.
        var ridgeTop = MathF.Max(top * 1.25f, dorsal * 0.93f);
        if (capital || ridgeTop > top * 1.1f)
        {
            b.Loft(
            [
                S(r, 0.12f, hw * 0.2f, top * 0.9f, 0, 0.5f),
                S(r, 0.2f, hw * 0.34f, ridgeTop, 0, 0.42f),
                S(r, 0.62f, hw * 0.34f, ridgeTop, 0, 0.42f),
                S(r, 0.8f, hw * 0.12f, top * 0.95f, 0, 0.5f),
            ], float3.Zero, hull, null, dark);
        }

        // Passerelle arrière : tour, pont large et rangées de hublots.
        var towerZ = -r + 2 * r * 0.14f;
        var towerH = MathF.Max(ridgeTop * 1.55f, top * 2.2f);
        b.Wedge(new float3(0, (top + towerH) / 2, towerZ), new float3(hw * 0.22f, (towerH - top) / 2, r * 0.07f), 0.7f, 0.85f, hull);
        b.Box(new float3(0, towerH + r * 0.012f, towerZ - r * 0.01f), new float3(hw * 0.42f, r * 0.012f, r * 0.045f), dark);
        b.Box(new float3(0, towerH + r * 0.012f, towerZ + r * 0.036f), new float3(hw * 0.38f, r * 0.004f, r * 0.002f), Window, 3);
        WindowRows(b, r, hw, top, 0.22f, 0.66f, capital ? 3 : 1, rng);

        // Ailerons arrière (une jupe en delta de chaque bord), feux de position aux pointes.
        if (cls.Role != Role.Brawler || r > 60)
        {
            foreach (var sx in (ReadOnlySpan<float>)[1, -1])
            {
                var center = new float3(sx * hw * 1.18f, -bottom * 0.15f, -r + 2 * r * 0.16f);
                b.Wedge(center, new float3(hw * 0.32f, MathF.Max(1, top * 0.09f), r * 0.15f), 0.35f, 1, hull);
                b.Box(center + new float3(sx * hw * 0.3f, 0, -r * 0.12f), new float3(r * 0.006f, r * 0.006f, r * 0.006f),
                    sx > 0 ? Mat(1f, 0.15f, 0.1f, 0) : Mat(0.2f, 1f, 0.3f, 0), 8);
            }
        }

        // Moteurs : nacelles cylindriques à l'arrière, bouche bleue brillante.
        var engines = cls.Role switch { Role.Capital => 5, Role.Carrier => 4, Role.Line => 3, _ => 2 };
        var engineR = MathF.Min(hw, top) * (engines > 3 ? 0.32f : 0.42f);
        for (var i = 0; i < engines; i++)
        {
            var x = (i - (engines - 1) / 2f) * engineR * 2.3f;
            var y = engines > 3 && i % 2 == 1 ? -engineR * 0.4f : engineR * 0.1f;
            b.Cylinder(new float3(x, y, -r + r * 0.06f), new float3(0, 0, -1), engineR, r * 0.08f, 14, dark, Light(side), 2.5f, engineR * 0.85f);
        }

        // Pièces propres.
        if (cls.Id == ShipClassId.Souverain)
        {
            // Proue fourchue autour de l'émetteur axial, anneau bleu.
            foreach (var sx in (ReadOnlySpan<float>)[1, -1])
                b.Wedge(new float3(sx * hw * 0.32f, top * 0.1f, r * 0.86f), new float3(hw * 0.12f, top * 0.32f, r * 0.17f), 0.4f, 0.5f, hull);
            b.Cylinder(new float3(0, 0, r * 0.7f), new float3(0, 0, 1), hw * 0.14f, r * 0.27f, 18, dark, Light(side), 10, hw * 0.1f);
        }
        if (cls.Role == Role.Carrier)
        {
            // Pont d'envol : une plaque sombre sur le dos, deux pistes balisées.
            b.Box(new float3(0, top * 1.02f, r * 0.15f), new float3(hw * 0.75f, top * 0.03f, r * 0.55f), dark);
            for (var k = -6; k <= 6; k++)
                foreach (var sx in (ReadOnlySpan<float>)[0.35f, -0.35f])
                    b.Box(new float3(sx * hw, top * 1.06f, r * (0.15f + k * 0.08f)), new float3(r * 0.004f, r * 0.002f, r * 0.012f), Light(side), 4);
            HangarMouths(b, cls, Light(side));
        }

        Greebles(b, sections, r, hw, rng, dark, cls.Radius >= 300 ? (int)(r / 5) : 12);
        return sections;
    }

    /// <summary>Clans : caisson brut à proue en bélier, plaques décalées, tour excentrée, poutrelles, moteurs orange.</summary>
    static Section[] ClansHull(MeshBuilder b, ShipClassDef cls, float hw, float top, float bottom, float dorsal, float panel, Random rng)
    {
        var r = cls.Radius;
        var side = cls.Side;
        var hull = Hull(side, panel);
        var dark = Dark(side, panel);
        var capital = cls.Radius >= 300;
        Section[] sections =
        [
            S(r, 0.00f, hw * 0.92f, top * 0.92f, bottom, 0.12f),
            S(r, 0.08f, hw, top, bottom, 0.1f),
            S(r, 0.62f, hw, top, bottom, 0.1f),
            S(r, 0.82f, hw * 0.9f, top * 0.95f, bottom * 0.98f, 0.14f),
            S(r, 0.95f, hw * 0.74f, top * 0.8f, bottom * 0.84f, 0.18f),
            S(r, 1.00f, hw * 0.68f, top * 0.62f, bottom * 0.7f, 0.22f),
        ];
        b.Loft(sections, float3.Zero, hull, Trim(side), dark);

        // Plaques de blindage décalées le long des flancs (asymétriques).
        var plates = capital ? 7 : 3;
        foreach (var sx in (ReadOnlySpan<float>)[1, -1])
            for (var i = 0; i < plates; i++)
            {
                var t = 0.1f + 0.7f * (i + 0.5f) / plates;
                var h = top * (0.5f + 0.4f * (float)rng.NextDouble());
                var y = -bottom * 0.2f + (float)(rng.NextDouble() - 0.5) * top * 0.4f;
                b.Box(new float3(sx * (hw + r * 0.012f), y, -r + 2 * r * t), new float3(r * 0.014f, h * 0.6f, r * 0.7f / plates * 0.9f), dark);
            }

        // Arête et poutrelles apparentes sur le dos.
        var ridgeTop = MathF.Max(top * 1.15f, dorsal * 0.93f);
        if (capital)
        {
            b.Box(new float3(hw * 0.12f, (top + ridgeTop) / 2, -r * 0.05f), new float3(hw * 0.3f, (ridgeTop - top) / 2 + top * 0.02f, r * 0.62f), hull);
            foreach (var sx in (ReadOnlySpan<float>)[0.55f, -0.45f])
                b.Box(new float3(sx * hw, top + r * 0.02f, 0), new float3(r * 0.006f, r * 0.006f, r * 0.75f), dark);
            for (var k = -6; k <= 6; k++)
                b.Box(new float3(0.05f * hw, top + r * 0.02f, k * r * 0.11f), new float3(hw * 0.5f, r * 0.005f, r * 0.005f), dark);
        }

        // Tour excentrée côté bâbord, hublots orange.
        var towerZ = -r + 2 * r * 0.2f;
        var towerH = MathF.Max(ridgeTop * 1.5f, top * 2.4f);
        var towerX = hw * 0.45f;
        b.Box(new float3(towerX, (top + towerH) / 2, towerZ), new float3(hw * 0.2f, (towerH - top) / 2, r * 0.06f), hull);
        b.Box(new float3(towerX + hw * 0.05f, towerH + r * 0.015f, towerZ), new float3(hw * 0.3f, r * 0.015f, r * 0.04f), dark);
        b.Box(new float3(towerX + hw * 0.05f, towerH + r * 0.015f, towerZ + r * 0.041f), new float3(hw * 0.26f, r * 0.005f, r * 0.002f), Window, 3);
        WindowRows(b, r, hw, top, 0.18f, 0.6f, capital ? 2 : 1, rng, Mat(1f, 0.6f, 0.25f, 0));

        // Blocs moteurs carrés, grosses tuyères orange.
        var engines = cls.Role == Role.Capital ? 4 : cls.Role == Role.Brawler ? 2 : 3;
        var engineR = MathF.Min(hw, top) * 0.42f;
        b.Box(new float3(0, 0, -r + r * 0.03f), new float3(hw * 0.95f, top * 0.8f, r * 0.05f), dark);
        for (var i = 0; i < engines; i++)
        {
            var x = (i - (engines - 1) / 2f) * engineR * 2.4f;
            b.Cylinder(new float3(x, 0, -r - r * 0.01f), new float3(0, 0, -1), engineR, r * 0.06f, 8, dark, Light(side), 2.5f, engineR * 1.1f);
        }

        // Pièces propres.
        if (cls.Id == ShipClassId.Gouffre)
        {
            // La mâchoire : deux mandibules au-dessus et au-dessous du Marteau, dont le canon dépasse au fond.
            b.Wedge(new float3(0, top * 0.55f, r * 0.88f), new float3(hw * 0.8f, top * 0.32f, r * 0.2f), 0.55f, 0.4f, hull, top * 0.15f);
            b.Wedge(new float3(0, -bottom * 0.55f, r * 0.86f), new float3(hw * 0.75f, bottom * 0.3f, r * 0.2f), 0.5f, 0.4f, hull, -bottom * 0.1f);
            b.Cylinder(new float3(0, 0, r * 0.55f), new float3(0, 0, 1), hw * 0.2f, r * 0.42f, 12, dark, Mat(1f, 0.4f, 0.1f, 0), 8, hw * 0.24f);
        }
        if (cls.Role == Role.Brawler)
        {
            // Griffes avant de la frégate.
            foreach (var sx in (ReadOnlySpan<float>)[1, -1])
                b.Wedge(new float3(sx * hw * 0.7f, 0, r * 0.95f), new float3(hw * 0.2f, top * 0.5f, r * 0.25f), 0.3f, 0.4f, dark);
        }
        if (cls.Role == Role.Carrier)
        {
            // La ruche : un dôme bosselé sur le dos, percé d'alvéoles qui luisent.
            b.Loft(
            [
                S(r, 0.18f, hw * 0.5f, top * 1.0f, 0, 0.5f),
                S(r, 0.3f, hw * 0.85f, top * 1.9f, 0, 0.45f),
                S(r, 0.62f, hw * 0.85f, top * 1.9f, 0, 0.45f),
                S(r, 0.75f, hw * 0.45f, top * 1.0f, 0, 0.5f),
            ], float3.Zero, hull, null, dark);
            for (var k = 0; k < 36; k++)
            {
                var t = 0.32f + 0.28f * (float)rng.NextDouble();
                var sx = rng.Next(2) == 0 ? 1 : -1;
                b.Box(new float3(sx * hw * (0.55f + 0.25f * (float)rng.NextDouble()), top * (1.1f + 0.6f * (float)rng.NextDouble()), -r + 2 * r * t),
                    new float3(r * 0.01f, r * 0.012f, r * 0.012f), Light(side), 3);
            }
            HangarMouths(b, cls, Light(side));
        }

        Greebles(b, sections, r, hw, rng, dark, capital ? (int)(r / 4) : 10);
        return sections;
    }

    /// <summary>Rangées de hublots sur les flancs (des points chauds qui donnent l'échelle).</summary>
    static void WindowRows(MeshBuilder b, float r, float hw, float top, float t0, float t1, int rows, Random rng, float4? color = null)
    {
        var count = (int)(r / 12);
        for (var row = 0; row < rows; row++)
        {
            var y = top * (0.05f + 0.22f * row);
            for (var i = 0; i < count; i++)
            {
                if (rng.NextDouble() < 0.25)
                    continue; // des hublots éteints
                var z = -r + 2 * r * (t0 + (t1 - t0) * i / count);
                foreach (var sx in (ReadOnlySpan<float>)[1, -1])
                    b.Box(new float3(sx * (hw + 0.4f), y, z), new float3(0.6f, MathF.Max(0.8f, r * 0.0025f), MathF.Max(1.2f, r * 0.004f)),
                        color ?? Window, 2.5f);
            }
        }
    }

    /// <summary>Bouches de hangar aux baies du porteur : une ouverture sombre au fond éclairé.</summary>
    static void HangarMouths(MeshBuilder b, ShipClassDef cls, float4 glow)
    {
        var r = cls.Radius;
        foreach (var bay in cls.Hangar?.Bays ?? [])
        {
            var p = bay.P * r;
            var n = bay.N;
            b.Box(p + n * (r * 0.005f), new float3(MathF.Abs(n.X) > 0.5f ? r * 0.006f : r * 0.07f, r * 0.03f, r * 0.08f), Void);
            b.Box(p + n * (r * 0.009f), new float3(MathF.Abs(n.X) > 0.5f ? r * 0.002f : r * 0.06f, r * 0.004f, r * 0.07f), glow, 2.5f);
        }
    }

    /// <summary>Détails de surface : petits blocs (antennes, prises, caissons) semés sur le dos et les flancs.</summary>
    static void Greebles(MeshBuilder b, Section[] sections, float r, float hw, Random rng, float4 color, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var z = -r * 0.9f + 1.7f * r * (float)rng.NextDouble();
            var profile = MeshBuilder.ProfileAt(sections, z);
            var size = r * (0.004f + 0.012f * (float)rng.NextDouble());
            var onTop = rng.NextDouble() < 0.6;
            if (onTop)
            {
                var x = (float)(rng.NextDouble() * 2 - 1) * profile.HalfWidth * (1 - profile.Chamfer) * 0.9f;
                b.Box(new float3(x, profile.Top + size * 0.5f, z), new float3(size * (0.5f + (float)rng.NextDouble()), size * 0.5f, size * (0.5f + 2 * (float)rng.NextDouble())), color);
            }
            else
            {
                var sx = rng.Next(2) == 0 ? 1 : -1;
                var y = (float)(rng.NextDouble() * 0.6 - 0.3) * profile.Top;
                b.Box(new float3(sx * (profile.HalfWidth + size * 0.4f), y, z), new float3(size * 0.4f, size * 0.6f, size * (0.6f + 2 * (float)rng.NextDouble())), color);
            }
        }
    }

    // --- Tourelles (échelle 1 : socle au sol, canons vers +Z, haut vers +Y) ---------------------------

    static Vertex[] BuildTurret(Side side, TurretKind kind)
    {
        var b = new MeshBuilder();
        var hull = Hull(side, 0);
        var dark = Dark(side, 0);
        var coalition = side == Side.Coalition;
        switch (kind)
        {
            case TurretKind.Heavy:
                b.Cylinder(float3.Zero, Vec.Up, 0.5f, 0.18f, coalition ? 16 : 8, dark);
                if (coalition)
                    b.Wedge(new float3(0, 0.32f, 0), new float3(0.42f, 0.16f, 0.48f), 0.75f, 0.7f, hull);
                else
                    b.Box(new float3(0, 0.32f, -0.05f), new float3(0.48f, 0.18f, 0.48f), hull);
                var barrels = coalition ? 2 : 3;
                for (var i = 0; i < barrels; i++)
                {
                    var x = (i - (barrels - 1) / 2f) * 0.22f;
                    b.Cylinder(new float3(x, 0.34f, 0.3f), new float3(0, 0, 1), coalition ? 0.06f : 0.08f, coalition ? 1.15f : 0.95f, 8, dark,
                        Void, 0, coalition ? 0.05f : 0.08f);
                }
                break;
            case TurretKind.Lance:
                b.Cylinder(float3.Zero, Vec.Up, 0.45f, 0.25f, 16, dark);
                b.Wedge(new float3(0, 0.45f, 0), new float3(0.22f, 0.25f, 0.3f), 0.6f, 0.6f, hull);
                b.Cylinder(new float3(0, 0.55f, 0.2f), new float3(0, 0, 1), 0.14f, 1.0f, 12, dark, Light(side), 8, 0.09f);
                b.Box(new float3(0, 0.55f, 0.9f), new float3(0.05f, 0.05f, 0.25f), Light(side), 5);
                break;
            case TurretKind.Missile:
                b.Box(new float3(0, 0.12f, 0), new float3(0.45f, 0.12f, 0.55f), hull);
                for (var x = -1; x <= 1; x++)
                    for (var z = -1; z <= 1; z++)
                        b.Box(new float3(x * 0.28f, 0.245f, z * 0.34f), new float3(0.1f, 0.01f, 0.12f), coalition ? Void : Mat(0.35f, 0.08f, 0.05f, 0));
                break;
            case TurretKind.Pd:
                b.Cylinder(float3.Zero, Vec.Up, 0.45f, 0.35f, 10, dark, hull, 0, 0.3f);
                b.Cylinder(new float3(0, 0.3f, 0.1f), new float3(0, 0, 1), 0.07f, 0.8f, 6, dark);
                break;
            default:
                b.Cylinder(float3.Zero, Vec.Up, 0.4f, 0.2f, 10, dark);
                b.Box(new float3(0, 0.3f, 0), new float3(0.3f, 0.12f, 0.35f), hull);
                b.Cylinder(new float3(0, 0.32f, 0.2f), new float3(0, 0, 1), 0.06f, 0.8f, 6, dark, coalition ? Light(side) : Void, coalition ? 3 : 0);
                break;
        }
        return b.ToArray();
    }

    // --- Repères ------------------------------------------------------------------------------

    /// <summary>Matrice du modèle d'une coque : les axes du maillage (+X bâbord, +Y dorsal, +Z nez) sur ceux de la sim.</summary>
    public static float4x4 HullMatrix(in Ship s)
    {
        var f = Vec.FrameFromForward(Vec.LenSq(s.Fwd) > 1e-6f ? s.Fwd : new float3(0, 0, 1));
        return Axes(-f.Right, f.Up, f.Fwd, s.Pos, 1);
    }

    /// <summary>Matrice d'une tourelle : posée selon sa normale, tournée vers <paramref name="aim"/> dans son plan de pose.</summary>
    public static float4x4 TurretMatrix(in Ship s, in TurretMount mount, float3 local, float3? aim)
    {
        var f = Vec.FrameFromForward(Vec.LenSq(s.Fwd) > 1e-6f ? s.Fwd : new float3(0, 0, 1));
        var up = Vec.Normalize(-f.Right * mount.Normal.X + f.Up * mount.Normal.Y + f.Fwd * mount.Normal.Z);
        var pos = Batteries.HardpointWorld(s, local);
        var dir = aim is { } a ? a - pos : f.Fwd;
        var flat = dir - up * Vec.Dot(dir, up);
        if (Vec.LenSq(flat) < 1e-6f)
            flat = f.Fwd - up * Vec.Dot(f.Fwd, up);
        if (Vec.LenSq(flat) < 1e-6f)
            flat = f.Up;
        var z = Vec.Normalize(flat);
        var x = Vec.Cross(up, z);
        return Axes(x, up, z, pos, mount.Scale);
    }

    static float4x4 Axes(float3 x, float3 y, float3 z, float3 pos, float scale) => new(
        x.X * scale, x.Y * scale, x.Z * scale, 0,
        y.X * scale, y.Y * scale, y.Z * scale, 0,
        z.X * scale, z.Y * scale, z.Z * scale, 0,
        pos.X, pos.Y, pos.Z, 1);
}
