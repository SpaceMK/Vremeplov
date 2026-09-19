using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local, on-device user profile. Persisted as JSON in PlayerPrefs
/// (see <see cref="UserDataStore"/>). Presence of a saved record is what
/// makes the app treat someone as a returning user.
/// </summary>
[Serializable]
public class UserData
{
    public string userId;
    public string displayName;
    public bool isGuest;
    public long createdUtcTicks;
    public long lastSeenUtcTicks;

    // --- Energy currency (see EnergyModel) ---
    public int energy;
    /// <summary>UTC ticks the energy was last reconciled; the basis for offline regen.</summary>
    public long energyUpdatedUtcTicks;

    // --- Progression (see LevelModel / PlayerProgress) ---
    /// <summary>Current level, 1..<see cref="LevelModel.MaxLevel"/>.</summary>
    public int level;
    /// <summary>Experience accumulated <em>within</em> the current level (resets each level-up).</summary>
    public int xp;

    // --- Inventory (see ItemCatalog / PlayerProgress) ---
    /// <summary>Stacked owned items. JsonUtility serializes this as long as
    /// <see cref="InventoryStack"/> stays <c>[Serializable]</c>.</summary>
    public List<InventoryStack> inventory = new List<InventoryStack>();
    /// <summary>One-time flag so the starter inventory is granted only once.</summary>
    public bool inventorySeeded;

    public static UserData NewGuest()
    {
        long now = DateTime.UtcNow.Ticks;
        return new UserData
        {
            userId = Guid.NewGuid().ToString("N"),
            displayName = "Guest",
            isGuest = true,
            createdUtcTicks = now,
            lastSeenUtcTicks = now,
            energy = EnergyModel.Starting,
            energyUpdatedUtcTicks = now,
            level = LevelModel.StartLevel,
            xp = 0,
        };
    }
}

/// <summary>
/// Reads/writes <see cref="UserData"/> to PlayerPrefs as JSON. Swap the
/// backing store later without touching callers.
/// </summary>
public static class UserDataStore
{
    const string Key = "userData";

    /// <summary>True once a user record has been stored on this device.</summary>
    public static bool Exists() => PlayerPrefs.HasKey(Key);

    public static UserData Load()
    {
        if (!Exists()) return null;
        try { return JsonUtility.FromJson<UserData>(PlayerPrefs.GetString(Key)); }
        catch { return null; }
    }

    public static void Save(UserData data)
    {
        if (data == null) return;
        data.lastSeenUtcTicks = DateTime.UtcNow.Ticks;
        PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
        PlayerPrefs.Save();
    }

    public static void Clear() => PlayerPrefs.DeleteKey(Key);
}
