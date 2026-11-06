using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// Commandement de flotte, port de <c>fleet.ts</c> : groupes tactiques avec formations, doctrines
/// d'engagement et manœuvres coordonnées. Partagé par le joueur et l'IA, et avancé dans la simulation
/// (les projections de l'analyse le clonent et le rejouent à l'identique).
/// </summary>
public static class FleetSystem
{
    public const float FleetTick = 1;

    /// <summary>Groupes préremplis par rôle : la ligne, les lanciers, le hangar, l'artillerie, le soutien.</summary>
    public static FleetState CreateFleet(World w, Side side)
    {
        var ships = new List<int>();
        foreach (ref readonly var s in w.Ships.AsSpan())
            if (s.Side == side && s.Class.Mobile)
                ships.Add(s.Id);

        List<int> ByRole(params Role[] roles) => ships.FindAll(id => roles.Contains(w.ShipById(id).Class.Role));
        bool Layer(int id)
        {
            ref readonly var s = ref w.ShipById(id);
            for (var i = 0; i < s.WeaponCount; i++)
                if (s.Class.Weapons[s.Weapons[i].DefIndex].Kind == WeaponKind.Mine)
                    return true;
            return false;
        }
        Group Make(int id, string name, List<int> members, Formation formation, Doctrine doctrine) =>
            new() { Id = id, Name = name, Members = members, Formation = formation, Doctrine = doctrine };

        var support = ByRole(Role.Support);
        var groups = new List<Group>
        {
            Make(1, "Ligne", ByRole(Role.Capital, Role.Line, Role.Escort), Formation.Line,
                Doctrine.Default with { Range = EngageRange.Optimal, Priority = FirePriority.Capital }),
            Make(2, "Lanciers", ByRole(Role.Brawler), Formation.Wedge,
                Doctrine.Default with { Range = EngageRange.Melee, Priority = FirePriority.Carrier }),
            Make(3, "Hangar", ByRole(Role.Carrier), Formation.Screen,
                Doctrine.Default with { Range = EngageRange.Max, Priority = FirePriority.Capital, Drones = HangarMode.Attack }),
            Make(4, "Artillerie", ByRole(Role.Artillery), Formation.Line,
                Doctrine.Default with { Range = EngageRange.Max, Priority = FirePriority.Capital }),
            Make(5, "Soutien", support.FindAll(id => !Layer(id)), Formation.Screen,
                Doctrine.Default with { Range = EngageRange.Max, Priority = FirePriority.Nearest }),
            Make(6, "Mines", support.FindAll(Layer), Formation.Screen,
                Doctrine.Default with { Range = EngageRange.Max, Priority = FirePriority.Nearest }),
        };
        groups.RemoveAll(g => g.Id > 3 && g.Members.Count == 0);
        return new FleetState { Side = side, Groups = groups };
    }

    public static Group? GroupOf(FleetState f, int shipId) => f.Groups.Find(g => g.Members.Contains(shipId));

    /// <summary>Place une coque dans un groupe (et la rattache au commandement de flotte).</summary>
    public static void AssignShip(FleetState f, int shipId, int groupId)
    {
        foreach (var g in f.Groups)
            g.Members.Remove(shipId);
        f.Groups.Find(g => g.Id == groupId)?.Members.Add(shipId);
        f.Detached.Remove(shipId);
    }

    /// <summary>Met un groupe sous commandement (en y rattachant ses membres détachés). <c>Object.assign(g, patch, { active: true })</c>.</summary>
    public static void CommandGroup(FleetState f, Group g, Action<Group>? patch = null)
    {
        patch?.Invoke(g);
        g.Active = true;
        f.Detached.RemoveAll(g.Members.Contains);
    }

    // -------------------------------------------------------------------------
    // Pas

