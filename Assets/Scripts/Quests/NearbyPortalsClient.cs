using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Networking;

namespace TalesTensor.Quests
{
    /// <summary>
    /// Fetches live, portal-ready quest scenes from the Quest Engine
    /// (<c>GET /portals/nearby?lat=&amp;lng=&amp;radiusMeters=</c>, authenticated with
    /// <c>X-API-Key</c>). Networking only — no UI, not a MonoBehaviour; a host runs
    /// <see cref="FetchNearby"/> via <c>StartCoroutine</c>, mirroring
    /// <see cref="TalesTensor.Chat.ChatClient"/>.
    /// </summary>
    public class NearbyPortalsClient
    {
        public struct Result
        {
            public bool ok;
            public List<NearbyPortal> portals;
            public string error;
        }

        /// <summary>Query for portals within <paramref name="radiusMeters"/> of the given
        /// GPS point and invoke <paramref name="onComplete"/> with the result. An empty
        /// list with <c>ok=true</c> is normal (nothing in range).</summary>
        public IEnumerator FetchNearby(double lat, double lng, float radiusMeters,
            Action<Result> onComplete)
        {
            if (!QuestEngineConfig.IsConfigured)
            {
                onComplete?.Invoke(new Result
                {
                    ok = false,
                    error = "Quest Engine not configured (no API key) — using offline demo pins.",
                });
                yield break;
            }

            // InvariantCulture so decimal points never become commas in locales that use them.
            string sLat = lat.ToString(CultureInfo.InvariantCulture);
            string sLng = lng.ToString(CultureInfo.InvariantCulture);
            string sRad = radiusMeters.ToString(CultureInfo.InvariantCulture);
            string url = $"{QuestEngineConfig.BaseUrl}/portals/nearby?lat={sLat}&lng={sLng}&radiusMeters={sRad}";

            using var req = UnityWebRequest.Get(url);
            // A hung request would otherwise leave the map's single-flight fetch stuck forever.
            req.timeout = 30;
            req.SetRequestHeader("X-API-Key", QuestEngineConfig.ApiKey);
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Quests] /portals/nearby failed at ({sLat}, {sLng}) r={sRad}m: {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                onComplete?.Invoke(new Result { ok = false, error = req.error });
                yield break;
            }

            NearbyPortalsResponse parsed;
            try
            {
                parsed = JsonUtility.FromJson<NearbyPortalsResponse>(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Quests] failed to parse /portals/nearby: {e.Message}\n{req.downloadHandler.text}");
                onComplete?.Invoke(new Result { ok = false, error = "could not parse response" });
                yield break;
            }

            var list = new List<NearbyPortal>();
            if (parsed?.portals != null)
                foreach (var p in parsed.portals)
                    if (p != null && p.location != null) list.Add(p);

            // The queried point is logged so a "0 portals" result can be checked against where
            // the live quests actually are (e.g. the editor's simulated GPS start).
            Debug.Log($"[Quests] /portals/nearby returned {list.Count} portal(s) within {sRad}m of ({sLat}, {sLng}).");
            onComplete?.Invoke(new Result { ok = true, portals = list });
        }
    }
}
