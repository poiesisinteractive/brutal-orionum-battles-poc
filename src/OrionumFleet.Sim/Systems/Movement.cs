using Brutal.Numerics;

namespace OrionumFleet.Sim;

/// <summary>
/// Vol newtonien assisté et exécution des ordres, port de <c>movement.ts</c>.
///
/// Pas de frottement : la vitesse persiste tant que la poussée ne la change pas. Le moteur principal
/// pousse dans l'axe du nez ; les propulseurs de manœuvre (RCS) poussent faiblement dans toutes les
/// directions. Pour freiner fort, une coque se retourne et pousse. La rotation a de l'inertie, donc les
/// grosses coques doivent anticiper. L'ordinateur de vol transforme les ordres (vitesse et cap voulus)
/// en poussée et en couple.
///
/// Les boucles sur toutes les coques (menace la plus proche, distance de sécurité, séparation) sont
/// portées telles quelles, en O(n²) ; le passage à la grille spatiale se fera en S7, mesures à l'appui.
/// </summary>
public static class Movement
{
    struct Steering
    {
        public float3 Vel;
        /// <summary>Cap voulu. null = face à la direction du déplacement.</summary>
        public float3? Face;

        public Steering(float3 vel, float3? face) => (Vel, Face) = (vel, face);
    }

    /// <summary>Le <c>REINFORCE.dazeSpeed</c> du lot 3 : vitesse d'une coque sonnée au sortir d'un saut.</summary>
    const float DazeSpeed = 0.5f;

    // -------------------------------------------------------------------------
    // Ordres

    /// <summary>Fait avancer la file : retire les ordres terminés, laisse les ordres déclenchés passer devant.</summary>
    public static void ProcessOrders(World w, ref Ship s)
    {
        while (s.Orders.Count > 0 && IsComplete(w, s, s.Orders[0]))
            s.Orders.RemoveAt(0);
        for (var i = 1; i < s.Orders.Count; i++)
        {
            var o = s.Orders[i];
            if (o.Trigger.Type is not (TriggerType.None or TriggerType.Immediate) && TriggerMet(w, s, o))
            {
                s.Orders.RemoveRange(0, i);
                break;
            }
        }
    }

    static bool IsComplete(World w, in Ship s, in Order o) => o.Type switch
    {
        OrderType.Move => o.Point is { } p && Vec.Dist(s.Pos, p) < s.Class.Radius * 2 + 25,
        OrderType.Attack or OrderType.Orbit or OrderType.Escort or OrderType.KeepDistance or OrderType.Kite
            or OrderType.Pass or OrderType.Ram or OrderType.Station or OrderType.Tow => !w.IsActiveShip(o.TargetId),
        _ => false,
    };

    public static bool TriggerMet(World w, in Ship s, in Order o)
    {
        switch (o.Trigger.Type)
        {
            case TriggerType.Delay:
                return w.Time >= o.Trigger.Value;
            case TriggerType.HullBelow:
                return s.Hull / s.HullMax < o.Trigger.Value;
            case TriggerType.InRange:
            {
                var t = w.ShipIndex(o.TargetId);
                if (t < 0 || !w.Ships[t].IsActive)
                    return false;
                var range = o.Distance ?? s.Class.PreferredRange;
                return Vec.Dist(s.Pos, w.Ships[t].Pos) <= range;
            }
            default:
                return false;
        }
    }

    public static void UpdateMovement(World w, int index, float dt)
    {
        ref var s = ref w.Ships[index];
        s.PrevPos = s.Pos;
        s.PrevFwd = s.Fwd;
        if (!s.Class.Mobile)
            return;

        var order = s.Orders.Count > 0 ? s.Orders[0] : Order.Hold;
        var st = Steer(w, s, order);
        Separate(w, s, ref st);
        if (order.Type != OrderType.Ram)
            KeepStandoff(w, s, ref st, order);
        FlightComputer(w, ref s, st, order, dt);

        if (order.Type == OrderType.Retreat && Vec.Dist(s.Pos, w.Home(s.Side)) < w.Scenario.RetreatRadius)
            w.WithdrawShip(index);
    }

