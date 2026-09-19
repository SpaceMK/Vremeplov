using UnityEngine;

/// <summary>
/// The one piece of Firebase configuration the C# side needs at runtime: the
/// <em>web</em> OAuth client ID. Google Sign-In needs it to mint an ID token that
/// Firebase Auth will accept — the Android/iOS client IDs won't do.
///
/// Everything else (project ID, API key, app IDs) is read by the native SDKs
/// straight out of <c>google-services.json</c> / <c>GoogleService-Info.plist</c>,
/// which is why none of it appears here.
///
/// The value is kept in a Resources TextAsset rather than a constant so it ships
/// with the build without needing a code change. Generate it from your
/// <c>google-services.json</c> via <b>Tools ▸ Tales Tensor ▸ Sync Firebase Config</b>.
/// </summary>
public static class CloudConfig
{
    /// <summary>Resources path (no extension) of the generated client-ID asset.</summary>
    public const string ResourceName = "CloudConfig";

    static string _webClientId;
    static bool _loaded;

    /// <summary>
    /// The web OAuth client ID, or null if it hasn't been generated yet — in which
    /// case Google sign-in is unavailable and the app stays guest-only.
    /// </summary>
    public static string WebClientId
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                var asset = Resources.Load<TextAsset>(ResourceName);
                string value = asset != null ? asset.text.Trim() : null;
                _webClientId = string.IsNullOrEmpty(value) ? null : value;

                if (_webClientId == null)
                {
                    Debug.LogWarning(
                        "[Cloud] No web client ID found (Assets/Resources/CloudConfig.txt). " +
                        "Google sign-in is disabled. Run Tools > Tales Tensor > Sync Firebase Config " +
                        "after adding google-services.json.");
                }
            }
            return _webClientId;
        }
    }

    /// <summary>True when Google sign-in has enough configuration to be offered.</summary>
    public static bool IsConfigured => !string.IsNullOrEmpty(WebClientId);

    /// <summary>Forget the cached value so the editor can regenerate it mid-session.</summary>
    public static void Invalidate()
    {
        _loaded = false;
        _webClientId = null;
    }
}
