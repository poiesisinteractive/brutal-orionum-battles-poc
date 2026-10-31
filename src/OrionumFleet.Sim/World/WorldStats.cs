using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace OrionumFleet.Sim;

/// <summary>Compteur par camp (Coalition, Clans).</summary>
public struct PerSide<T>
    where T : unmanaged
{
    public T Coalition;
    public T Clans;

    [UnscopedRef]
    public ref T this[Side side] => ref side == Side.Coalition ? ref Coalition : ref Clans;
}

/// <summary>Compteurs par (classe, type d'arme) : les <c>shots</c> et <c>hits</c> du TS, clés « classe.arme ».</summary>
[InlineArray(Size)]
public struct ShotCounters
{
    public const int Size = 9 * 7;

    int _element;

    public static int Key(ShipClassId classId, WeaponKind weapon) => (int)classId * 7 + (int)weapon;
}

/// <summary>Compteurs d'équilibrage lus par le runner headless (les <c>stats</c> de world.ts).</summary>
public struct WorldStats
{
    public int DroneSubsystemKills;
    public int DroneShipHits;
    public int DroneDroneShots;
    public int DronesLaunched;
    public int DronesLostToPd;
    public int BombsFired;
    public int BombsIntercepted;
    public int BombsDetonated;
    public float BombDamage;
    public float BombFriendlyDamage;
    public int EmpDetonated;
    public PerSide<int> OrionumUses;
    public PerSide<int> Resonances;
    public PerSide<int> ArcShots;
    public int Swallowed;
    public PerSide<int> Crushed;
    public int BlockedShots;
    public ShotCounters Shots;
    public ShotCounters Hits;
}
