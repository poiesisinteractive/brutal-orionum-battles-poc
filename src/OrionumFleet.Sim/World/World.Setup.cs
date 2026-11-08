using Brutal.Numerics;

namespace OrionumFleet.Sim;

public sealed partial class World
{
    /// <summary>Coque totale de la flotte Coalition de l'Escarmouche (la référence du raid) et croissance de la Tanière.</summary>
    const double DepotRefFleetHull = 9289;
    const double DepotScaleExp = 0.5;
    /// <summary>Au-delà de la flotte « Bataille », la puissance de feu des raiders croît plus vite : mise à l'échelle plus raide.</summary>
    const double DepotLargeExp = 1.3;
    const double DepotLargeFrom = 2.53;

    /// <summary>Rang de déploiement par rôle (z, vers l'ennemi) quand les postes classiques ne suffisent pas.</summary>
    static float RankZ(Role role) => role switch
    {
        Role.Brawler => 300,
        Role.Escort => 160,
        Role.Line => 90,
        Role.Convoy => -60,
        Role.Support => -200,
        Role.Carrier => -340,
        Role.Artillery => -560,
        _ => 0,
    };

    /// <summary>Postes de formation d'origine (local : x à droite, z vers l'ennemi), tant qu'ils suffisent.</summary>
    static float3[]? ClassicSlots(Role role) => role switch
    {
        Role.Capital => [new(0, 0, 0)],
        Role.Carrier => [new(-160, 30, -220), new(160, -20, -240)],
        Role.Brawler => [new(-260, 10, 180), new(260, -10, 180), new(0, 40, 260)],
        Role.Escort => [new(90, 0, -120)],
        _ => null,
    };

    /// <summary>
    /// Déploie les deux flottes en rangs par rôle, chaque rang étalé en travers de l'axe, puis la Tanière
    /// (port de <c>setupScenario</c>). Les rôles sont déployés dans l'ordre de leur première apparition
    /// dans la flotte, comme le <c>Map</c> du TS : l'ordre des ids et des tirages RNG en dépend.
    /// </summary>
    void SetupScenario(ShipClassId[] coalition, ShipClassId[] clans)
    {
        Span<short> nameIndex = stackalloc short[GameData.Classes.Length];
        foreach (var side in (ReadOnlySpan<Side>)[Side.Coalition, Side.Clans])
        {
            var fleet = side == Side.Coalition ? coalition : clans;
            nameIndex.Fill(-1);
            var basePos = Home(side);
            var facing = side == Side.Coalition ? 1f : -1f;

            var roles = new List<Role>();
            foreach (var classId in fleet)
            {
                var role = GameData.Class(classId).Role;
                if (role != Role.Depot && !roles.Contains(role))
                    roles.Add(role);
            }

            foreach (var role in roles)
            {
                var ids = fleet.Where(c => GameData.Class(c).Role == role).ToArray();
                var gap = ids.Max(c => GameData.Class(c).Radius) * 2 + 80;
                // Les petites flottes gardent les postes placés à la main (la calibration de l'Escarmouche).
                var classic = ClassicSlots(role);
                var useClassic = classic is not null && ids.Length <= classic.Length;
                for (var i = 0; i < ids.Length; i++)
                {
                    var classId = ids[i];
                    var x = (i - (ids.Length - 1) / 2f) * gap;
                    var y = (i % 3 - 1) * 30f;
                    var slot = useClassic ? classic![i] : new float3(x, y, RankZ(role));
                    var pos = basePos + new float3(slot.X * facing, slot.Y, slot.Z * facing);
                    var name = ++nameIndex[(int)classId];
                    AddShip(classId, name, pos, new float3(0, 0, facing));
                }
            }
        }

        if (Scenario.Objective == Objective.Raid)
        {
            ref var depot = ref AddShip(ShipClassId.Depot, 0, Scenario.DepotPos, new float3(0, 0, -1));
            DepotId = depot.Id;

            // La station est calibrée pour les flottes de l'Escarmouche ; elle grandit avec la flotte de raid.
            var raid = coalition.Sum(c => (double)GameData.Class(c).Hull);
            var r = Math.Max(1, raid / DepotRefFleetHull);
            var k = Math.Pow(Math.Min(r, DepotLargeFrom), DepotScaleExp) * Math.Pow(Math.Max(1, r / DepotLargeFrom), DepotLargeExp);
            depot.Hull = depot.HullMax = (float)Math.Round(depot.HullMax * k, MidpointRounding.AwayFromZero);
        }
    }

    ref Ship AddShip(ShipClassId classId, short nameIndex, float3 pos, float3 fwd)
    {
        var cls = GameData.Class(classId);
        var forward = Vec.Normalize(fwd);
        var ship = new Ship
        {
            Id = _nextId++,
            Side = cls.Side,
            ClassId = classId,
            NameIndex = nameIndex,
            Pos = pos,
            PrevPos = pos,
            Fwd = forward,
            PrevFwd = forward,
            Pointing = Pointing.Auto,
            ExposeFace = Face.Front,
            Alive = true,
            TimeScale = 1,
            Hull = cls.Hull,
            HullMax = cls.Hull,
            Energy = GameData.PostureEnergy(Posture.Balanced),
            Posture = Posture.Balanced,
            WeaponCount = (byte)cls.Weapons.Length,
            HasHangar = cls.Hangar is not null,
            Spawn = pos,
            EmpAmmo = cls.Emp is not null ? 1 : 0,
            HasOrionum = cls.Orionum is not null,
        };
        ((Span<float>)ship.Faces).Fill(cls.ShieldFace);
        ((Span<SubsystemState>)ship.Subsystems).Fill(new SubsystemState(cls.SubsystemHp, cls.SubsystemHp, false));
        for (var w = 0; w < cls.Weapons.Length; w++)
            ship.Weapons[w] = new WeaponState { DefIndex = (byte)w, Cooldown = 1.5 * Rng.NextDouble() }; // range(0, 1.5) du TS, en double
        if (cls.Hangar is { } hangar)
            ship.Hangar = new HangarState { Stock = hangar.Stock, Cooldown = 0, Mode = HangarMode.Attack };

        var index = Ships.Count;
        ref var added = ref Ships.Add(ship);
        _byId.Set(added.Id, new EntityRef(EntityKind.Ship, index));
        EnsureHistoryCapacity(Ships.Count);
        return ref added;
    }
}
