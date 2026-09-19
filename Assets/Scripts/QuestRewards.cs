/// <summary>
/// Cross-scene state for the AR quest chain (session-only, mirrors the pins). The King
/// hands the player a sealed letter for another generated quest; when the player enters
/// THAT quest, it's a "delivery" — he thanks them, grants a reward, and its quick-asks
/// award XP. The XP/energy celebration is deferred to the next map load so the level-up
/// popup shows on the map.
/// </summary>
public static class QuestRewards
{
    /// <summary>The quest a King's Scroll is to be delivered to (null if none pending).</summary>
    public static string LetterQuest;

    /// <summary>Set when a delivery quest's XP reward is taken; applied on the next map
    /// load (grant XP → level up, refill energy to full, celebratory popup).</summary>
    public static bool CelebratePending;

    /// <summary>XP to grant on the next map load — sized to reach the next level.</summary>
    public static int PendingXp;
}
