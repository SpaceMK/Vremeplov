using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace TalesTensor.Quests
{
    /// <summary>
    /// Resolves the rendered reconstruction video for the <c>portal_video</c> scenes returned
    /// by <see cref="NearbyPortalsClient"/>, by joining them against the Time Portal Builder's
    /// quest workspaces. Networking only — no UI, not a MonoBehaviour; a host runs
    /// <see cref="ResolveVideos"/> via <c>StartCoroutine</c>.
    ///
    /// Why the client does this join: the Quest Engine designs a quest (narrative + GPS) and
    /// the Time Portal renders each scene's video, but with the current deployments the video
    /// URL never makes it back into the Quest Engine's <c>/portals/nearby</c> response. The Time
    /// Portal does expose the link in both directions, so two reads are enough:
    /// <list type="number">
    ///   <item><c>GET /api/quests</c> — every workspace with its <c>quest_engine_id</c>; kept if
    ///     that id matches a nearby portal's <c>quest.id</c> or <c>attempt.proposalId</c>
    ///     (creator tooling may store either — both are accepted).</item>
    ///   <item><c>GET /api/quests/{id}</c> per match — its stops, each carrying the Quest
    ///     Engine <c>sceneId</c> as <c>quest_engine_stop_id</c> plus the portal's
    ///     <c>video_url</c>/<c>status</c>. Stops with no stored scene id fall back to
    ///     <c>stop_index</c> == <c>stage.orderIndex</c> within the same matched quest.</item>
    /// </list>
    /// Both responses are cached for <see cref="TimePortalConfig.CacheSeconds"/>, so the
    /// movement-driven map refresh normally costs zero Time Portal calls. Failures never
    /// block the map: scenes that can't be resolved keep <c>video == null</c> and the pin
    /// plays the demo video, exactly as it does when the Time Portal isn't configured.
    /// </summary>
    public class TimePortalClient
    {
        public struct Result
        {
            public bool ok;
            /// <summary>How many portal_video scenes now have a playable video.</summary>
            public int resolved;
            public string error;
        }

        const int RequestTimeoutSeconds = 30;

        // Session cache: the workspace list, and each fetched workspace by Time Portal id.
        static TimePortalQuest[] _questList;
        static float _questListFetchedAt = float.NegativeInfinity;
        static readonly Dictionary<string, (TimePortalQuest quest, float fetchedAt)> _questById = new();
        // Session cache: the full portals list from /api/portals, used to surface standalone
        // portals (creator tool one-offs not tied to any quest) at their real GPS.
        static TimePortalPortal[] _standaloneList;
        static float _standaloneListFetchedAt = float.NegativeInfinity;

        /// <summary>Drop every cached response so the next resolve re-queries the Time Portal
        /// (e.g. from a debug "refresh" action).</summary>
        public static void ClearCache()
        {
            _questList = null;
            _questListFetchedAt = float.NegativeInfinity;
            _questById.Clear();
            _standaloneList = null;
            _standaloneListFetchedAt = float.NegativeInfinity;
        }

        static bool Fresh(float fetchedAt) =>
            Time.realtimeSinceStartup - fetchedAt < TimePortalConfig.CacheSeconds;

        /// <summary>Fill <see cref="NearbyPortal.video"/> on every <c>portal_video</c> scene in
        /// <paramref name="portals"/> that the Time Portal has rendered. Scenes that already
        /// carry a ready video (e.g. supplied by the Quest Engine directly) are left alone.</summary>
        public IEnumerator ResolveVideos(List<NearbyPortal> portals, Action<Result> onComplete)
        {
            if (!TimePortalConfig.IsConfigured)
            {
                onComplete?.Invoke(new Result { ok = false, error = "Time Portal not configured (no base URL) — live portals use the demo video." });
                yield break;
            }

            // The Quest Engine ids a workspace could have been created under, and the scenes
            // that still need a video.
            var wanted = new HashSet<string>();
            var pending = new List<NearbyPortal>();
            foreach (var p in portals)
            {
                if (p == null || !p.IsPortalVideo) continue;
                if (p.video != null && p.video.IsReady) continue;
                pending.Add(p);
                if (p.quest != null && !string.IsNullOrEmpty(p.quest.id)) wanted.Add(p.quest.id);
                if (p.attempt != null && !string.IsNullOrEmpty(p.attempt.proposalId)) wanted.Add(p.attempt.proposalId);
            }
            if (pending.Count == 0)
            {
                onComplete?.Invoke(new Result { ok = true, resolved = 0 });
                yield break;
            }

            // 1. Workspace list (cached).
            if (_questList == null || !Fresh(_questListFetchedAt))
            {
                string error = null;
                yield return Get($"{TimePortalConfig.BaseUrl}/api/quests", json =>
                {
                    var parsed = JsonUtility.FromJson<TimePortalQuestList>(json);
                    _questList = parsed?.quests ?? Array.Empty<TimePortalQuest>();
                    _questListFetchedAt = Time.realtimeSinceStartup;
                }, e => error = e);
                if (error != null)
                {
                    onComplete?.Invoke(new Result { ok = false, error = error });
                    yield break;
                }
            }

            var matched = new List<TimePortalQuest>();
            foreach (var q in _questList)
                if (q != null && !string.IsNullOrEmpty(q.quest_engine_id) && wanted.Contains(q.quest_engine_id))
                    matched.Add(q);

            if (matched.Count == 0)
            {
                Debug.Log($"[TimePortal] no workspace matches the {wanted.Count} nearby quest id(s); videos stay on the demo fallback.");
                onComplete?.Invoke(new Result { ok = true, resolved = 0 });
                yield break;
            }

            // 2. Each matched workspace's stops (cached per id). One failure doesn't sink the rest.
            var bySceneId = new Dictionary<string, PortalVideo>();
            var byQuestAndIndex = new Dictionary<(string questEngineId, int index), PortalVideo>();
            foreach (var summary in matched)
            {
                TimePortalQuest detail = null;
                if (_questById.TryGetValue(summary.id, out var cached) && Fresh(cached.fetchedAt))
                    detail = cached.quest;
                else
                {
                    yield return Get($"{TimePortalConfig.BaseUrl}/api/quests/{summary.id}", json =>
                    {
                        detail = JsonUtility.FromJson<TimePortalQuest>(json);
                        if (detail != null) _questById[summary.id] = (detail, Time.realtimeSinceStartup);
                    }, e => Debug.LogWarning($"[TimePortal] workspace {summary.id} ('{summary.title}') failed: {e}"));
                }
                if (detail?.stops == null) continue;

                foreach (var stop in detail.stops)
                {
                    if (stop == null) continue;
                    var video = FromStop(stop);
                    if (!string.IsNullOrEmpty(stop.quest_engine_stop_id))
                        bySceneId[stop.quest_engine_stop_id] = video;
                    byQuestAndIndex[(summary.quest_engine_id, stop.stop_index)] = video;
                }
            }

            // 3. Attach to the scenes.
            int resolved = 0;
            foreach (var p in pending)
            {
                PortalVideo video = null;
                if (!bySceneId.TryGetValue(p.sceneId, out video) && p.stage != null)
                {
                    // Index fallback. The Quest Engine's orderIndex is 0-based while the Time
                    // Portal's stop_index has been observed 1-based, so try both offsets.
                    foreach (int index in new[] { p.stage.orderIndex, p.stage.orderIndex + 1 })
                    {
                        if (p.quest != null && byQuestAndIndex.TryGetValue((p.quest.id, index), out video)) break;
                        if (p.attempt != null && byQuestAndIndex.TryGetValue((p.attempt.proposalId, index), out video)) break;
                    }
                }
                if (video == null) continue;
                p.video = video;
                if (video.IsReady) resolved++;
            }

            Debug.Log($"[TimePortal] matched {matched.Count} workspace(s); {resolved}/{pending.Count} portal_video scene(s) have a rendered video.");
            onComplete?.Invoke(new Result { ok = true, resolved = resolved });
        }

        /// <summary>Fetch every stop the Time Portal has rendered a video for, as a synthetic
        /// <see cref="NearbyPortal"/> at its real GPS, filtered to within <paramref name="radiusMeters"/>
        /// of (<paramref name="lat"/>, <paramref name="lng"/>) and to stops NOT in
        /// <paramref name="excludeSceneIds"/> (the ids <see cref="NearbyPortalsClient"/> already
        /// returned, so a stop is not shown twice). Reads the same cached workspace list and
        /// details as <see cref="ResolveVideos"/>, so the two paths share cost.
        ///
        /// Why this exists as a separate path: Portal Ready on the Quest Engine is
        /// all-stops-gated (an attempt goes live only once every stop has a video), which
        /// leaves individual finished videos invisible until the last one lands. This surfaces
        /// them directly, at the real anchor point, so the map matches what has actually
        /// been produced.</summary>
        public IEnumerator FetchRenderedPortals(double lat, double lng, float radiusMeters,
            HashSet<string> excludeSceneIds, Action<List<NearbyPortal>> onComplete)
        {
            var results = new List<NearbyPortal>();
            if (!TimePortalConfig.IsConfigured)
            {
                onComplete?.Invoke(results);
                yield break;
            }

            // 1. Workspace list (cached, shared with ResolveVideos).
            if (_questList == null || !Fresh(_questListFetchedAt))
            {
                string error = null;
                yield return Get($"{TimePortalConfig.BaseUrl}/api/quests", json =>
                {
                    var parsed = JsonUtility.FromJson<TimePortalQuestList>(json);
                    _questList = parsed?.quests ?? Array.Empty<TimePortalQuest>();
                    _questListFetchedAt = Time.realtimeSinceStartup;
                }, e => error = e);
                if (error != null)
                {
                    Debug.LogWarning($"[TimePortal] rendered-portal list fetch failed: {error}");
                    onComplete?.Invoke(results);
                    yield break;
                }
            }

            // 2. Each workspace that has at least one rendered stop: fetch its detail
            //    (cached). A per-workspace failure logs and moves on; other workspaces
            //    still contribute.
            foreach (var summary in _questList)
            {
                if (summary == null || summary.completed_stops <= 0) continue;

                TimePortalQuest detail = null;
                if (_questById.TryGetValue(summary.id, out var cached) && Fresh(cached.fetchedAt))
                    detail = cached.quest;
                else
                {
                    yield return Get($"{TimePortalConfig.BaseUrl}/api/quests/{summary.id}", json =>
                    {
                        detail = JsonUtility.FromJson<TimePortalQuest>(json);
                        if (detail != null) _questById[summary.id] = (detail, Time.realtimeSinceStartup);
                    }, e => Debug.LogWarning($"[TimePortal] workspace {summary.id} ({summary.title}) failed: {e}"));
                }
                if (detail?.stops == null) continue;

                int totalStops = detail.stops.Length;
                foreach (var stop in detail.stops)
                {
                    if (stop == null || stop.portal == null || string.IsNullOrEmpty(stop.portal.video_url))
                        continue;
                    // 0,0 lat/lng means the stop has no anchor. Skip rather than plant it at Null Island.
                    if (stop.lat == 0.0 && stop.lng == 0.0) continue;

                    string sceneId = !string.IsNullOrEmpty(stop.quest_engine_stop_id)
                        ? stop.quest_engine_stop_id
                        : $"tp_{stop.id}";
                    if (excludeSceneIds != null && excludeSceneIds.Contains(sceneId)) continue;

                    float distance = HaversineMeters(lat, lng, stop.lat, stop.lng);
                    if (distance > radiusMeters) continue;

                    results.Add(Synthesize(summary, detail, stop, sceneId, distance, totalStops));
                }
            }

            Debug.Log($"[TimePortal] surfaced {results.Count} rendered portal(s) directly (within {radiusMeters:0}m).");
            onComplete?.Invoke(results);
        }

        /// <summary>Fetch every standalone Time Portal (one made in the creator tool, not
        /// attached to any quest) that has a rendered video, as a synthetic
        /// <see cref="NearbyPortal"/> at its real GPS. Filtered to within
        /// <paramref name="radiusMeters"/> of (<paramref name="lat"/>, <paramref name="lng"/>)
        /// and to portals NOT in <paramref name="excludeSceneIds"/>. Reads the shared cache;
        /// the standalone list is refreshed on the same <see cref="TimePortalConfig.CacheSeconds"/>
        /// window as the quest workspaces.
        ///
        /// Playback gates on <c>video_url</c> alone, not the portal's status word: on live
        /// data, portals reporting <c>cancelled</c> still had a playable video URL, and the
        /// Quest Engine's own client uses the same gate.</summary>
        public IEnumerator FetchStandalonePortals(double lat, double lng, float radiusMeters,
            HashSet<string> excludeSceneIds, Action<List<NearbyPortal>> onComplete)
        {
            var results = new List<NearbyPortal>();
            if (!TimePortalConfig.IsConfigured)
            {
                onComplete?.Invoke(results);
                yield break;
            }

            if (_standaloneList == null || !Fresh(_standaloneListFetchedAt))
            {
                string error = null;
                yield return Get($"{TimePortalConfig.BaseUrl}/api/portals", json =>
                {
                    var parsed = JsonUtility.FromJson<TimePortalPortalList>(json);
                    _standaloneList = parsed?.portals ?? Array.Empty<TimePortalPortal>();
                    _standaloneListFetchedAt = Time.realtimeSinceStartup;
                }, e => error = e);
                if (error != null)
                {
                    Debug.LogWarning($"[TimePortal] /api/portals fetch failed: {error}");
                    onComplete?.Invoke(results);
                    yield break;
                }
            }

            foreach (var p in _standaloneList)
            {
                if (p == null) continue;
                // A quest-stop portal is already surfaced by its stop (FetchRenderedPortals),
                // so skip it here to avoid a duplicate pin.
                if (!string.IsNullOrEmpty(p.quest_stop_id)) continue;
                if (string.IsNullOrEmpty(p.video_url)) continue;
                if (p.lat == 0.0 && p.lng == 0.0) continue;

                string sceneId = $"tp_{p.id}";
                if (excludeSceneIds != null && excludeSceneIds.Contains(sceneId)) continue;

                float distance = HaversineMeters(lat, lng, p.lat, p.lng);
                if (distance > radiusMeters) continue;

                results.Add(SynthesizeStandalone(p, sceneId, distance));
            }

            Debug.Log($"[TimePortal] surfaced {results.Count} standalone portal(s) directly (within {radiusMeters:0}m).");
            onComplete?.Invoke(results);
        }

        /// <summary>Build a <see cref="NearbyPortal"/> from a standalone Time Portal. There is
        /// no quest or Encounter Card here, so <see cref="PortalQuest.name"/> falls back to
        /// the era label so the pin card still has something to read.</summary>
        static NearbyPortal SynthesizeStandalone(TimePortalPortal p, string sceneId, float distance)
        {
            string label = !string.IsNullOrEmpty(p.era) ? p.era : "Time Portal";
            return new NearbyPortal
            {
                sceneId = sceneId,
                distanceMeters = distance,
                mode = "portal_video",
                historicalPeriod = p.era ?? "",
                historicalBasis = "",
                location = new PortalLocation
                {
                    lat = p.lat,
                    lng = p.lng,
                    standingPointLat = p.lat,
                    standingPointLng = p.lng,
                    heading = 0f,
                    name = p.location ?? "",
                    address = p.location ?? "",
                },
                stage = new PortalStage { orderIndex = 0, totalScenes = 1 },
                attempt = new PortalAttempt { proposalId = "", title = label },
                quest = new PortalQuest { id = "", name = label },
                data = new SceneData(),
                video = new PortalVideo
                {
                    status = PortalVideo.StatusReady,
                    url = p.video_url,
                    firstFrameUrl = p.approved_image_url,
                },
            };
        }

        /// <summary>Build a <see cref="NearbyPortal"/> from a Time Portal stop that has a
        /// rendered video. Narrative fields are left empty on purpose: the Time Portal never
        /// gets the Encounter Card; the pin plays the video and shows the quest title only.</summary>
        static NearbyPortal Synthesize(TimePortalQuest summary, TimePortalQuest detail,
            TimePortalStop stop, string sceneId, float distance, int totalStops)
        {
            return new NearbyPortal
            {
                sceneId = sceneId,
                distanceMeters = distance,
                mode = "portal_video",
                historicalPeriod = "",
                historicalBasis = "",
                location = new PortalLocation
                {
                    lat = stop.lat,
                    lng = stop.lng,
                    standingPointLat = stop.lat,
                    standingPointLng = stop.lng,
                    heading = 0f,
                    name = stop.location ?? "",
                    address = stop.location ?? "",
                },
                stage = new PortalStage
                {
                    orderIndex = Mathf.Max(0, stop.stop_index - 1),
                    totalScenes = totalStops,
                },
                attempt = new PortalAttempt
                {
                    proposalId = summary.quest_engine_id,
                    title = summary.title,
                },
                quest = new PortalQuest
                {
                    id = summary.quest_engine_id,
                    name = summary.title,
                },
                data = new SceneData(),
                video = new PortalVideo
                {
                    status = PortalVideo.StatusReady,
                    url = stop.portal.video_url,
                    firstFrameUrl = stop.portal.approved_image_url,
                },
            };
        }

        /// <summary>Great-circle distance in metres, the same formula the Quest Engine uses
        /// server-side for <c>/portals/nearby</c>, mirrored here so the filter matches.</summary>
        static float HaversineMeters(double lat1, double lng1, double lat2, double lng2)
        {
            const double R = 6371000.0;
            double dLat = (lat2 - lat1) * System.Math.PI / 180.0;
            double dLng = (lng2 - lng1) * System.Math.PI / 180.0;
            double la1 = lat1 * System.Math.PI / 180.0;
            double la2 = lat2 * System.Math.PI / 180.0;
            double h = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2)
                     + System.Math.Cos(la1) * System.Math.Cos(la2) * System.Math.Sin(dLng / 2) * System.Math.Sin(dLng / 2);
            return (float)(R * 2.0 * System.Math.Atan2(System.Math.Sqrt(h), System.Math.Sqrt(1.0 - h)));
        }

        static PortalVideo FromStop(TimePortalStop stop)
        {
            var portal = stop.portal;
            return new PortalVideo
            {
                // The stop's own status is the truthful one ("video_ready" only once a video
                // exists); the portal's status is finer-grained while rendering.
                status = stop.status == PortalVideo.StatusReady
                    ? PortalVideo.StatusReady
                    : (portal != null && !string.IsNullOrEmpty(portal.status) ? portal.status : stop.status),
                url = portal?.video_url,
                firstFrameUrl = portal?.approved_image_url,
            };
        }

        static IEnumerator Get(string url, Action<string> onJson, Action<string> onError)
        {
            using var req = UnityWebRequest.Get(url);
            // Bounded so a sleeping or unreachable Time Portal can only delay the video
            // upgrade briefly; the pins themselves never wait on this request.
            req.timeout = RequestTimeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(TimePortalConfig.ApiKey))
                req.SetRequestHeader("X-API-Key", TimePortalConfig.ApiKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[TimePortal] GET {url} failed: {req.responseCode} {req.error}\n{req.downloadHandler?.text}");
                onError?.Invoke(req.error);
                yield break;
            }
            try
            {
                onJson(req.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TimePortal] failed to parse {url}: {e.Message}\n{req.downloadHandler.text}");
                onError?.Invoke("could not parse response");
            }
        }
    }
}