    public static void Step(World w)
    {
        foreach (var side in (ReadOnlySpan<Side>)[Side.Coalition, Side.Clans])
        {
            var f = w.Fleets[(int)side];
            f.Clock -= World.DtExact;
            if (f.Clock > 0)
                continue;
            f.Clock = FleetTick;
            RunManeuver(w, f);
            foreach (var g in f.Groups)
            {
                g.Members.RemoveAll(id => !w.IsActiveShip(id));
                if (g.Active)
                    CommandMembers(w, f, g);
            }
        }
    }

    static float AnchorRank(Role role) => role switch
    {
        Role.Capital => 5,
        Role.Line => 4,
        Role.Carrier => 3,
        Role.Artillery => 2.5f,
        Role.Support or Role.Escort => 2,
        Role.Brawler => 1,
        _ => 0,
    };

    /// <summary>L'ancre du groupe : le plus gros rôle, puis la coque la plus solide. Rend un index de coque ou -1.</summary>
    static int AnchorOf(World w, Group g)
    {
        var best = -1;
        foreach (var id in g.Members)
        {
            var i = w.ShipIndex(id);
            if (i < 0 || !w.Ships[i].IsActive)
                continue;
            if (best < 0)
            {
                best = i;
                continue;
            }
            ref readonly var s = ref w.Ships[i];
            ref readonly var b = ref w.Ships[best];
            var rs = AnchorRank(s.Class.Role);
            var rb = AnchorRank(b.Class.Role);
            if (rs > rb || (rs == rb && s.Hull > b.Hull))
                best = i;
        }
        return best;
    }

    /// <summary>La cible du groupe : explicite, ou choisie selon la doctrine. Rend un index de coque ou -1.</summary>
    public static int GroupTarget(World w, Side side, Group g, int fromIndex)
    {
        if (g.TargetId != 0)
        {
            var t = w.ShipIndex(g.TargetId);
            if (t >= 0 && w.Ships[t].IsActive)
                return t;
            g.TargetId = 0;
        }
        return g.AutoTarget ? PickByPriority(w, side, g.Doctrine.Priority, fromIndex) : -1;
    }

    public static int PickByPriority(World w, Side side, FirePriority priority, int fromIndex)
    {
        var from = w.Ships[fromIndex].Pos;
        var enemy = side == Side.Coalition ? Side.Clans : Side.Coalition;
        var best = -1;
        var bestScore = float.NegativeInfinity;
        var ships = w.Ships.AsSpan();
        for (var i = 0; i < ships.Length; i++)
        {
            ref readonly var e = ref ships[i];
            if (e.Side != enemy || !e.IsActive)
                continue;
            var role = e.Class.Role;
            var d = Vec.Dist(from, e.Pos);
            var score = -d / 1000;
            if (priority == FirePriority.Weakest)
                score += 3 * (1 - e.Hull / e.HullMax);
            else if (priority == FirePriority.Nearest)
                score *= 3;
            else if (Matches(priority, role))
                score += 3;
            if (role == Role.Depot && priority != FirePriority.Depot)
                score -= 2;
            if (score > bestScore)
                (best, bestScore) = (i, score);
        }
        return best;
    }

    /// <summary>Le <c>priority === role</c> du TS : les priorités de rôle portent le nom du rôle.</summary>
    static bool Matches(FirePriority priority, Role role) => priority switch
    {
        FirePriority.Capital => role == Role.Capital,
        FirePriority.Carrier => role == Role.Carrier,
        FirePriority.Brawler => role == Role.Brawler,
        FirePriority.Depot => role == Role.Depot,
        _ => false,
    };

    public static Subsystem? PrioritySubsystem(in Ship t) => t.Class.Role switch
    {
        Role.Carrier => Subsystem.Weapons,
        Role.Capital => t.Subsystems[(int)Subsystem.Weapons].Offline ? Subsystem.Shields : Subsystem.Weapons,
        Role.Depot => Subsystem.Shields,
        _ => null,
    };

