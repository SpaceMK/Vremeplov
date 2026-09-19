using System;
using UnityEngine;

/// <summary>
/// Everything about a player that is worth carrying between devices, in one
/// serializable record: the <see cref="UserData"/> (energy, level, XP, inventory)
/// plus the onboarding <see cref="UserProfile"/>.
///
/// This is the unit the cloud stores — see <see cref="CloudSaveService"/>. Local
/// storage is untouched and still lives in PlayerPrefs behind
/// <see cref="UserDataStore"/> / <see cref="QuestionnaireStore"/>; a bundle is
/// just a snapshot of both taken together, so a restore can't leave a player with
/// someone else's progress but their own questionnaire answers.
/// </summary>
[Serializable]
public class SaveBundle
{
    /// <summary>Bump when the shape changes so old documents can be migrated.</summary>
    /// <remarks>
    /// v2 added <see cref="settings"/>. The addition is backward-compatible: a v1
    /// document simply deserializes with a null <see cref="settings"/>, which
    /// <see cref="GameSettings.Apply"/> treats as "leave this device's choices alone".
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    public int schemaVersion = CurrentSchemaVersion;
    public UserData user;
    public UserProfile profile;
    public bool questionnaireCompleted;

    /// <summary>Account-level preferences (notifications, frame rate).</summary>
    public SettingsData settings;

    /// <summary>
    /// UTC ticks the underlying save was last <em>modified</em> — not when this
    /// snapshot was taken. That distinction decides which of two saves wins: stamping
    /// "now" would make the local copy look newer every single time it was examined,
    /// so a device that hasn't been played in a month would still beat one played
    /// this morning.
    /// </summary>
    public long savedUtcTicks;

    /// <summary>Snapshot whatever is currently on this device.</summary>
    public static SaveBundle CaptureLocal()
    {
        var user = PlayerSession.User;
        return new SaveBundle
        {
            schemaVersion = CurrentSchemaVersion,
            user = user,
            profile = QuestionnaireStore.Load(),
            questionnaireCompleted = QuestionnaireStore.IsComplete(),
            settings = GameSettings.Capture(),
            // UserDataStore.Save refreshes this on every write, so it is exactly
            // "when this save last changed".
            savedUtcTicks = user.lastSeenUtcTicks,
        };
    }

    /// <summary>
    /// Overwrite this device's saved state with the bundle and re-point the live
    /// <see cref="PlayerSession"/> at it, so systems already holding the old
    /// record (energy, progression) pick the new one up.
    /// </summary>
    public void ApplyToLocal()
    {
        if (user == null)
        {
            Debug.LogWarning("[Cloud] Refusing to apply a bundle with no user record.");
            return;
        }

        PlayerSession.Adopt(user);

        // Only touch the questionnaire when the bundle actually carries one —
        // otherwise restoring an old save would silently re-trigger onboarding.
        // markDirty: false — this is the cloud's own copy arriving, so re-queueing a
        // push of what we just received would be a pointless write (and, on a restore
        // that resolved a conflict, would immediately re-upload the losing side's
        // timestamp).
        if (questionnaireCompleted && profile != null)
            QuestionnaireStore.Save(profile, markDirty: false);

        // Null for a pre-v2 document; Apply leaves this device's settings as they are.
        GameSettings.Apply(settings);
    }

    /// <summary>True if this bundle is newer than <paramref name="other"/> (null counts as older).</summary>
    public bool IsNewerThan(SaveBundle other) => other == null || savedUtcTicks > other.savedUtcTicks;

    /// <summary>
    /// XP earned across the player's whole life, not just within the current level.
    /// Only used to describe a save to the player when resolving a conflict.
    /// </summary>
    public int TotalXp()
    {
        if (user == null) return 0;
        int total = user.xp;
        for (int lvl = LevelModel.StartLevel; lvl < user.level; lvl++)
            total += LevelModel.XpToNext(lvl);
        return total;
    }

    /// <summary>A one-line human summary, e.g. "Level 7 · 1,240 XP · 3 days ago".</summary>
    public string Describe()
    {
        if (user == null) return "Empty save";

        string age = "just now";
        if (savedUtcTicks > 0)
        {
            TimeSpan since = DateTime.UtcNow - new DateTime(savedUtcTicks, DateTimeKind.Utc);
            if (since.TotalDays >= 2) age = $"{(int)since.TotalDays} days ago";
            else if (since.TotalDays >= 1) age = "yesterday";
            else if (since.TotalHours >= 1) age = $"{(int)since.TotalHours}h ago";
            else if (since.TotalMinutes >= 1) age = $"{(int)since.TotalMinutes}m ago";
        }

        return $"Level {user.level} · {TotalXp():n0} XP · {age}";
    }
}