    // -------------------------------------------------------------------------
    // Vol newtonien

    public static float MainAccel(in Ship s) => s.Class.Accel * CombatRules.EngineMult(s);

    public static float RcsAccel(in Ship s) => s.Class.Accel * s.Class.Rcs * CombatRules.EngineMult(s);

    /// <summary>Temps pour pivoter de 180° depuis l'arrêt et s'arrêter (bang-bang avec la vitesse max).</summary>
    public static float FlipTime(in Ship s)
    {
        var cls = s.Class;
        var eng = CombatRules.EngineMult(s);
        var wmax = cls.TurnRate * eng;
        var aa = MathF.Max(1e-3f, cls.AngAccel * eng);
        var tAcc = wmax / aa;
        var angleAcc = wmax * tAcc; // dépensé en accélérant puis en freinant
        return angleAcc >= MathF.PI ? 2 * MathF.Sqrt(MathF.PI / aa) : tAcc + MathF.PI / wmax;
    }

    /// <summary>
    /// Vitesse sûre pour encore pouvoir s'arrêter en <paramref name="d"/>, retournement compris.
    /// Résout v·t_retournement + v²/(2a) = d.
    /// </summary>
    public static float SafeSpeed(in Ship s, float d)
    {
        var a = MainAccel(s) * 0.85f;
        var t = FlipTime(s);
        return MathF.Max(0, a * (-t + MathF.Sqrt(t * t + 2 * MathF.Max(0, d) / a)));
    }

    static void FlightComputer(World w, ref Ship s, in Steering st, in Order order, float dt)
    {
        var cls = s.Class;
        var main = MainAccel(s);
        var rcs = RcsAccel(s);
        var cruise = cls.Speed * CombatRules.EngineMult(s) * (order.Type == OrderType.Pass ? 1.35f : 1)
            * (s.Fling > 0 ? GameData.Terrain.Sling.SpeedMult : 1);
        // Un remorqueur qui traîne une coque va lentement (lot 1).
        if (order.Type == OrderType.Tow && w.ShipIndex(order.TargetId) is var towed and >= 0 && w.Ships[towed].TowedBy == s.Id)
            cruise *= 0.55f;
        if (order.SpeedCap is { } cap)
            cruise = MathF.Min(cruise, cap);
        // Au sortir d'un saut : sonnée (lot 3).
        if (s.Daze > 0)
            cruise *= DazeSpeed;
        var vDes = Vec.ClampLen(st.Vel, cruise);
        var dv = vDes - s.Vel;
        var dvLen = Vec.Len(dv);

        // Cap : un pointage explicite l'emporte ; sinon le moteur principal s'aligne sur la poussée quand
        // le changement voulu est grand, et sur la direction du déplacement sinon.
        var face = ResolvePointing(w, s, order, st);
        if (face is null)
        {
            if (dvLen > main * 0.8f)
                face = Vec.Normalize(dv);
            else if (Vec.Len(vDes) > 2)
                face = Vec.Normalize(vDes);
            else
                face = s.Fwd;
        }
        Rotate(ref s, face.Value, dt);

        // Poussée : le moteur principal pour la part alignée sur le nez, les RCS pour le reste. Le moteur ne
        // s'allume qu'une fois le nez aligné : tourner d'abord, pousser ensuite.
        var along = Vec.Dot(dv, s.Fwd);
        var align = dvLen > 1e-6f ? along / dvLen : 0;
        var gate = MathF.Min(1, MathF.Max(0, (align - 0.9f) / 0.08f));
        var mainA = along > 0 ? MathF.Min(main, along / dt) * gate : 0;
        var rest = dv - Vec.Scale(s.Fwd, mainA * dt);
        var rcsA = Vec.ClampLen(Vec.Scale(rest, 1 / dt), rcs);
        s.Vel += Vec.Scale(Vec.Scale(s.Fwd, mainA) + rcsA, dt);
        // Les puits attirent la coque ; l'ordinateur de vol compense aux pas suivants.
        s.Vel += Terrain.ShipGravity(w, s, dt);
        s.Pos += Vec.Scale(s.Vel, dt);
        s.Thrust = main > 0 ? mainA / main : 0;
        s.RcsAcc = rcsA;
    }