    public static float DoctrineDistance(in Ship s, Doctrine d)
    {
        var cls = s.Class;
        if (d.Range == EngageRange.Max)
            return Movement.MaxWeaponRange(s) * 0.92f;
        // Au contact, mais pas au point que les tourelles ne suivent plus la cible.
        if (d.Range == EngageRange.Melee)
            return MathF.Max(cls.Radius * 4, cls.PreferredRange * 0.75f);
        return cls.PreferredRange;
    }

    static void CommandMembers(World w, FleetState f, Group g)
    {
        var anchor = AnchorOf(w, g);
        if (anchor < 0)
            return;
        var target = GroupTarget(w, f.Side, g, anchor);
        var subsystem = target >= 0 ? PrioritySubsystem(w.Ships[target]) : null;
        var targetId = target >= 0 ? w.Ships[target].Id : 0;
        // Repère de formation : vers la cible (ou la destination, ou le nez de l'ancre).
        var anchorPos = w.Ships[anchor].Pos;
        var fwdDir = target >= 0 ? Vec.Normalize(w.Ships[target].Pos - anchorPos)
            : g.MovePoint is { } mp ? Vec.Normalize(mp - anchorPos)
            : w.Ships[anchor].Fwd;
        var slots = FormationSlots(w, g, anchor, fwdDir);

        foreach (var id in g.Members)
        {
            var i = w.ShipIndex(id);
            if (i < 0 || !w.Ships[i].IsActive || f.Detached.Contains(id))
                continue;
            ref var s = ref w.Ships[i];
            if (s.HasHangar)
                s.Hangar.Mode = g.Doctrine.Drones;
            if (g.Doctrine.RetreatAt > 0 && s.Hull / s.HullMax < g.Doctrine.RetreatAt)
            {
                var kiteTarget = targetId != 0 ? targetId : NearestEnemy(w, s);
                // Le TS passe ici une clé littérale ('kite', 'retreat') qui ne correspond jamais à celle de
                // l'ordre en cours : l'ordre est toujours redonné, jamais mis à jour en place.
                SetOrder(w, i, new Order(OrderType.Kite) { TargetId = kiteTarget, Distance = Movement.MaxWeaponRange(s) * 1.3f }, alwaysIssue: true);
                ref var after = ref w.Ships[i];
                if (after.Orders.Count > 0 && after.Orders[0].Type == OrderType.Kite && !w.IsActiveShip(after.Orders[0].TargetId))
                    SetOrder(w, i, new Order(OrderType.Retreat), alwaysIssue: true);
                continue;
            }

            var cls = s.Class;
            Order order;
            if (i == anchor)
            {
                if (target >= 0)
                    order = new Order(OrderType.Attack) { TargetId = targetId, Distance = DoctrineDistance(s, g.Doctrine), Subsystem = subsystem };
                else if (g.MovePoint is { } point)
                    order = new Order(OrderType.Move) { Point = point };
                else
                    order = Order.Hold;
            }
            else if (target >= 0 && (g.Doctrine.Range == EngageRange.Melee || g.Formation == Formation.None))
            {
                // Les petites coques prennent les grosses (qui gardent une zone de mort) en passes balistiques.
                var targetCls = w.Ships[target].Class;
                var run = cls.Standoff is null or 0 && targetCls.Standoff is not (null or 0) && cls.Role == Role.Brawler;
                order = run && g.Doctrine.Range == EngageRange.Melee
                    ? new Order(OrderType.Pass) { TargetId = targetId, Subsystem = subsystem }
                    : new Order(OrderType.Attack) { TargetId = targetId, Distance = DoctrineDistance(s, g.Doctrine), Subsystem = subsystem };
            }
            else if (g.Formation == Formation.None)
            {
                order = new Order(OrderType.Escort) { TargetId = w.Ships[anchor].Id };
            }
            else
            {
                order = new Order(OrderType.Station)
                {
                    TargetId = w.Ships[anchor].Id,
                    Slot = slots.TryGetValue(id, out var slot) ? slot : float3.Zero,
                    FocusId = targetId,
                    Subsystem = subsystem,
                };
            }
            if (g.Bearing is { } bearing && order.Type == OrderType.Attack)
                order.Bearing = bearing;
            if (g.SpeedCap is { } cap)
                order.SpeedCap = cap;
            SetOrder(w, i, order);
        }
    }

