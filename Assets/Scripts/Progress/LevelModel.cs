using UnityEngine;

/// <summary>
/// Pure, stateless rules for the level / experience curve — the progression
/// counterpart to <see cref="EnergyModel"/>. The requirement to reach the next
/// level compounds, so each level takes meaningfully longer than the last, up to a
/// cap of <see cref="MaxLevel"/>.
/// </summary>
public static class LevelModel
{
    /// <summary>Everyone starts here.</summary>
    public const int StartLevel = 1;
    /// <summary>The current cap; XP stops accumulating once reached.</summary>
    public const int MaxLevel = 30;

    /// <summary>XP needed to go from level 1 to level 2.</summary>
    const int BaseXp = 100;
    /// <summary>Per-level multiplier — ~18% more each level for a smooth compounding curve.</summary>
    const float Growth = 1.18f;

    /// <summary>
    /// Experience required to advance <em>from</em> <paramref name="level"/> to the
    /// next one. Returns 0 at (or beyond) <see cref="MaxLevel"/>, which is how the
    /// rest of the system detects "maxed out".
    /// </summary>
    public static int XpToNext(int level)
    {
        if (level >= MaxLevel) return 0;
        if (level < StartLevel) level = StartLevel;
        return Mathf.RoundToInt(BaseXp * Mathf.Pow(Growth, level - StartLevel));
    }

    /// <summary>True once the player can't level up any further.</summary>
    public static bool IsMaxLevel(int level) => level >= MaxLevel;

    /// <summary>Fraction (0..1) of the way through the current level's XP bar.</summary>
    public static float Progress(int level, int xp)
    {
        int need = XpToNext(level);
        if (need <= 0) return 1f; // max level: show a full bar
        return Mathf.Clamp01((float)xp / need);
    }
}
