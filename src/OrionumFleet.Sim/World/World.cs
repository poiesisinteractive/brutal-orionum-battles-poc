using Brutal.Numerics;

namespace OrionumFleet.Sim;

public sealed record WorldOptions
{
    public ScenarioId Scenario { get; init; } = ScenarioId.Wolf359Raid;
    public Pillars Pillars { get; init; } = Pillars.All;
    /// <summary>Flottes explicites (tests). Par défaut, l'Escarmouche : le seul préréglage du périmètre.</summary>
    public ShipClassId[]? Coalition { get; init; }
    public ShipClassId[]? Clans { get; init; }
    public float? TimeLimit { get; init; }
}

/// <summary>
/// Le monde de la bataille, port de <c>World</c> (world.ts). Toutes les entités vivent dans des tables
/// de structs : <see cref="Clone"/> copie des tableaux, sans recréer d'objets.
///
/// Ce fichier contient l'état, les requêtes et les commandes ; le cycle de vie des entités est dans
/// World.Lifecycle.cs et le déploiement dans World.Setup.cs.
/// </summary>
public sealed partial class World
{
    public const float Dt = GameData.Tuning.Dt;

    public uint Seed { get; }
    public ScenarioDef Scenario { get; private set; }
    public Pillars Pillars { get; private set; }
    public float TimeLimit { get; private set; }
    public Rng Rng;
    /// <summary>Temps de la bataille (s), en double comme le TS : il s'accumule sur des dizaines de milliers de pas.</summary>
    public double Time;
    public int Tick;
    public BattleResult? Result;
    public WorldStats Stats;
    public int DepotId;

    public EntityTable<Ship> Ships { get; private set; }
    public EntityTable<Drone> Drones { get; private set; }
    public EntityTable<Projectile> Projectiles { get; private set; }
    /// <summary>Zones Orionum (bulles, stases, effets de résonance).</summary>
    public EntityTable<TimeZone> Zones { get; private set; }
    public int NextZoneId = 1;
    /// <summary>Puits de gravité (pilier Terrain) : ceux du scénario et les frondes ancrées.</summary>
    public EntityTable<GravityWell> Wells { get; private set; }
    public int NextWellId = 1;

    int _nextId = 1;
    IdMap _byId;
    List<SimEvent> _events = [];
    double _historyClock;
    // Fenêtre rétroactive : anneau de HistorySlots instantanés par coque, indexé par slot de coque.
    Snapshot[] _history;
    int[] _historyHead;
    int[] _historyCount;

    public World(uint seed, WorldOptions? options = null)
    {
        options ??= new WorldOptions();
        Seed = seed;
        Rng = new Rng(seed);
        Scenario = GameData.Scenario(options.Scenario);
        Pillars = options.Pillars;
        TimeLimit = options.TimeLimit ?? Scenario.TimeLimit;

        Ships = new EntityTable<Ship>(32);
        Drones = new EntityTable<Drone>(256);
        Projectiles = new EntityTable<Projectile>(512);
        Zones = new EntityTable<TimeZone>(8);
        Wells = new EntityTable<GravityWell>(8);
        _byId = new IdMap(1024);
        _history = new Snapshot[32 * GameData.Tuning.HistorySlots];
        _historyHead = new int[32];
        _historyCount = new int[32];

        SetupScenario(
            options.Coalition ?? GameData.SkirmishCoalition,
            options.Clans ?? GameData.SkirmishClans
        );
        Terrain.CreateWells(this);
        // S2 : createFleet (commandement de flotte) pour chaque camp.
    }

    World(World source)
    {
        Seed = source.Seed;
        Scenario = source.Scenario;
        Pillars = source.Pillars;
        TimeLimit = source.TimeLimit;
        Rng = source.Rng;
        Time = source.Time;
        Tick = source.Tick;
        Result = source.Result;
        Stats = source.Stats;
        DepotId = source.DepotId;
        Ships = source.Ships.Clone();
        Drones = source.Drones.Clone();
        Projectiles = source.Projectiles.Clone();
        Zones = source.Zones.Clone();
        NextZoneId = source.NextZoneId;
        Wells = source.Wells.Clone();
        NextWellId = source.NextWellId;
        _nextId = source._nextId;
        _byId = source._byId.Clone();
        _historyClock = source._historyClock;
        _history = (Snapshot[])source._history.Clone();
        _historyHead = (int[])source._historyHead.Clone();
        _historyCount = (int[])source._historyCount.Clone();
    }