    /// <summary>
    /// Donne un ordre, sauf si un ordre équivalent tourne déjà ; dans ce cas, poste, relèvement, plafond,
    /// distance et point sont mis à jour en place. L'équivalence est la clé texte du TS
    /// (type, cible, sous-système, focus, présence d'un point).
    /// </summary>
    static void SetOrder(World w, int index, in Order o, bool alwaysIssue = false)
    {
        ref var s = ref w.Ships[index];
        if (!alwaysIssue && s.Orders.Count == 1)
        {
            ref var cur = ref s.Orders[0];
            if (cur.Type == o.Type && cur.TargetId == o.TargetId && cur.Subsystem == o.Subsystem
                && cur.FocusId == o.FocusId && cur.Point.HasValue == o.Point.HasValue)
            {
                cur.Slot = o.Slot;
                cur.Bearing = o.Bearing;
                cur.SpeedCap = o.SpeedCap;
                cur.Distance = o.Distance;
                cur.Point = o.Point;
                return;
            }
        }
        w.Issue(s.Id, o, queue: false, IssueSource.Fleet);
    }

    static int NearestEnemy(World w, in Ship s)
    {
        var best = 0;
        var bestD = float.PositiveInfinity;
        foreach (ref readonly var e in w.Ships.AsSpan())
        {
            if (e.Side == s.Side || !e.IsActive)
                continue;
            var d = Vec.Dist(e.Pos, s.Pos);
            if (d < bestD)
                (best, bestD) = (e.Id, d);
        }
        return best;
    }

    // -------------------------------------------------------------------------
    // Formations : postes dans un repère tourné vers la menace, en décalages monde depuis l'ancre.

    public static Dictionary<int, float3> FormationSlots(World w, Group g, int anchorIndex, float3 fwdDir)
    {
        var frame = Vec.FrameFromForward(fwdDir);
        var others = new List<int>();
        foreach (var id in g.Members)
        {
            var i = w.ShipIndex(id);
            if (i >= 0 && w.Ships[i].IsActive && i != anchorIndex)
                others.Add(i);
        }
        var maxR = w.Ships[anchorIndex].Class.Radius;
        foreach (var i in others)
            maxR = MathF.Max(maxR, w.Ships[i].Class.Radius);
        var sp = maxR * 2.4f + 70;

        float3 At(float x, float y, float z) => Vec.Scale(frame.Right, x) + Vec.Scale(frame.Up, y) + Vec.Scale(frame.Fwd, z);
        static float SideOf(int i) => (i % 2 == 0 ? 1 : -1) * (i / 2 + 1);
        bool Protected(int i) => w.Ships[i].Class.Role is Role.Carrier or Role.Capital or Role.Artillery or Role.Support or Role.Convoy;

        var output = new Dictionary<int, float3>(others.Count);
        for (var n = 0; n < others.Count; n++)
        {
            var i = others[n];
            var id = w.Ships[i].Id;
            output[id] = g.Formation switch
            {
                Formation.Line => At(SideOf(n) * sp, 0, 0),
                Formation.Wall => Protected(i) ? At(SideOf(n) * sp * 0.5f, 0, -sp * 1.4f) : At(SideOf(n) * sp * 0.7f, 0, 0),
                Formation.Screen => Protected(i)
                    ? At(SideOf(n) * sp * 0.8f, 0, -sp * 0.6f)
                    : At(SideOf(n) * sp * 0.6f, n % 2 * sp * 0.3f, sp * 1.1f),
                Formation.Wedge => At(SideOf(n) * sp * 0.75f, 0, -MathF.Ceiling((n + 1) / 2f) * sp * 0.8f),
                Formation.Sphere => SpherePoint(n, others.Count, sp, At),
                _ => At(SideOf(n) * sp, 0, -sp),
            };
        }
        return output;
    }

