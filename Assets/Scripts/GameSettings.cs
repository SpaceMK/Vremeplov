using System;
using UnityEngine;

/// <summary>
/// The player's preferences in the shape the cloud stores them. Kept separate from
/// <see cref="UserData"/> because these are choices, not progress — but they still
/// belong to the account rather than the handset, so a player who signs in on a new
/// phone gets the settings they chose on the old one.
/// </summary>
[Serializable]
public class SettingsData
{
    public bool notificationsEnabled = true;
    public int frameRateMode = (int)FrameRateMode.Max;
}

/// <summary>
/// Single owner of the player's preference keys, and the one place a preference
/// change is turned into a cloud backup.
///
/// Every setter routes through <see cref="PlayerSession.Save"/> rather than just
/// writing PlayerPrefs. That does two things a bare write cannot:
///
/// <list type="bullet">
/// <item>marks the record dirty, so <see cref="CloudSync"/> schedules a push;</item>
/// <item>advances <c>lastSeenUtcTicks</c>, which is what decides whether this
/// device's save beats the cloud's. Without it a settings-only change would be
/// pushed under an unchanged timestamp and could lose a conflict against an older
/// copy — the change would appear to save and then silently revert.</item>
/// </list>
///
/// <see cref="Apply"/> is the exception: restoring a bundle must not re-dirty the
/// record it just adopted.
/// </summary>
public static class GameSettings
{
    // The original keys, unchanged, so existing installs keep their choices.
    const string NotificationsKey = "notificationsEnabled";
    const string FrameRateKey = "frameRateMode";

    /// <summary>Whether the player wants notifications. Defaults to on.</summary>
    public static bool NotificationsEnabled
    {
        get => PlayerPrefs.GetInt(NotificationsKey, 1) == 1;
        set
        {
            if (value == NotificationsEnabled) return; // no write, no needless push
            PlayerPrefs.SetInt(NotificationsKey, value ? 1 : 0);
            PlayerPrefs.Save();
            MarkChanged();
        }
    }

    /// <summary>The frame-rate choice. Applied to Unity as well as stored.</summary>
    public static FrameRateMode FrameRate
    {
        get => (FrameRateMode)PlayerPrefs.GetInt(FrameRateKey, (int)FrameRateMode.Max);
        set
        {
            if (value == FrameRate)
            {
                // Same value: still re-apply, since callers use this to enforce the
                // setting at launch, but there is nothing new to back up.
                FrameRateSetting.Apply();
                return;
            }

            PlayerPrefs.SetInt(FrameRateKey, (int)value);
            PlayerPrefs.Save();
            FrameRateSetting.Apply();
            MarkChanged();
        }
    }

    /// <summary>Snapshot the current preferences for a <see cref="SaveBundle"/>.</summary>
    public static SettingsData Capture() => new SettingsData
    {
        notificationsEnabled = NotificationsEnabled,
        frameRateMode = (int)FrameRate,
    };

    /// <summary>
    /// Adopt preferences from a restored cloud save. Deliberately silent — it writes
    /// the values and applies them without marking anything dirty, because the record
    /// being restored is by definition already what the cloud holds.
    /// </summary>
    public static void Apply(SettingsData data)
    {
        // A bundle written before settings were part of the schema carries none. Leave
        // this device's choices alone rather than resetting them to defaults.
        if (data == null) return;

        PlayerPrefs.SetInt(NotificationsKey, data.notificationsEnabled ? 1 : 0);
        PlayerPrefs.SetInt(FrameRateKey, data.frameRateMode);
        PlayerPrefs.Save();
        FrameRateSetting.Apply();
    }

    static void MarkChanged() => PlayerSession.Save();
}