    /// <summary>
    /// Copie profonde pour les projections de l'analyse tactique. La copie ne partage aucun état
    /// modifiable, garde l'état du RNG et part avec une file d'événements vide.
    /// </summary>
    public World Clone() => new(this);

    // -------------------------------------------------------------------------
    // Requêtes

    public EntityRef Get(int id) => _byId.Get(id);

    /// <summary>Index de la coque d'id donné, ou -1.</summary>
    public int ShipIndex(int id)
    {
        var e = _byId.Get(id);
        return e.Kind == EntityKind.Ship ? e.Index : -1;
    }

    /// <summary>La coque d'id donné. À n'appeler qu'avec un id de coque valide (voir <see cref="ShipIndex"/>).</summary>
    public ref Ship ShipById(int id) => ref Ships[ShipIndex(id)];

    public int DroneIndex(int id)
    {
        var e = _byId.Get(id);
        return e.Kind == EntityKind.Drone ? e.Index : -1;
    }

    public int ProjectileIndex(int id)
    {
        var e = _byId.Get(id);
        return e.Kind == EntityKind.Projectile ? e.Index : -1;
    }

    public DroneDef DroneDefOf(in Drone d)
    {
        var hangar = ShipById(d.CarrierId).Class.Hangar!;
        return d.Role == DroneRole.Interceptor ? hangar.Interceptor : hangar.Drone;
    }

    /// <summary>Point de départ d'un camp (les coques en retraite sautent près de lui).</summary>
    public float3 Home(Side side) => side == Side.Coalition ? Scenario.CoalitionSpawn : Scenario.ClansSpawn;

    public string WellName(in GravityWell g) => g.NameIndex >= 0 ? Scenario.Wells[g.NameIndex].Name : "Fronde";

    // -------------------------------------------------------------------------
    // Commandes : point d'entrée unique du joueur et de l'IA. (Les ordres arrivent en S2.)

    public void SetPosture(int shipId, Posture posture)
    {
        var i = ShipIndex(shipId);
        if (i < 0 || !Ships[i].IsActive || posture == Posture.Custom)
            return;
        ref var s = ref Ships[i];
        s.Posture = posture;
        s.Energy = GameData.PostureEnergy(posture);
    }

    public void SetEnergy(int shipId, Energy e)
    {
        var i = ShipIndex(shipId);
        if (i < 0 || !Ships[i].IsActive)
            return;
        var w = MathF.Max(0, e.Weapons);
        var sh = MathF.Max(0, e.Shields);
        var en = MathF.Max(0, e.Engines);
        var sum = w + sh + en;
        if (sum == 0)
            sum = 1;
        ref var s = ref Ships[i];
        s.Energy = new Energy(w / sum, sh / sum, en / sum);
        s.Posture = Posture.Custom;
    }

    public void SetPointing(int shipId, Pointing pointing, PointAt? pointAt = null, Face? face = null, float3? lockDir = null)
    {
        var i = ShipIndex(shipId);
        if (i < 0 || !Ships[i].IsActive)
            return;
        ref var s = ref Ships[i];
        s.Pointing = pointing;
        if (pointAt is not null)
            s.PointAt = pointAt;
        if (face is { } f)
            s.ExposeFace = f;
        if (pointing == Pointing.Lock)
            s.LockDir = Vec.Normalize(lockDir ?? s.Fwd);
    }

    /// <summary>Tir courbe (pilier Terrain) : les obus peuvent contourner le cœur d'un puits.</summary>
    public void SetArcFire(int shipId, bool on)
    {
        var i = ShipIndex(shipId);
        if (i >= 0 && Ships[i].IsActive)
            Ships[i].ArcFire = on && Pillars.Terrain;
    }

    /// <summary>Bombe à énergie : tirée dès que la cible est à portée. 0 annule.</summary>
    public void OrderEmp(int shipId, int targetId)
    {
        var i = ShipIndex(shipId);
        if (i < 0)
            return;
        ref var s = ref Ships[i];
        if (s.IsActive && s.Class.Emp is not null && s.EmpAmmo > 0)
            s.EmpTargetId = targetId;
    }

    public void SetHangarMode(int shipId, HangarMode mode)
    {
        var i = ShipIndex(shipId);
        if (i >= 0 && Ships[i].HasHangar)
            Ships[i].Hangar.Mode = mode;
    }

    // -------------------------------------------------------------------------
    // Événements

    public void Emit(in SimEvent e) => _events.Add(e);

    /// <summary>Rend et vide les événements accumulés depuis le dernier appel.</summary>
    public List<SimEvent> DrainEvents()
    {
        var events = _events;
        _events = [];
        return events;
    }
}
