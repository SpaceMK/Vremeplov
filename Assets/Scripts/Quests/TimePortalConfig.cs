using UnityEngine;

namespace TalesTensor.Quests
{
    /// <summary>
    /// Runtime configuration for the Time Portal Builder (the FastAPI service that renders
    /// the reconstruction video for each <c>portal_video</c> scene). Used by
    /// <see cref="TimePortalClient"/> to look up the rendered video for the scenes that
    /// <see cref="NearbyPortalsClient"/> returns.
    ///
    /// Mirrors <see cref="QuestEngineConfig"/>: defaults come from the <c>MapController</c>
    /// Inspector via <see cref="Configure"/>, and a PlayerPrefs override (set on-device) wins
    /// so a URL can be provisioned without a rebuild. When the URL is cleared,
    /// <see cref="IsConfigured"/> is false and live portals simply play the demo video (the
    /// Quest Engine part of the map keeps working on its own).
    /// </summary>
    public static class TimePortalConfig
    {
        const string PrefBaseUrl = "timePortal.baseUrl";
        const string PrefApiKey  = "timePortal.apiKey";

        /// <summary>The deployed Time Portal Builder. Not a secret — safe to ship as a default.</summary>
        public const string DefaultBaseUrl = "https://web-production-a9cc46.up.railway.app";

        /// <summary>How long a Time Portal lookup stays fresh before a map refresh re-queries
        /// it. Renders take minutes, so a few minutes of staleness is invisible in play.</summary>
        public const float DefaultCacheSeconds = 300f;

        public static string BaseUrl { get; private set; } = DefaultBaseUrl;
        /// <summary>Optional. Sent as <c>X-API-Key</c> when set; the read endpoints are open
        /// today, so this only matters once the Time Portal gates them.</summary>
        public static string ApiKey  { get; private set; } = "";
        public static float  CacheSeconds { get; private set; } = DefaultCacheSeconds;

        /// <summary>True once a base URL is known — the only state in which the Time Portal
        /// is queried. Off ⇒ live portals fall back to the demo video.</summary>
        public static bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

        static TimePortalConfig()
        {
            BaseUrl = PlayerPrefs.GetString(PrefBaseUrl, DefaultBaseUrl);
            ApiKey  = PlayerPrefs.GetString(PrefApiKey, "");
        }

        /// <summary>Apply configuration supplied at boot (e.g. from the MapController
        /// Inspector). Empty values are ignored so they don't wipe a PlayerPrefs override.</summary>
        public static void Configure(string baseUrl, string apiKey, float cacheSeconds)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl)) BaseUrl = baseUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(apiKey))  ApiKey  = apiKey.Trim();
            if (cacheSeconds > 0f) CacheSeconds = cacheSeconds;
        }

        /// <summary>Persist a base URL for this device (survives restarts).</summary>
        public static void SetBaseUrl(string baseUrl)
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
            PlayerPrefs.SetString(PrefBaseUrl, BaseUrl);
            PlayerPrefs.Save();
        }

        /// <summary>Persist an API key for this device (survives restarts).</summary>
        public static void SetApiKey(string apiKey)
        {
            ApiKey = (apiKey ?? "").Trim();
            PlayerPrefs.SetString(PrefApiKey, ApiKey);
            PlayerPrefs.Save();
        }
    }
}