    /// <summary>Dynamique angulaire : accélérer puis freiner la rotation vers <paramref name="face"/> (profil bang-bang).</summary>
    static void Rotate(ref Ship s, float3 face, float dt)
    {
        var cls = s.Class;
        var eng = CombatRules.EngineMult(s);
        var wmax = cls.TurnRate * eng;
        var aa = cls.AngAccel * eng;
        var c = MathF.Min(1, MathF.Max(-1, Vec.Dot(s.Fwd, face)));
        var angle = MathF.Acos(c);
        var axis = Vec.Cross(s.Fwd, face);
        if (Vec.Len(axis) < 1e-6f)
            axis = angle > 1 ? Vec.FrameFromForward(s.Fwd).Up : float3.Zero;
        axis = Vec.Len(axis) > 0 ? Vec.Normalize(axis) : axis;
        // Vitesse angulaire voulue : aussi vite que possible tout en pouvant s'arrêter sur le cap.
        var wantRate = MathF.Min(wmax, MathF.Sqrt(2 * aa * angle) * 0.9f);
        var wantVel = Vec.Scale(axis, angle < 0.002f ? 0 : wantRate);
        var dw = Vec.ClampLen(wantVel - s.AngVel, aa * dt);
        s.AngVel += dw;
        var rate = Vec.Len(s.AngVel);
        if (rate > 1e-6f)
            s.Fwd = Vec.Normalize(Vec.RotateAxis(s.Fwd, Vec.Scale(s.AngVel, 1 / rate), rate * dt));
    }

    /// <summary>Le mode de pointage de la coque, indépendant du déplacement.</summary>
    static float3? ResolvePointing(World w, in Ship s, in Order order, in Steering st)
    {
        // Le TS calcule toujours la menace ; elle est sans effet de bord, on ne la calcule qu'au besoin.
        switch (s.Pointing)
        {
            case Pointing.Target:
                return ThreatDirection(w, s, order);
            case Pointing.Broadside:
                return ThreatDirection(w, s, order) is { } broadside ? BroadsideFacing(s, broadside) : null;
            case Pointing.Movement:
                return Vec.Len(s.Vel) > 2 ? Vec.Normalize(s.Vel) : null;
            case Pointing.Shield:
                return ThreatDirection(w, s, order) is { } shield ? ShieldFacing(s, shield) : null;
            case Pointing.Point:
            {
                if (s.PointAt is not { } p)
                    return null;
                var t = p.TargetId != 0 ? w.ShipIndex(p.TargetId) : -1;
                if (t >= 0 && w.Ships[t].IsActive)
                    return Vec.Normalize(w.Ships[t].Pos - s.Pos);
                return p.Point is { } point ? Vec.Normalize(point - s.Pos) : null;
            }
            case Pointing.Face:
                return ThreatDirection(w, s, order) is { } face ? FaceToward(s.ExposeFace, face) : null;
            case Pointing.Lock:
                return s.LockDir;
            default:
                return st.Face;
        }
    }

    static float3? ThreatDirection(World w, in Ship s, in Order order)
    {
        var t = order.TargetId != 0 ? w.ShipIndex(order.TargetId) : -1;
        if (t >= 0 && w.Ships[t].IsActive && w.Ships[t].Side != s.Side)
            return Vec.Normalize(w.Ships[t].Pos - s.Pos);

        var best = -1;
        var bestD = float.PositiveInfinity;
        var ships = w.Ships.AsSpan();
        for (var i = 0; i < ships.Length; i++)
        {
            ref readonly var o = ref ships[i];
            if (!o.IsActive || o.Side == s.Side)
                continue;
            var d = Vec.Dist(o.Pos, s.Pos);
            if (d < bestD)
                (best, bestD) = (i, d);
        }
        return best >= 0 ? Vec.Normalize(ships[best].Pos - s.Pos) : null;
    }

