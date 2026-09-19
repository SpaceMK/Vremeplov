using UnityEngine;

namespace TalesTensor.Quests
{
    /// <summary>
    /// Runtime configuration for the Tales Tensor Quest Engine integration — the base URL
    /// and the <c>X-API-Key</c> used by <see cref="NearbyPortalsClient"/> to fetch live
    /// portals from <c>GET /portals/nearby</c>.
    ///
    /// The key is a secret: it is NOT hardcoded here. Sources, highest priority first:
    /// <list type="number">
    ///   <item>A per-device PlayerPrefs override (<see cref="SetApiKey"/>).</item>
    ///   <item>The <c>MapController</c> Inspector fields (<see cref="Configure"/>, called in Awake).</item>
    ///   <item>A gitignored <c>Assets/Resources/QuestEngineSecrets.json</c> — the normal way
    ///     to provision a dev machine or a build without committing the key. Copy
    ///     <c>QuestEngineSecrets.example.json</c> beside it and fill in the value.</item>
    /// </list>
    /// When no key is configured, <see cref="IsConfigured"/> is false and the map falls back
    /// to the offline demo pins instead of live quests.
    /// </summary>
    public static class QuestEngineConfig
    {
        const string PrefBaseUrl = "questEngine.baseUrl";
        const string PrefApiKey  = "questEngine.apiKey";
        const string PrefRadius  = "questEngine.radiusMeters";

        /// <summary>The deployed Quest Engine. Not a secret — safe to ship as a default.</summary>
        public const string DefaultBaseUrl =
            "https://tales-tensor-quest-engine-production.up.railway.app";

        /// <summary>Search radius (metres) for live portals around the player.</summary>
        public const float DefaultRadiusMeters = 2000f;
        /// <summary>The Quest Engine rejects <c>radiusMeters</c> above this (400 Bad Request).</summary>
        public const float MaxRadiusMeters = 50000f;

        public static string BaseUrl { get; private set; } = DefaultBaseUrl;
        public static string ApiKey  { get; private set; } = "";
        public static float  NearbyRadiusMeters { get; private set; } = DefaultRadiusMeters;

        /// <summary>True once a non-empty base URL and API key are known — the only state
        /// in which live portals are fetched. Off ⇒ the map uses offline demo pins.</summary>
        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);

        static QuestEngineConfig()
        {
            // A PlayerPrefs override (set on-device or in a build step) wins over the
            // compiled defaults, so a key can be provisioned without a rebuild.
            var secrets = LoadSecretsFile();
            BaseUrl = PlayerPrefs.GetString(PrefBaseUrl,
                string.IsNullOrWhiteSpace(secrets?.questEngineBaseUrl) ? DefaultBaseUrl : secrets.questEngineBaseUrl.TrimEnd('/'));
            ApiKey  = PlayerPrefs.GetString(PrefApiKey, (secrets?.questEngineApiKey ?? "").Trim());
            NearbyRadiusMeters = PlayerPrefs.GetFloat(PrefRadius, DefaultRadiusMeters);
        }

        /// <summary>Shape of <c>Assets/Resources/QuestEngineSecrets.json</c>. Every field is
        /// optional; an absent or empty value simply doesn't apply.</summary>
        [System.Serializable]
        class SecretsFile
        {
            public string questEngineApiKey;
            public string questEngineBaseUrl;
        }

        const string SecretsResource = "QuestEngineSecrets";

        /// <summary>Read the gitignored secrets file from Resources, if the project has one.
        /// Resources ship inside the player, so the same file provisions device builds made
        /// from this machine. Never throws — a missing or malformed file means "no secrets".</summary>
        static SecretsFile LoadSecretsFile()
        {
            try
            {
                var text = Resources.Load<TextAsset>(SecretsResource);
                if (text == null || string.IsNullOrWhiteSpace(text.text)) return null;
                return JsonUtility.FromJson<SecretsFile>(text.text);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Quests] could not read Resources/{SecretsResource}.json: {e.Message}");
                return null;
            }
        }

        /// <summary>Apply configuration supplied at boot (e.g. from the MapController
        /// Inspector). Empty values are ignored so they don't wipe a PlayerPrefs override.</summary>
        public static void Configure(string baseUrl, string apiKey, float radiusMeters)
        {
            if (!string.IsNullOrWhiteSpace(baseUrl)) BaseUrl = baseUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(apiKey))  ApiKey  = apiKey.Trim();
            if (radiusMeters > 0f) NearbyRadiusMeters = Mathf.Min(radiusMeters, MaxRadiusMeters);
        }

        /// <summary>Persist a search radius for this device (survives restarts), e.g. from a
        /// settings or debug screen. Takes effect on the next live-portal fetch. Values
        /// outside (0, <see cref="MaxRadiusMeters"/>] fall back to the default.</summary>
        public static void SetNearbyRadius(float meters)
        {
            NearbyRadiusMeters = meters > 0f ? Mathf.Min(meters, MaxRadiusMeters) : DefaultRadiusMeters;
            PlayerPrefs.SetFloat(PrefRadius, NearbyRadiusMeters);
            PlayerPrefs.Save();
        }

        /// <summary>Persist an API key for this device (survives restarts).</summary>
        public static void SetApiKey(string apiKey)
        {
            ApiKey = (apiKey ?? "").Trim();
            PlayerPrefs.SetString(PrefApiKey, ApiKey);
            PlayerPrefs.Save();
        }

        /// <summary>Persist a base URL for this device (survives restarts).</summary>
        public static void SetBaseUrl(string baseUrl)
        {
            BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/');
            PlayerPrefs.SetString(PrefBaseUrl, BaseUrl);
            PlayerPrefs.Save();
        }
    }
}
