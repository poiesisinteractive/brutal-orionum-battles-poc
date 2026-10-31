namespace OrionumFleet.Sim;

/// <summary>Règles de combat (combat.ts). Le reste du combat arrive en S3.</summary>
public static class CombatRules
{
    /// <summary>Durée de l'agonie (s) selon la taille : une frégate part en ~3,5 s, un capital brûle ~7 s.</summary>
    public static float AgonyDuration(float radius) => MathF.Min(8, 2.6f + radius * 0.065f);

    /// <summary>Rupture du réacteur : rayon du souffle selon la taille de la coque.</summary>
    public static float BreachRadius(float radius) =>
        60 + MathF.Min(radius, 50) * 3.2f + MathF.Max(0, radius - 50) * 1.2f;

    /// <summary>Rupture du réacteur : dégâts du souffle selon la taille de la coque.</summary>
    public static float BreachDamage(float radius) =>
        70 + MathF.Min(radius, 50) * 8 + MathF.Max(0, radius - 50) * 2;
}