    /// <summary>Garder le flanc vers la menace, du côté qui demande le moins de rotation.</summary>
    static float3 BroadsideFacing(in Ship s, float3 threat)
    {
        var a = Vec.Cross(threat, Vec.Up);
        if (Vec.Len(a) < 1e-3f)
            a = new float3(1, 0, 0);
        a = Vec.Normalize(a);
        return Vec.Dot(a, s.Fwd) >= 0 ? a : Vec.Scale(a, -1);
    }

    /// <summary>Présenter la face de bouclier la plus forte à la menace.</summary>
    static float3 ShieldFacing(in Ship s, float3 threat)
    {
        var best = Face.Front;
        foreach (var f in (ReadOnlySpan<Face>)[Face.Front, Face.Back, Face.Port, Face.Starboard])
            if (s.Faces[(int)f] > s.Faces[(int)best])
                best = f;
        return FaceToward(best, threat);
    }

    /// <summary>Direction du nez qui tourne une face donnée vers la menace.</summary>
    public static float3 FaceToward(Face face, float3 threat)
    {
        switch (face)
        {
            case Face.Dorsal:
            case Face.Ventral:
            {
                // Le roulis est calé sur le haut du monde : exposer le dos ou le ventre = cabrer le nez à 90°.
                var side = Vec.Cross(threat, Vec.Up);
                if (Vec.Len(side) < 1e-3f)
                    side = new float3(1, 0, 0);
                var n = Vec.Normalize(Vec.Cross(Vec.Normalize(side), threat));
                return face == Face.Dorsal ? n : Vec.Scale(n, -1);
            }
            case Face.Back:
                return Vec.Scale(threat, -1);
            case Face.Port:
                return Vec.Normalize(Vec.Cross(threat, Vec.Up));
            case Face.Starboard:
                return Vec.Normalize(Vec.Cross(Vec.Up, threat));
            default:
                return threat;
        }
    }

    // -------------------------------------------------------------------------
    // Pilotage par ordre

    static Steering Steer(World w, in Ship s, in Order o)
    {
        var cls = s.Class;
        var ti = o.TargetId != 0 ? w.ShipIndex(o.TargetId) : -1;
        var active = ti >= 0 && w.Ships[ti].IsActive;
        switch (o.Type)
        {
            case OrderType.Move:
                return new Steering(Arrive(s, o.Point ?? s.Pos), null);
            case OrderType.Retreat:
                return new Steering(Arrive(s, w.Home(s.Side)), null);
            case OrderType.Attack:
            {
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                // Une grosse coque engage au-delà de sa distance de sécurité, quelle que soit la distance demandée.
                return Engage(w, s, t, MathF.Max(o.Distance ?? cls.PreferredRange, StandoffRange(s, t)), o);
            }
            case OrderType.Tow:
            {
                // Lot 1 (remorqueur) : seule la remontée vers le point de saut est portée, pas l'accrochage (tryHook).
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                if (t.TowedBy == s.Id)
                    return new Steering(Arrive(s, w.Home(s.Side)), null);
                return new Steering(Arrive(s, t.Pos) + t.Vel, null);
            }
            case OrderType.Ram:
            {
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                return Engage(w, s, t, cls.Radius + t.Class.Radius + 60, o);
            }
            case OrderType.Orbit:
            {
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                return new Steering(Orbit(s, t, o.Distance ?? cls.PreferredRange, cls.Speed * 0.7f) + t.Vel, null);
            }
            case OrderType.Escort:
            {
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                var tf = Vec.FrameFromForward(t.Fwd);
                var side = s.Id % 2 == 0 ? 1 : -1;
                var gap = t.Class.Radius + cls.Radius + 70;
                var slot = t.Pos + Vec.Scale(tf.Fwd, -gap * 0.6f) + Vec.Scale(tf.Right, side * gap);
                var v = Arrive(s, slot) + t.Vel;
                return new Steering(v, Vec.Dist(s.Pos, slot) < 80 ? t.Fwd : null);
            }
            case OrderType.KeepDistance:
            {
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                var want = o.Distance ?? cls.PreferredRange;
                return new Steering(HoldAtDistance(s, t.Pos, want) + t.Vel, Vec.Normalize(t.Pos - s.Pos));
            }
            case OrderType.Kite:
            {
                // Repli combatif : reculer jusqu'à la portée max, nez (et canons) sur la cible.
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                var want = o.Distance ?? MaxWeaponRange(s) * 0.95f;
                return new Steering(HoldAtDistance(s, t.Pos, want) + t.Vel, Vec.Normalize(t.Pos - s.Pos));
            }
            case OrderType.Pass:
                return active ? BallisticPass(w, s, w.Ships[ti]) : Brake();
            case OrderType.Station:
            {
                // Poste de formation relatif à l'ancre ; canons (et nez) sur la cible du groupe.
                if (!active)
                    return Brake();
                ref readonly var t = ref w.Ships[ti];
                var slot = t.Pos + (o.Slot ?? float3.Zero);
                var fi = o.FocusId != 0 ? w.ShipIndex(o.FocusId) : -1;
                var v = Arrive(s, slot) + t.Vel;
                if (fi < 0 || !w.Ships[fi].IsActive)
                    return new Steering(v, Vec.Dist(s.Pos, slot) < 150 ? t.Fwd : null);
                var toF = Vec.Normalize(w.Ships[fi].Pos - s.Pos);
                float3? face = cls.Engage switch
                {
                    EngageStyle.Broadside => BroadsideFacing(s, toF),
                    EngageStyle.Nose or EngageStyle.Standoff => toF,
                    _ => null,
                };
                return new Steering(v, Vec.Dist(s.Pos, slot) < 200 ? face : null);
            }
            default:
                return Brake();
        }
    }

