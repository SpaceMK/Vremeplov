using UnityEngine;

/// <summary>The three frame-rate choices offered in the options popup.</summary>
public enum FrameRateMode
{
    Fps30 = 0,
    Fps60 = 1,
    /// <summary>Uncapped — run as fast as the display's refresh rate allows.</summary>
    Max = 2,
}

/// <summary>
/// Persists the player's frame-rate choice and applies it to
/// <see cref="Application.targetFrameRate"/>. Defaults to <see cref="FrameRateMode.Max"/>
/// (uncapped to the display's refresh rate). Applied once at launch by
/// <see cref="AppBootstrap"/> and again whenever the options toggle changes it.
/// </summary>
public static class FrameRateSetting
{
    /// <summary>
    /// The stored choice. Backed by <see cref="GameSettings"/>, which owns the key so
    /// that changing it also schedules a cloud backup — a plain PlayerPrefs write here
    /// would persist locally and never leave the device.
    /// </summary>
    public static FrameRateMode Mode
    {
        get => GameSettings.FrameRate;
        set => GameSettings.FrameRate = value;
    }

    /// <summary>Push the current <see cref="Mode"/> to Unity's frame-rate target.</summary>
    public static void Apply()
    {
        // VSync would clamp us to the display refresh and override the target, so it
        // stays off and targetFrameRate drives the cadence in every mode.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = Mode switch
        {
            FrameRateMode.Fps30 => 30,
            FrameRateMode.Fps60 => 60,
            _ => MaxDisplayRate(),
        };
    }

    /// <summary>The display's refresh rate (Hz), or -1 (platform default) if unknown.</summary>
    static int MaxDisplayRate()
    {
        double hz = Screen.currentResolution.refreshRateRatio.value;
        // Ask for at least 60 so we never end up *below* the 60 option on odd reports;
        // on a 90/120 Hz panel this requests the panel's full rate.
        return hz >= 1.0 ? Mathf.Max(60, Mathf.RoundToInt((float)hz)) : -1;
    }
}