    static float3 SpherePoint(int i, int count, float sp, Func<float, float, float, float3> at)
    {
        var k = i + 0.5f;
        var phi = MathF.Acos(1 - 2 * k / count);
        var theta = MathF.PI * (1 + MathF.Sqrt(5)) * k;
        return at(MathF.Cos(theta) * MathF.Sin(phi) * sp * 1.2f, MathF.Cos(phi) * sp * 1.2f, MathF.Sin(theta) * MathF.Sin(phi) * sp * 1.2f);
    }

    // -------------------------------------------------------------------------
    // Manœuvres coordonnées

    public static void StartManeuver(World w, Side side, Maneuver m)
    {
        var f = w.Fleets[(int)side];
        EndManeuver(f);
        f.Maneuver = m;
        f.Clock = 0;
    }

    public static void EndManeuver(FleetState f)
    {
        foreach (var g in f.Groups)
        {
            g.Bearing = null;
            g.SpeedCap = null;
        }
        f.Maneuver = null;
    }

    static void RunManeuver(World w, FleetState f)
    {
        var m = f.Maneuver;
        if (m is null)
            return;

        List<Group> GroupsOf(int[] ids)
        {
            var list = new List<Group>();
            foreach (var id in ids)
                if (f.Groups.Find(g => g.Id == id) is { } g && g.Members.Exists(w.IsActiveShip))
                    list.Add(g);
            return list;
        }

        switch (m.Type)
        {
            case ManeuverType.Pincer:
            {
                var t = w.ShipIndex(m.TargetId);
                var gs = GroupsOf(m.Groups);
                if (t < 0 || !w.Ships[t].IsActive || gs.Count < 2)
                {
                    EndManeuver(f);
                    return;
                }
                // Deux groupes prennent la cible par des flancs opposés : ses boucliers ne peuvent pas faire face aux deux.
                var c = Centroid(w, gs.SelectMany(g => g.Members));
                var axis = Vec.Normalize(c - w.Ships[t].Pos);
                var flank = Vec.Normalize(Vec.Cross(axis, Vec.Up));
                for (var i = 0; i < gs.Count; i++)
                {
                    CommandGroup(f, gs[i], g => (g.TargetId, g.AutoTarget) = (m.TargetId, false));
                    gs[i].Bearing = Vec.Normalize(Vec.Scale(flank, i == 0 ? 1 : -1) + Vec.Scale(axis, 0.25f));
                }
                break;
            }
            case ManeuverType.Sync:
            {
                // Tout le monde arrive à distance de tir au même instant : les rapides attendent les lents.
                var gs = GroupsOf(m.Groups).FindAll(g => g.TargetId != 0 || g.AutoTarget);
                if (gs.Count == 0)
                {
                    EndManeuver(f);
                    return;
                }
                float tMax = 0;
                var legs = new List<(Group G, float D)>();
                foreach (var g in gs)
                {
                    float worst = 0;
                    foreach (var id in g.Members)
                    {
                        var si = w.ShipIndex(id);
                        var anchor = AnchorOf(w, g);
                        var t = anchor >= 0 ? GroupTarget(w, f.Side, g, anchor) : -1;
                        if (si < 0 || !w.Ships[si].IsActive || t < 0)
                            continue;
                        ref readonly var s = ref w.Ships[si];
                        var d = MathF.Max(0, Vec.Dist(s.Pos, w.Ships[t].Pos) - DoctrineDistance(s, g.Doctrine));
                        worst = MathF.Max(worst, d);
                        tMax = MathF.Max(tMax, d / s.Class.Speed + Movement.FlipTime(s) * 0.5f);
                    }
                    legs.Add((g, worst));
                }
                if (legs.TrueForAll(l => l.D < 60))
                {
                    EndManeuver(f);
                    return;
                }
                foreach (var (g, d) in legs)
                    g.SpeedCap = MathF.Max(6, d / MathF.Max(1, tMax));
                break;
            }
            case ManeuverType.FocusFace:
            {
                var t = w.ShipIndex(m.TargetId);
                var gs = GroupsOf(m.Groups);
                if (t < 0 || !w.Ships[t].IsActive || gs.Count == 0)
                {
                    EndManeuver(f);
                    return;
                }
                // Tout le monde frappe la face la plus faible de la cible : se placer de ce côté.
                ref readonly var target = ref w.Ships[t];
                var tf = Vec.FrameFromForward(target.Fwd);
                ReadOnlySpan<(Face Face, float3 Dir)> faces =
                [
                    (Face.Front, tf.Fwd), (Face.Back, Vec.Scale(tf.Fwd, -1)),
                    (Face.Port, Vec.Scale(tf.Right, -1)), (Face.Starboard, tf.Right),
                ];
                var dir = faces[0].Dir;
                var min = float.PositiveInfinity;
                foreach (var (face, n) in faces)
                    if (target.Faces[(int)face] < min)
                        (min, dir) = (target.Faces[(int)face], n);
                for (var i = 0; i < gs.Count; i++)
                {
                    CommandGroup(f, gs[i], g => (g.TargetId, g.AutoTarget) = (m.TargetId, false));
                    gs[i].Bearing = Vec.Normalize(Vec.RotateAxis(dir, Vec.Up, (i - (gs.Count - 1) / 2f) * 0.3f));
                }
                break;
            }
            case ManeuverType.Echelon:
            {
                // Repli par bonds : un groupe se replie (canons toujours sur l'ennemi), les autres le couvrent.
                var gs = GroupsOf(m.Groups);
                if (gs.Count == 0)
                {
                    EndManeuver(f);
                    return;
                }
                var home = w.Home(f.Side);
                if (w.Time - m.PhaseStart > 12)
                {
                    m.Moving = (m.Moving + 1) % gs.Count;
                    m.PhaseStart = w.Time;
                }
                for (var i = 0; i < gs.Count; i++)
                {
                    var g = gs[i];
                    var near = g.Members.TrueForAll(id =>
                    {
                        var si = w.ShipIndex(id);
                        return si < 0 || !w.Ships[si].IsActive || Vec.Dist(w.Ships[si].Pos, home) < w.Scenario.RetreatRadius * 3;
                    });
                    if (near)
                    {
                        foreach (var id in g.Members)
                            if (w.IsActiveShip(id))
                                w.Issue(id, new Order(OrderType.Retreat), queue: false, IssueSource.Fleet);
                        g.Active = false;
                        continue;
                    }
                    var moving = i == m.Moving % gs.Count;
                    if (moving)
                        CommandGroup(f, g, x => (x.MovePoint, x.TargetId, x.AutoTarget) = (home, 0, false));
                    else
                        CommandGroup(f, g, x => (x.MovePoint, x.AutoTarget, x.Doctrine) = (null, true, x.Doctrine with { Range = EngageRange.Max }));
                    foreach (var id in g.Members)
                    {
                        var si = w.ShipIndex(id);
                        if (si >= 0 && w.Ships[si].IsActive)
                            w.Ships[si].Pointing = moving ? Pointing.Target : Pointing.Auto;
                    }
                }
                if (gs.TrueForAll(g => !g.Active))
                    EndManeuver(f);
                break;
            }
        }
    }

    static float3 Centroid(World w, IEnumerable<int> ids)
    {
        var c = float3.Zero;
        var n = 0;
        foreach (var id in ids)
        {
            var i = w.ShipIndex(id);
            if (i < 0 || !w.Ships[i].IsActive)
                continue;
            c += w.Ships[i].Pos;
            n++;
        }
        return n > 0 ? Vec.Scale(c, 1f / n) : c;
    }

    public static float3 GroupCentroid(World w, Group g) => Centroid(w, g.Members);
}