    /// <summary>
    /// Passe balistique : pousser vers une ligne décalée à côté de la cible, couper les moteurs et
    /// traverser en tirant, nez sur la cible, puis revenir.
    /// </summary>
    static Steering BallisticPass(World w, in Ship s, in Ship t)
    {
        var cls = s.Class;
        var rel = t.Pos - s.Pos;
        var d = Vec.Len(rel);
        var relN = Vec.Normalize(rel);
        var relVel = s.Vel - t.Vel;
        var closing = Vec.Dot(relVel, relN);
        var reach = MathF.Max(cls.PreferredRange, 250);
        var passSpeed = cls.Speed * 1.35f;
        if (closing > passSpeed * 0.7f && d < reach * 1.6f)
        {
            // En roue libre : pas de poussée (voulu = actuel), canons sur la cible. Dans un champ de gravité,
            // la passe est gardée sur sa ligne (les propulseurs luttent contre la courbure).
            if (w.Wells.Count > 0 && Vec.LenSq(Terrain.FieldAt(w.Wells.AsSpan(), s.Pos)) > 4)
                return new Steering(t.Vel + Vec.Scale(relN, MathF.Max(Vec.Len(relVel), passSpeed)), relN);
            return new Steering(s.Vel, relN);
        }
        // (Re)lancer une passe : viser à côté de la cible pour ne pas l'éperonner.
        var side = Vec.Normalize(Vec.Cross(relN, Vec.Up));
        var sign = s.Id % 2 == 0 ? 1 : -1;
        var aim = t.Pos + Vec.Scale(side, sign * (t.Class.Radius + cls.Radius + 90));
        var dir = Vec.Normalize(aim - s.Pos);
        if (d < reach * 0.6f && closing < 0)
        {
            // Passée la cible et en éloignement : prolonger un peu avant de revenir.
            return new Steering(t.Vel + Vec.Scale(Vec.Normalize(relVel), passSpeed * 0.8f), null);
        }
        return new Steering(t.Vel + Vec.Scale(dir, passSpeed), null);
    }

