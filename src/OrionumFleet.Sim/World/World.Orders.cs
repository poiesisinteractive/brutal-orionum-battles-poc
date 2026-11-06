namespace OrionumFleet.Sim;

/// <summary>Origine d'un ordre : direct (clic du joueur, micro de l'IA) ou commandement de flotte.</summary>
public enum IssueSource : byte
{
    Direct,
    Fleet,
}

public sealed partial class World
{
    /// <summary>Commandement de flotte, indexé par <see cref="Side"/>.</summary>
    public FleetState[] Fleets { get; private set; } = [];

    public FleetState Fleet(Side side) => Fleets[(int)side];

    /// <summary>La coque d'id donné existe et est en jeu (<c>isActive(w.shipById(id))</c>).</summary>
    public bool IsActiveShip(int id)
    {
        var i = ShipIndex(id);
        return i >= 0 && Ships[i].IsActive;
    }

    /// <summary>
    /// Donne un ordre : le point d'entrée unique du joueur et de l'IA (port de <c>World.issue</c>).
    /// Un ordre <see cref="IssueSource.Direct"/> sort la coque du contrôle de son groupe jusqu'à ce que le
    /// groupe soit commandé à nouveau ; <see cref="IssueSource.Fleet"/> est le commandement de flotte lui-même.
    /// </summary>
    public void Issue(int shipId, Order order, bool queue = false, IssueSource source = IssueSource.Direct)
    {
        var i = ShipIndex(shipId);
        if (i < 0 || !Ships[i].IsActive || !Ships[i].Class.Mobile)
            return;
        ref var s = ref Ships[i];

        // Saut d'urgence en charge (lot 3) : la flotte le laisse tranquille ; un ordre direct l'annulerait.
        if (s.JumpCharge is not null && source == IssueSource.Fleet)
            return;

        if (source == IssueSource.Direct)
        {
            var f = Fleets[(int)s.Side];
            var g = FleetSystem.GroupOf(f, shipId);
            if (g is { Active: true } && !f.Detached.Contains(shipId))
                f.Detached.Add(shipId);
        }

        if (order.Point is { } point && Wells.Count > 0)
            order.Point = Terrain.ClearOfCores(this, s, point);

        var conditional = order.Trigger.Type is not (TriggerType.None or TriggerType.Immediate);
        if (conditional)
        {
            // Un ordre conditionnel attend derrière l'ordre en cours ; sans rien à faire en attendant, on tient.
            if (s.Orders.Count == 0)
                s.Orders.Set(Order.Hold);
            s.Orders.Push(order);
        }
        else if (queue)
        {
            s.Orders.Push(order);
        }
        else
        {
            s.Orders.Set(order);
        }
    }
}
