using System.Collections;
using System.Collections.Generic;
using TalesTensor.Quests;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TalesTensor.Map
{
    /// <summary>
    /// Spawns the interactive <see cref="MapPin"/>s scattered around the player's
    /// first GPS fix (placed at real-world coordinates through
    /// <see cref="MapController.GeoToUnity"/>) and owns tap routing: a clean tap on a
    /// pin opens its card (closing any other), a tap on empty map closes the open one.
    /// Hold-drag still orbits the camera via <see cref="MapController"/> — only taps
    /// that don't move are treated as selections, so the two don't conflict.
    /// Created and driven by <see cref="MapController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapPinLayer : MonoBehaviour
    {
        // Quest names live in the shared QuestCatalog so the AR scene can reference them too.
        static string[] QuestNames => QuestCatalog.Names;

        MapController _map;
        int _pinCount;
        float _interactRangeMeters;
        float _minMeters, _maxMeters;
        float _refillMinMeters, _refillMaxMeters;
        float _pinHeight, _cardGap, _cardScale;
        Sprite _teardropOverride, _glyphChat, _glyphGem, _glyphClock;

        Transform _root;
        readonly List<MapPin> _pins = new();
        MapPin _openPin;
        bool _spawned;
        // The persisted set: generated once, restored on every later visit, and pruned
        // as pins are claimed so claimed points never respawn.
        MapPinSave _save;

        // Live-portal refresh state: where/when the Quest Engine was last queried, so the
        // map re-fetches on real movement (not on a timer) and never overlaps requests.
        LatLon _lastFetchLocation;
        float _lastFetchTime = float.NegativeInfinity;
        bool _fetchInFlight;
        bool _refreshOnFocus;

        // Tap detection state
        bool _wasPressed;
        Vector2 _downPos;
        float _downTime;
        bool _moved;
        const float TapMoveThreshold = 16f;  // pixels
        const float TapMaxDuration = 0.4f;    // seconds

        public void Init(MapController map, int pinCount, float interactRangeMeters,
            float minMeters, float maxMeters, float refillMinMeters, float refillMaxMeters,
            float pinHeight, float cardGap, float cardScale,
            Sprite teardropOverride, Sprite glyphChat, Sprite glyphGem, Sprite glyphClock)
        {
            _map = map;
            _pinCount = Mathf.Max(0, pinCount);
            _interactRangeMeters = interactRangeMeters;
            _minMeters = minMeters;
            _maxMeters = maxMeters;
            _refillMinMeters = refillMinMeters;
            _refillMaxMeters = refillMaxMeters;
            _pinHeight = pinHeight;
            _cardGap = cardGap;
            _cardScale = cardScale;
            _teardropOverride = teardropOverride;
            _glyphChat = glyphChat;
            _glyphGem = glyphGem;
            _glyphClock = glyphClock;

            _root = new GameObject("Pins").transform;
            _root.SetParent(transform, false);
        }

        void Update()
        {
            if (_map == null || !_map.HasOrigin) return;
            if (!_spawned) { RestoreOrGenerate(); _spawned = true; }
            HandleTaps();
            MaybeRefreshLive();
        }

        // Returning to the app (or the map scene) is the moment a video that finished
        // rendering in the background becomes worth showing, so a refresh is queued for the
        // next frame with a fix — the distance gate is skipped, only the rate limit applies.
        void OnApplicationFocus(bool focus)
        {
            if (focus) _refreshOnFocus = true;
        }

        // --- generation ---

        // Timed-event availability seeding: a 2-hour window starting on the hour or
        // half-hour, anywhere from 9am up to 7pm (so it ends by 9pm).
        const int DayStartMin = 9 * 60;    // 09:00
        const int DayEndMin = 21 * 60;     // 21:00
        const int WindowLengthMin = 120;   // 2 hours

        // Energy spent to claim a pin. Item ("Collect") pins are deliberately cheap;
        // the single Premium pin is pricey; every other kind keeps a flat cost.
        const int ItemCostMin = 1;
        const int ItemCostMax = 5;
        const int DefaultEnergyCost = 10;
        const int PremiumEnergyCost = 25; // lowered from 50 for testing
        // Two Portal pins are always seeded near the player so the portal experience is
        // immediately visible and reachable on spawn.
        const int PortalPinCount = 2;
        const int PortalEnergyCost = 5;

        // The guaranteed in-range AR pin spawns between these fractions of the interaction
        // range, so it's clearly reachable but not sitting on top of the player.
        const float NearMinRangeFraction = 0.3f;
        const float NearMaxRangeFraction = 0.8f;
        // ...and within this cone (degrees) of the camera's facing direction, so it's on
        // screen ahead of the player at spawn rather than randomly behind them.
        const float NearBearingSpreadDeg = 22f;

        // Pins stay hidden when the map first loads, then bounce in (slightly staggered).
        const float IntroHideSeconds = 2.5f;
        const float IntroStagger = 0.08f;
        // A refill happens mid-session, so the replacements bounce in almost immediately
        // rather than waiting out the long first-load settle.
        const float RefillIntroLead = 0.25f;

        // Live portals are re-queried once the player has moved this far from the last
        // query point (the Quest Engine's own guidance: poll on movement, not on a timer),
        // and never more often than the interval — a floor that also covers app-resume.
        const float RefetchDistanceMeters = 50f;
        const float RefetchMinIntervalSeconds = 60f;

        /// <summary>Restore the saved points of interest if a set has already been
        /// generated for this player; otherwise generate a fresh set and persist it.
        /// Either way the same pins reappear when the player comes back to the map,
        /// minus any they've already claimed.</summary>
        void RestoreOrGenerate()
        {
            _save = MapPinStore.Load();
            if (_save != null && _save.generated)
            {
                // Later visit this session: restore the saved set (claimed pins already
                // removed) rather than re-fetching/regenerating.
                SpawnAll(_save.pins, IntroHideSeconds);
                return;
            }

            // First visit. When the Quest Engine is configured, fetch live portals and
            // build the pin set from them (asynchronously); otherwise scatter the offline
            // demo set. Either path persists the result so it restores on later visits.
            if (QuestEngineConfig.IsConfigured)
            {
                // _spawned is already set by the caller, so this coroutine runs only once.
                StartCoroutine(FetchLiveThenSpawn());
            }
            else
            {
                _save = new MapPinSave
                {
                    generated = true,
                    pins = Generate(_minMeters, _maxMeters, guaranteeNearPin: true),
                };
                MapPinStore.Save(_save);
                SpawnAll(_save.pins, IntroHideSeconds);
            }
        }

        /// <summary>Fetch live portal-ready quest scenes from the Quest Engine and build the
        /// pin set from them (real scenes as AR/Portal pins, plus a few offline economy
        /// pins so collectibles still appear). Falls back to the full offline demo set if
        /// nothing is in range or the request fails.</summary>
        IEnumerator FetchLiveThenSpawn()
        {
            List<NearbyPortal> portals = null;
            yield return FetchLivePortals(r => { if (r.ok) portals = r.portals; });

            List<PinDefinition> pins;
            if (portals != null && portals.Count > 0)
            {
                pins = new List<PinDefinition>(portals.Count + 4);
                foreach (var p in portals)
                    pins.Add(PinDefinition.FromPortal(p, DefaultEnergyCost, PortalEnergyCost));
                // Keep the game-shell collectibles alongside the real quests.
                pins.AddRange(GenerateEconomyPins(Mathf.Min(_pinCount, 4)));
                Debug.Log($"[Quests] built {portals.Count} live quest pin(s) + economy pins.");
            }
            else
            {
                Debug.Log("[Quests] no live portals in range (or fetch failed) — using offline demo pins.");
                pins = Generate(_minMeters, _maxMeters, guaranteeNearPin: true);
            }

            _save = new MapPinSave { generated = true, pins = pins };
            MapPinStore.Save(_save);
            SpawnAll(_save.pins, IntroHideSeconds);

            if (portals != null && portals.Count > 0)
                StartCoroutine(ResolveVideosThenRefresh(portals));
        }

        /// <summary>Look up rendered videos for the live scenes on the map without holding the
        /// pins back: they spawn on the Quest Engine result alone, and any pin whose video
        /// readiness changes here is respawned so its card and entry behaviour update. A slow
        /// or sleeping Time Portal (20 s cold starts have been measured) therefore delays only
        /// the "real video" upgrade, never the map. The scenes are the same objects the pins
        /// hold, so the lookup's writes land on the pins directly.</summary>
        IEnumerator ResolveVideosThenRefresh(List<NearbyPortal> portals)
        {
            var before = new Dictionary<string, bool>(portals.Count);
            foreach (var p in portals)
                if (p != null && !string.IsNullOrEmpty(p.sceneId)) before[p.sceneId] = p.HasPlayableVideo;

            yield return StartCoroutine(new TimePortalClient().ResolveVideos(portals, _ => { }));

            int changed = 0;
            foreach (var pin in _pins.ToArray())
            {
                var def = pin.Definition;
                if (def == null || !def.IsLive) continue;
                if (!before.TryGetValue(def.portal.sceneId, out bool was) || was == def.portal.HasPlayableVideo) continue;
                Despawn(pin, keepDefinition: true);
                Spawn(def, RefillIntroLead);
                changed++;
            }
            if (changed > 0) Debug.Log($"[Quests] {changed} portal pin(s) now play their rendered video.");
        }

        /// <summary>Fetch nearby scenes from the Quest Engine. Records the query point/time for
        /// the movement-driven refresh and guarantees only one fetch runs at a time. Rendered
        /// videos are looked up separately, after the pins exist: see
        /// <see cref="ResolveVideosThenRefresh"/>.</summary>
        IEnumerator FetchLivePortals(System.Action<NearbyPortalsClient.Result> onComplete)
        {
            _fetchInFlight = true;
            _refreshOnFocus = false;
            _lastFetchLocation = _map.CurrentLocation;
            _lastFetchTime = Time.realtimeSinceStartup;

            var result = default(NearbyPortalsClient.Result);
            yield return StartCoroutine(new NearbyPortalsClient().FetchNearby(
                _lastFetchLocation.Latitude, _lastFetchLocation.Longitude,
                QuestEngineConfig.NearbyRadiusMeters,
                r => result = r));

            // Also surface every rendered portal the Time Portal already has directly, at
            // its real GPS. Portal Ready gates entire attempts, so individually finished
            // stops stay invisible in /portals/nearby until the last video lands. This adds
            // any not-yet-in-QE stop as an extra live pin with its real video attached.
            // Deduped by sceneId; if a slow or unreachable Time Portal takes too long its
            // request timeout kicks in and the QE-only result flows through unchanged.
            if (TimePortalConfig.IsConfigured)
            {
                var seen = new System.Collections.Generic.HashSet<string>();
                if (result.ok && result.portals != null)
                    foreach (var p in result.portals)
                        if (p != null && !string.IsNullOrEmpty(p.sceneId)) seen.Add(p.sceneId);

                List<NearbyPortal> tpOnly = null;
                yield return StartCoroutine(new TimePortalClient().FetchRenderedPortals(
                    _lastFetchLocation.Latitude, _lastFetchLocation.Longitude,
                    QuestEngineConfig.NearbyRadiusMeters, seen,
                    r => tpOnly = r));

                if (tpOnly != null && tpOnly.Count > 0)
                {
                    int qeCount = result.portals != null ? result.portals.Count : 0;
                    var combined = new List<NearbyPortal>(qeCount + tpOnly.Count);
                    if (result.portals != null) combined.AddRange(result.portals);
                    combined.AddRange(tpOnly);
                    result.ok = true;
                    result.portals = combined;
                }

                // Standalone portals: creator-tool one-offs with no quest, still at real GPS
                // with a rendered video. Deduped against everything we already have.
                foreach (var p in result.portals ?? new List<NearbyPortal>())
                    if (p != null && !string.IsNullOrEmpty(p.sceneId)) seen.Add(p.sceneId);

                List<NearbyPortal> tpStandalone = null;
                yield return StartCoroutine(new TimePortalClient().FetchStandalonePortals(
                    _lastFetchLocation.Latitude, _lastFetchLocation.Longitude,
                    QuestEngineConfig.NearbyRadiusMeters, seen,
                    r => tpStandalone = r));

                if (tpStandalone != null && tpStandalone.Count > 0)
                {
                    int prevCount = result.portals != null ? result.portals.Count : 0;
                    var combined = new List<NearbyPortal>(prevCount + tpStandalone.Count);
                    if (result.portals != null) combined.AddRange(result.portals);
                    combined.AddRange(tpStandalone);
                    result.ok = true;
                    result.portals = combined;
                }

                // One final sort now that every source has contributed, so nearest-first
                // holds across all of {Quest Engine, TP quest stops, TP standalone}.
                if (result.portals != null && result.portals.Count > 1)
                    result.portals.Sort((a, b) => a.distanceMeters.CompareTo(b.distanceMeters));
            }

            _fetchInFlight = false;
            onComplete?.Invoke(result);
        }

        /// <summary>Re-query live portals once the player has moved far enough (or the app
        /// regained focus), rate-limited, and merge the answer into the pins on the map.</summary>
        void MaybeRefreshLive()
        {
            if (!QuestEngineConfig.IsConfigured || _fetchInFlight || _save == null) return;
            if (Time.realtimeSinceStartup - _lastFetchTime < RefetchMinIntervalSeconds) return;

            bool moved = MetersBetween(_lastFetchLocation, _map.CurrentLocation) >= RefetchDistanceMeters;
            if (!moved && !_refreshOnFocus) return;
            _refreshOnFocus = false;

            StartCoroutine(RefreshLive());
        }

        IEnumerator RefreshLive()
        {
            List<NearbyPortal> portals = null;
            bool ok = false;
            yield return FetchLivePortals(r => { ok = r.ok; portals = r.portals; });
            // A failed request keeps the current map exactly as it is.
            if (!ok || portals == null) yield break;
            MergeLivePortals(portals);
            if (portals.Count > 0) yield return ResolveVideosThenRefresh(portals);
        }

        /// <summary>Reconcile the live pins against a fresh nearby result, keyed by scene:
        /// scenes now out of range are removed (claimed ones are already gone), scenes still
        /// in range keep their pin but pick up the latest data — respawning if their video
        /// readiness changed, since the card text and entry behaviour depend on it — and new
        /// scenes bounce in. Offline economy/demo pins are untouched.</summary>
        void MergeLivePortals(List<NearbyPortal> portals)
        {
            var incoming = new Dictionary<string, NearbyPortal>(portals.Count);
            foreach (var p in portals)
                if (p != null && !string.IsNullOrEmpty(p.sceneId)) incoming[$"live_{p.sceneId}"] = p;

            int removed = 0, updated = 0, added = 0;
            foreach (var pin in _pins.ToArray())
            {
                var def = pin.Definition;
                if (def == null || !def.IsLive) continue;

                if (!incoming.TryGetValue(def.id, out var fresh))
                {
                    Despawn(pin);
                    removed++;
                    continue;
                }

                bool wasPlayable = def.portal.HasPlayableVideo;
                def.portal = fresh;
                incoming.Remove(def.id);
                if (wasPlayable != fresh.HasPlayableVideo)
                {
                    Despawn(pin, keepDefinition: true);
                    Spawn(def, RefillIntroLead);
                    updated++;
                }
            }

            foreach (var fresh in incoming.Values)
            {
                var def = PinDefinition.FromPortal(fresh, DefaultEnergyCost, PortalEnergyCost);
                _save.pins.Add(def);
                Spawn(def, RefillIntroLead + added * IntroStagger);
                added++;
            }

            if (removed + updated + added > 0)
            {
                MapPinStore.Save(_save);
                Debug.Log($"[Quests] live refresh: +{added} new, {updated} video-state change(s), -{removed} out of range.");
            }
        }

        /// <summary>Remove a pin from the map without claiming it. Its definition is dropped
        /// from the saved set unless <paramref name="keepDefinition"/> (a respawn in place).</summary>
        void Despawn(MapPin pin, bool keepDefinition = false)
        {
            if (_openPin == pin) { pin.Close(); _openPin = null; }
            pin.Claimed -= HandleClaimed;
            _pins.Remove(pin);
            if (!keepDefinition && pin.Definition != null)
                _save.pins.RemoveAll(d => d.id == pin.Definition.id);
            Destroy(pin.gameObject);
        }

        /// <summary>Great-circle distance in metres between two coordinates.</summary>
        static float MetersBetween(LatLon a, LatLon b)
        {
            const double R = 6371000.0;
            double dLat = (b.Latitude - a.Latitude) * System.Math.PI / 180.0;
            double dLon = (b.Longitude - a.Longitude) * System.Math.PI / 180.0;
            double la1 = a.Latitude * System.Math.PI / 180.0, la2 = b.Latitude * System.Math.PI / 180.0;
            double h = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2)
                     + System.Math.Cos(la1) * System.Math.Cos(la2) * System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
            return (float)(R * 2.0 * System.Math.Atan2(System.Math.Sqrt(h), System.Math.Sqrt(1.0 - h)));
        }

        /// <summary>Scatter only the non-quest "game shell" pins (Chrono Boosters, timed
        /// events, and one premium reward) around the player, used to accompany live quest
        /// pins. Mirrors the demo-pin economy in <see cref="Generate"/> without any
        /// ArChat/Portal pins (those come from the Quest Engine when live).</summary>
        List<PinDefinition> GenerateEconomyPins(int count)
        {
            var defs = new List<PinDefinition>(count);
            if (count <= 0) return defs;

            LatLon centre = _map.CurrentLocation;
            int premiumIndex = Random.Range(0, count);
            int rot = 0;

            for (int i = 0; i < count; i++)
            {
                PinType type;
                int cost, wStart = 0, wEnd = 0;

                if (i == premiumIndex)
                {
                    type = PinType.Premium;
                    cost = PremiumEnergyCost;
                }
                else
                {
                    type = (rot % 2 == 0) ? PinType.Collect : PinType.TimedEvent;
                    rot++;
                    if (type == PinType.TimedEvent)
                    {
                        int slots = (DayEndMin - WindowLengthMin - DayStartMin) / 30 + 1;
                        wStart = DayStartMin + Random.Range(0, slots) * 30;
                        wEnd = wStart + WindowLengthMin;
                        cost = 0;
                    }
                    else
                    {
                        cost = Random.Range(ItemCostMin, ItemCostMax + 1);
                    }
                }

                float bearing = Random.Range(0f, Mathf.PI * 2f);
                float dist = Random.Range(_minMeters, _maxMeters);
                LatLon loc = Offset(centre, dist, bearing);
                defs.Add(new PinDefinition($"econ{i}", type, loc, null, wStart, wEnd, cost));
            }

            return defs;
        }

        /// <summary>Every pin has been claimed: scatter a fresh batch in the farther
        /// refill band (still within view), persist it, and bounce it in. Centred on the
        /// player's <em>current</em> location, and with no guaranteed in-range pin, so the
        /// replacements sit further out than the original spread.</summary>
        void Refill()
        {
            _save.pins = Generate(_refillMinMeters, _refillMaxMeters, guaranteeNearPin: false);
            MapPinStore.Save(_save);
            // A short lead-in for the bounce, without the long first-load settle delay.
            SpawnAll(_save.pins, RefillIntroLead);
        }

        void SpawnAll(List<PinDefinition> defs, float baseDelay)
        {
            for (int i = 0; i < defs.Count; i++)
                Spawn(defs[i], baseDelay + i * IntroStagger);
        }

        List<PinDefinition> Generate(float minMeters, float maxMeters, bool guaranteeNearPin)
        {
            var defs = new List<PinDefinition>(_pinCount);
            LatLon centre = _map.CurrentLocation;
            // Guarantee one AR pin spawns within interaction range of the player so there's
            // always something playable the moment they spawn in on device. On a refill we
            // skip this so the whole batch lands further away.
            int nearIndex = guaranteeNearPin && _pinCount > 0 ? Random.Range(0, _pinCount) : -1;
            // Exactly one Premium pin appears on the map (never on the near AR pin's slot);
            // everything else spreads evenly across the three regular kinds.
            int premiumIndex = -1;
            if (_pinCount > 1)
                do { premiumIndex = Random.Range(0, _pinCount); } while (premiumIndex == nearIndex);
            int regularRot = 0;

            for (int i = 0; i < _pinCount; i++)
            {
                PinType type;
                string quest = null;
                int wStart = 0, wEnd = 0;
                int cost;

                if (i == nearIndex)
                {
                    // The guaranteed in-range AR experience.
                    type = PinType.ArChat;
                    quest = QuestNames[i % QuestNames.Length];
                    cost = DefaultEnergyCost;
                }
                else if (i == premiumIndex)
                {
                    type = PinType.Premium;
                    cost = PremiumEnergyCost;
                }
                else
                {
                    type = (PinType)(regularRot % 3); // ArChat / Collect / TimedEvent
                    regularRot++;

                    if (type == PinType.ArChat)
                        quest = QuestNames[i % QuestNames.Length];
                    else if (type == PinType.TimedEvent)
                    {
                        int slots = (DayEndMin - WindowLengthMin - DayStartMin) / 30 + 1; // 9:00..19:00
                        wStart = DayStartMin + Random.Range(0, slots) * 30;
                        wEnd = wStart + WindowLengthMin;
                    }

                    // Chrono Booster pins cost 1-5 energy to claim (Random.Range's int
                    // overload is max-exclusive, so the upper bound is ItemCostMax + 1);
                    // timed events are free to collect within their window.
                    cost = type switch
                    {
                        PinType.Collect => Random.Range(ItemCostMin, ItemCostMax + 1),
                        PinType.TimedEvent => 0,
                        _ => DefaultEnergyCost,
                    };
                }

                // The near AR pin sits ahead of the camera (within a small cone) so it's
                // visible on spawn; every other pin scatters to any bearing.
                float bearing = i == nearIndex
                    ? _map.CameraHeadingRad
                      + Random.Range(-NearBearingSpreadDeg, NearBearingSpreadDeg) * Mathf.Deg2Rad
                    : Random.Range(0f, Mathf.PI * 2f);
                // The near AR pin sits comfortably inside interaction range; everything else
                // spreads across the usual far ring.
                float dist = i == nearIndex
                    ? Random.Range(_interactRangeMeters * NearMinRangeFraction,
                                   _interactRangeMeters * NearMaxRangeFraction)
                    : Random.Range(minMeters, maxMeters);
                LatLon loc = Offset(centre, dist, bearing);

                defs.Add(new PinDefinition($"pin{i}", type, loc, quest, wStart, wEnd, cost));
            }

            // Seed two Portal pins ahead of the player (skipped on a refill batch, which
            // lands everything further away). They flank the camera's facing so both are
            // on screen and within interaction range at spawn.
            if (guaranteeNearPin)
            {
                for (int p = 0; p < PortalPinCount; p++)
                {
                    float side = p == 0 ? -1f : 1f; // one to the left, one to the right
                    float bearing = _map.CameraHeadingRad
                        + side * 0.45f
                        + Random.Range(-NearBearingSpreadDeg, NearBearingSpreadDeg) * 0.5f * Mathf.Deg2Rad;
                    float dist = Random.Range(_interactRangeMeters * NearMinRangeFraction,
                                              _interactRangeMeters * NearMaxRangeFraction);
                    LatLon loc = Offset(centre, dist, bearing);
                    defs.Add(new PinDefinition($"portal{p}", PinType.Portal, loc,
                        energyCost: PortalEnergyCost));
                }
            }

            return defs;
        }

        /// <summary>Move a coordinate <paramref name="metres"/> along a compass <paramref name="bearingRad"/>.</summary>
        static LatLon Offset(LatLon c, float metres, float bearingRad)
        {
            const double metresPerDegLat = 111320.0;
            double north = metres * Mathf.Cos(bearingRad);
            double east = metres * Mathf.Sin(bearingRad);
            double dLat = north / metresPerDegLat;
            double dLon = east / (metresPerDegLat * System.Math.Cos(c.Latitude * System.Math.PI / 180.0));
            return new LatLon(c.Latitude + dLat, c.Longitude + dLon);
        }

        void Spawn(PinDefinition def, float introDelay)
        {
            var go = new GameObject($"Pin_{def.id}");
            go.transform.SetParent(_root, false);
            go.transform.position = _map.GeoToUnity(def.location);

            Sprite glyph = def.type switch
            {
                PinType.ArChat => _glyphChat,
                PinType.Premium => _glyphGem, // gem glyph reads as a premium reward
                PinType.TimedEvent => _glyphClock,
                _ => null,                    // Collect uses the procedural lightning bolt
            };

            var pin = go.AddComponent<MapPin>();
            pin.Init(_map, def, _pinHeight, _cardGap, _cardScale, _interactRangeMeters,
                _teardropOverride, glyph);
            pin.Claimed += HandleClaimed;
            pin.PlayIntro(introDelay);
            _pins.Add(pin);
        }

        /// <summary>A pin was claimed (consumed off the map): forget it so a stale tap
        /// target or open reference can't linger, and drop it from the saved set so it
        /// never respawns on a later visit or after an app restart.</summary>
        void HandleClaimed(MapPin pin)
        {
            if (_openPin == pin) _openPin = null;
            _pins.Remove(pin);

            if (_save == null || pin.Definition == null) return;
            if (_save.pins.RemoveAll(d => d.id == pin.Definition.id) == 0) return;

            // When the last pin is claimed, refill the map with a fresh batch further out;
            // otherwise just persist the smaller set.
            if (_save.pins.Count == 0) Refill();
            else MapPinStore.Save(_save);
        }

        // --- tap routing ---

        void HandleTaps()
        {
            GetPointer(out bool pressed, out Vector2 pos);

            if (pressed && !_wasPressed)
            {
                _downPos = pos;
                _downTime = Time.unscaledTime;
                _moved = false;
            }
            else if (pressed && (pos - _downPos).sqrMagnitude > TapMoveThreshold * TapMoveThreshold)
            {
                _moved = true; // it's a drag (orbit), not a tap
            }
            else if (!pressed && _wasPressed)
            {
                bool quick = Time.unscaledTime - _downTime <= TapMaxDuration;
                if (!_moved && quick && !OverUi()) ResolveTap(_downPos);
            }

            _wasPressed = pressed;
        }

        void ResolveTap(Vector2 screenPos)
        {
            Camera cam = _map.mapCamera != null ? _map.mapCamera : Camera.main;
            if (cam == null) return;

            MapPin hit = null;
            if (Physics.Raycast(cam.ScreenPointToRay(screenPos), out RaycastHit info, 5000f))
                hit = info.collider.GetComponentInParent<MapPin>();

            if (hit != null)
            {
                if (_openPin != null && _openPin != hit) _openPin.Close();
                hit.Toggle();
                _openPin = hit.IsOpen ? hit : null;
            }
            else if (_openPin != null)
            {
                _openPin.Close();
                _openPin = null;
            }
        }

        static bool OverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        static void GetPointer(out bool pressed, out Vector2 pos)
        {
#if ENABLE_INPUT_SYSTEM
            var p = Pointer.current;
            pressed = p != null && p.press.isPressed;
            pos = p != null ? p.position.ReadValue() : Vector2.zero;
#else
            pressed = Input.GetMouseButton(0);
            pos = Input.mousePosition;
#endif
        }
    }
}