    /// <summary>Comportement d'engagement propre à la classe, pour un ordre d'attaque.</summary>
    static Steering Engage(World w, in Ship s, in Ship t, float range, in Order o)
    {
        var cls = s.Class;
        var to = t.Pos - s.Pos;
        var d = Vec.Len(to);
        var toN = Vec.Normalize(to);
        if (o.Bearing is { } bearing)
        {
            // Engager depuis une direction imposée (tenailles, face visée) : se poser sur ce relèvement.
            var spot = t.Pos + Vec.Scale(bearing, range);
            var v = Arrive(s, spot) + t.Vel;
            float3? face = cls.Engage switch
            {
                EngageStyle.Broadside => BroadsideFacing(s, toN),
                EngageStyle.Orbit => null,
                _ => toN,
            };
            return new Steering(v, Vec.Dist(s.Pos, spot) < range * 0.5f ? face : null);
        }
        switch (cls.Engage)
        {
            case EngageStyle.Nose:
            {
                // Capital à canon axial : tenir la distance, pointer le nez (avec avance).
                var lead = t.Pos + Vec.Scale(t.Vel, d / 1500);
                return new Steering(HoldAtDistance(s, t.Pos, range * 0.92f) + t.Vel, Vec.Normalize(lead - s.Pos));
            }
            case EngageStyle.Broadside:
            {
                if (d > range * 1.15f)
                    return new Steering(Vec.Scale(toN, cls.Speed), null);
                var tangent = OrbitTangent(s, toN);
                var radial = Vec.Scale(toN, (d - range * 0.9f) * 0.3f);
                return new Steering(Vec.Scale(tangent, cls.Speed * 0.35f) + radial + t.Vel, BroadsideFacing(s, toN));
            }
            case EngageStyle.Orbit:
                if (d > range * 1.6f)
                    return new Steering(Vec.Scale(toN, cls.Speed), null);
                return new Steering(Orbit(s, t, range * 0.75f, cls.Speed) + t.Vel, null);
            case EngageStyle.Standoff:
                return new Steering(HoldAtDistance(s, t.Pos, range) + t.Vel, toN);
            default:
                return Brake();
        }
    }

    static float3 OrbitTangent(in Ship s, float3 toN)
    {
        var dir = s.Id % 2 == 0 ? 1 : -1;
        var t = Vec.Cross(toN, Vec.Up);
        if (Vec.Len(t) < 1e-3f)
            t = new float3(1, 0, 0);
        return Vec.Scale(Vec.Normalize(t), dir);
    }

    static float3 Orbit(in Ship s, in Ship t, float radius, float speed)
    {
        var to = t.Pos - s.Pos;
        var d = Vec.Len(to);
        var toN = Vec.Normalize(to);
        var tangent = OrbitTangent(s, toN);
        var weave = MathF.Sin(s.Id * 1.7f + d * 0.01f) * 0.25f;
        var radial = Vec.Scale(toN, (d - radius) / MathF.Max(radius, 1) * speed * 1.5f);
        return Vec.Scale(tangent, speed) + radial + Vec.Scale(Vec.Up, weave * speed);
    }

    /// <summary>Vitesse (relative à la cible) qui se pose à <paramref name="want"/> d'elle, sans dépasser la vitesse sûre.</summary>
    static float3 HoldAtDistance(in Ship s, float3 target, float want)
    {
        var to = target - s.Pos;
        var err = Vec.Len(to) - want;
        var v = MathF.Min(s.Class.Speed, SafeSpeed(s, MathF.Abs(err)));
        return Vec.Scale(Vec.Normalize(to), MathF.Sign(err) * v);
    }

    /// <summary>Vitesse voulue pour s'arrêter en <paramref name="point"/>, plafonnée par la vitesse sûre.</summary>
    static float3 Arrive(in Ship s, float3 point)
    {
        var to = point - s.Pos;
        var d = Vec.Len(to);
        if (d < 2)
            return float3.Zero;
        return Vec.Scale(Vec.Normalize(to), MathF.Min(s.Class.Speed, SafeSpeed(s, d)));
    }

    public static float MaxWeaponRange(in Ship s)
    {
        var cls = s.Class;
        float r = 0;
        for (var i = 0; i < s.WeaponCount; i++)
        {
            var def = cls.Weapons[s.Weapons[i].DefIndex];
            if (def.Kind != WeaponKind.Pd)
                r = MathF.Max(r, def.Range);
        }
        return r != 0 ? r : cls.PreferredRange;
    }

