namespace OrionumFleet.Sim;

/// <summary>Règles de combat (combat.ts). Le reste du combat arrive en S3.</summary>
public static class CombatRules
{
    public static float WeaponMult(in Ship s) => 0.55f + 1.35f * s.Energy.Weapons;

    public static float ShieldMult(in Ship s) => 0.6f + 1.2f * s.Energy.Shields;

    public static float ShieldRegenMult(in Ship s) => 0.4f + 1.8f * s.Energy.Shields;

    /// <summary>Puissance des moteurs : 0,2 si le sous-système est coupé, sinon selon l'énergie.</summary>
    public static float EngineMult(in Ship s) =>
        s.Subsystems[(int)Subsystem.Engines].Offline ? 0.2f : 0.55f + 1.35f * s.Energy.Engines;

    /// <summary>Ordres dont la cible est un ennemi sur lequel tirer (feu concentré, ciblage précis).</summary>
    public static bool IsHostileOrder(OrderType t) =>
        t is OrderType.Attack or OrderType.Pass or OrderType.Kite or OrderType.KeepDistance or OrderType.Ram;

    /// <summary>Durée de l'agonie (s) selon la taille : une frégate part en ~3,5 s, un capital brûle ~7 s.</summary>
    public static float AgonyDuration(float radius) => MathF.Min(8, 2.6f + radius * 0.065f);

    /// <summary>Rupture du réacteur : rayon du souffle selon la taille de la coque.</summary>
    public static float BreachRadius(float radius) =>
        60 + MathF.Min(radius, 50) * 3.2f + MathF.Max(0, radius - 50) * 1.2f;

    /// <summary>Rupture du réacteur : dégâts du souffle selon la taille de la coque.</summary>
    public static float BreachDamage(float radius) =>
        70 + MathF.Min(radius, 50) * 8 + MathF.Max(0, radius - 50) * 2;
}