    static Steering Brake() => new(float3.Zero, null);

    /// <summary>Distance centre à centre à laquelle <paramref name="s"/> est exactement à sa distance de sécurité de <paramref name="t"/>.</summary>
    public static float StandoffRange(in Ship s, in Ship t) =>
        s.Class.Standoff is { } so and not 0 ? so + s.Class.Radius + t.Class.Radius : 0;

    /// <summary>
    /// Les grosses coques ne laissent jamais venir la mêlée : dans la bulle de sécurité d'une coque
    /// ennemie, elles reculent (et glissent sur leurs propulseurs) en gardant les canons sur la cible.
    /// Ce sont les escortes et les frégates qui s'occupent des intrus.
    /// </summary>
    static void KeepStandoff(World w, in Ship s, ref Steering st, in Order order)
    {
        var cls = s.Class;
        if (!cls.Mobile)
            return;
        // Les petites coques restent hors de la zone de mort d'une grosse coque ennemie (la moitié de sa
        // distance de sécurité) ; la passe balistique est l'exception voulue.
        var small = cls.Standoff is null or 0;
        if (small && order.Type == OrderType.Pass)
            return;

        var push = float3.Zero;
        foreach (ref readonly var o in w.Ships.AsSpan())
        {
            if (!o.IsActive || o.Side == s.Side)
                continue;
            var zone = small ? (o.Class.Standoff ?? 0) * GameData.Tuning.KillZone : cls.Standoff!.Value;
            if (zone == 0)
                continue;
            var gap = Vec.Dist(s.Pos, o.Pos) - cls.Radius - o.Class.Radius;
            if (gap >= zone)
                continue;
            var away = Vec.Normalize(s.Pos - o.Pos);
            var k = (zone - MathF.Max(0, gap)) / zone;
            var slide = Vec.Scale(Vec.Normalize(Vec.Cross(away, Vec.Up)), s.Id % 2 == 0 ? 0.35f : -0.35f);
            push += Vec.Scale(Vec.Normalize(away + slide), k * cls.Speed * 2.2f);
        }
        var p = Vec.Len(push);
        if (p > 0)
            st.Vel += Vec.Scale(push, MathF.Min(1, cls.Speed * 1.4f / p));
    }

    static void Separate(World w, in Ship s, ref Steering st)
    {
        var r = s.Class.Radius;
        if (OrionumSystem.StasisPush(w, s) is { } wall)
            st.Vel += wall;
        if (Terrain.CorePush(w, s, st.Vel) is { } core)
            st.Vel += core;

        // Nos propres mines sont cartographiées : on les contourne (celles de l'ennemi, on les découvre trop tard).
        foreach (ref readonly var p in w.Projectiles.AsSpan())
        {
            if (!p.Alive || p.Weapon != WeaponKind.Mine || p.Side != s.Side)
                continue;
            var clear = r + 170;
            var d = Vec.Dist(s.Pos, p.Pos);
            if (d < clear && d > 1e-3f)
                st.Vel += Vec.Scale(Vec.Normalize(s.Pos - p.Pos), (clear - d) * 1.5f);
        }

        foreach (ref readonly var o in w.Ships.AsSpan())
        {
            // S'écarter d'une coque à l'agonie : son réacteur va lâcher.
            if (o.Alive && o.Dying is not null)
            {
                var danger = CombatRules.BreachRadius(o.Class.Radius) + r + 40;
                var d = Vec.Dist(s.Pos, o.Pos);
                if (d < danger && d > 1e-3f)
                    st.Vel += Vec.Scale(Vec.Normalize(s.Pos - o.Pos), (danger - d) * 1.2f);
                continue;
            }
            if (o.Id == s.Id || !o.IsActive)
                continue;
            var minD = r + o.Class.Radius + 30;
            var dist = Vec.Dist(s.Pos, o.Pos);
            if (dist >= minD || dist < 1e-3f)
                continue;
            st.Vel += Vec.Scale(Vec.Normalize(s.Pos - o.Pos), (minD - dist) * 1.5f);
        }
    }
}
