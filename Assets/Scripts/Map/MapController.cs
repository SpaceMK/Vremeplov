using TalesTensor.Quests;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TalesTensor.Map
{
    /// <summary>
    /// Heart of the world-map screen. Polls the player's GPS position, converts it
    /// to a flat Unity position via Web Mercator, drives the player marker and the
    /// tilted follow-camera, and self-assembles the tile layer. Other systems
    /// (e.g. interactive points) place objects at real-world coordinates through
    /// <see cref="GeoToUnity"/>.
    ///
    /// Drop this single component on a GameObject in the scene — it adds the GPS and
    /// raster-tile components itself, so no extra wiring is required.
    /// </summary>
    [DisallowMultipleComponent]
    public class MapController : MonoBehaviour
    {
        [Header("Mapbox")]
        [Tooltip("Your Mapbox access token (https://account.mapbox.com/). Required.")]
        public string mapboxToken = "";
        [Tooltip("Mapbox style id used for the raster base map.")]
        public string mapStyle = "streets-v12";
        [Tooltip("Raster tile pixel size: 256 (lighter) or 512 (sharper).")]
        public int tilePixelSize = 512;

        [Header("Map layout")]
        [Tooltip("Slippy-map zoom level (15-17 suits street-level play).")]
        public int Zoom = 17;
        [Tooltip("Unity world units per map-tile edge. Scales the whole map.")]
        public float UnitsPerTile = 24f;
        [Tooltip("How many tile rings of raster map to keep loaded around the player.")]
        public int rasterRadius = 2;

        [Header("Camera")]
        [Tooltip("Camera that follows the player. Falls back to Camera.main if unset.")]
        public Camera mapCamera;
        [Tooltip("Starting camera tilt away from straight-down, in degrees (90 = top-down).")]
        public float cameraPitch = 55f;
        [Tooltip("Camera distance from the player along its view direction.")]
        public float cameraDistance = 30f;

        [Header("Camera orbit (hold-drag)")]
        [Tooltip("Yaw rotation per pixel dragged horizontally.")]
        public float orbitYawSpeed = 0.25f;
        [Tooltip("Pitch change per pixel dragged vertically.")]
        public float orbitPitchSpeed = 0.15f;
        [Tooltip("Lowest tilt the drag can reach (closer to the horizon).")]
        public float minPitch = 40f;
        [Tooltip("Highest tilt the drag can reach (closer to top-down).")]
        public float maxPitch = 75f;

        [Header("Player marker")]
        [Tooltip("Marker that represents the player. One is created if left empty.")]
        public Transform playerMarker;
        [Tooltip("Seconds to glide the player marker toward each new GPS reading, so it " +
                 "moves smoothly between the discrete (~1 Hz) device fixes instead of " +
                 "snapping point to point. 0 = snap instantly.")]
        public float playerFollowSmoothTime = 0.3f;

        [Header("Materials")]
        [Tooltip("Material for raster map tiles (URP/Unlit). Instanced per tile.")]
        public Material tileMaterial;
        [Tooltip("Material for the auto-created player marker (URP/Lit).")]
        public Material markerMaterial;

        [Header("Buildings (Stage 2)")]
        [Tooltip("Extrude 3D building blocks from Mapbox vector tiles. Disable for the lightest base map.")]
        public bool buildingsEnabled = true;
        [Tooltip("Material for the extruded building blocks (URP/Lit, ideally double-sided).")]
        public Material buildingMaterial;
        [Tooltip("How many tile rings of buildings to keep loaded around the player.")]
        [Range(0, 3)] public int buildingRadius = 1;
        [Tooltip("Height (metres) used for buildings whose data has no height.")]
        public float defaultBuildingHeight = 8f;

        [Header("Pins & Energy")]
        [Tooltip("Spawn the interactive experience pins scattered around the player.")]
        public bool pinsEnabled = true;
        [Tooltip("How many demo pins to scatter around the first GPS fix.")]
        public int pinCount = 6;
        [Tooltip("How close (real-world metres) the player must be to enter a pin's experience.")]
        public float interactRangeMeters = 250f;
        [Tooltip("If any fixed-location pin (the offline Skopje walk) is farther than this from " +
                 "the player's first fix, the whole layout is shrunk toward the player so the " +
                 "farthest sits at this radius. 0 = keep real positions.")]
        public float pinClampRadiusMeters = 200f;
        [Tooltip("Nearest / farthest a generated pin is placed from the player, in metres.")]
        public float pinMinMeters = 40f;
        public float pinMaxMeters = 110f;
        [Tooltip("When the map is refilled after every pin has been claimed, the new pins " +
                 "spawn in this farther band (metres) — further out than the initial spread " +
                 "but kept within viewing distance so they're still on screen.")]
        public float pinRefillMinMeters = 90f;
        public float pinRefillMaxMeters = 150f;
        [Tooltip("Pin marker height in Unity units.")]
        public float pinWorldHeight = 3f;
        [Tooltip("Gap between the pin and its expanded card, in Unity units.")]
        public float pinCardGap = 0.15f;
        [Tooltip("World scale of the pin card (multiplies its pixel size).")]
        public float pinCardScale = 0.018f;
        [Tooltip("Show the energy HUD at the top of this scene.")]
        public bool energyHudEnabled = true;
        [Tooltip("Show a small compass in the bottom-right that tracks the look direction.")]
        public bool compassEnabled = true;
        [Tooltip("Show the top-left profile button + shared 'Coming soon' popup.")]
        public bool profileMenuEnabled = true;
        [Tooltip("Show a full-screen 'waiting for GPS / permission needed' overlay until the first fix.")]
        public bool locationOverlayEnabled = true;

        [Header("Quest Engine (live portals)")]
        [Tooltip("Base URL of the Tales Tensor Quest Engine. Leave as the default unless self-hosting.")]
        public string questEngineBaseUrl = QuestEngineConfig.DefaultBaseUrl;
        [Tooltip("X-API-Key for GET /portals/nearby. REQUIRED to show live quests. " +
                 "Leave blank to use the offline demo pins instead. Treat as a secret — " +
                 "prefer a build-config/secret over committing it here.")]
        public string questEngineApiKey = "";
        [Tooltip("Search radius (metres) for live portals around the player.")]
        public float questFetchRadiusMeters = QuestEngineConfig.DefaultRadiusMeters;

        [Header("Time Portal (rendered portal videos)")]
        [Tooltip("Base URL of the Time Portal Builder service. Live portal_video scenes look up " +
                 "their rendered video here. Leave as the default unless self-hosting.")]
        public string timePortalBaseUrl = TimePortalConfig.DefaultBaseUrl;
        [Tooltip("Optional X-API-Key for the Time Portal read endpoints (they are open today).")]
        public string timePortalApiKey = "";
        [Tooltip("Seconds a Time Portal lookup stays cached before a map refresh re-queries it.")]
        public float timePortalCacheSeconds = TimePortalConfig.DefaultCacheSeconds;

        [Header("Pin & energy art overrides (optional)")]
        [Tooltip("Override the generated teardrop pin body sprite.")]
        public Sprite pinTeardropSprite;
        public Sprite pinChatSprite;
        public Sprite pinGemSprite;
        public Sprite pinClockSprite;
        [Tooltip("Override the generated lightning energy icon.")]
        public Sprite energySprite;
        [Tooltip("Override the generated profile icon.")]
        public Sprite profileSprite;

        public bool HasOrigin { get; private set; }
        public LatLon CurrentLocation { get; private set; }

        /// <summary>The compass bearing (radians) the camera currently faces, matching the
        /// convention used by pin placement (0 = north, increasing clockwise toward east).
        /// The camera's horizontal forward is (sin yaw, 0, cos yaw), so the heading is the
        /// yaw itself.</summary>
        public float CameraHeadingRad => _yaw * Mathf.Deg2Rad;

        // mapbox.mapbox-streets-v8 vector tiles are served up to zoom 16.
        const int VectorMaxZoom = 16;

        GpsLocationProvider _gps;
        RasterTileLayer _raster;
        BuildingTileLayer _buildings;
        double _originX, _originY;
        float _yaw;
        float _pitch;
        Vector3 _followVel; // SmoothDamp velocity for the player-marker glide

        void Awake()
        {
            // Fall back to a gitignored Resources/MapboxSecrets.json when the Inspector
            // field is blank, so builds and dev machines can be provisioned without
            // committing the token to the scene.
            if (string.IsNullOrWhiteSpace(mapboxToken))
                mapboxToken = LoadMapboxTokenFromResources();

            _gps = GetComponent<GpsLocationProvider>();
            if (_gps == null) _gps = gameObject.AddComponent<GpsLocationProvider>();

            _yaw = 0f;
            _pitch = cameraPitch;

            if (mapCamera == null) mapCamera = Camera.main;
            if (playerMarker == null) playerMarker = CreateDefaultMarker();

            // Make simulated WASD movement follow the camera's heading.
            if (mapCamera != null) _gps.headingReference = mapCamera.transform;

            // Cover the (blank) map with a waiting/permission overlay until we have a fix.
            if (locationOverlayEnabled) GpsStatusOverlay.Create(_gps);

            if (tileMaterial == null)
                Debug.LogWarning("[Map] No tile material assigned — tiles will not render.");

            _raster = gameObject.AddComponent<RasterTileLayer>();
            _raster.Init(this, tileMaterial);

            if (buildingsEnabled)
            {
                if (buildingMaterial == null)
                    Debug.LogWarning("[Map] Buildings enabled but no building material assigned.");
                int buildingZoom = Mathf.Min(Zoom, VectorMaxZoom);
                _buildings = gameObject.AddComponent<BuildingTileLayer>();
                _buildings.Init(this, buildingMaterial, buildingZoom, buildingRadius, defaultBuildingHeight);
            }

            // Provision the Quest Engine integration before pins spawn, so MapPinLayer can
            // decide whether to fetch live portals or fall back to offline demo pins.
            QuestEngineConfig.Configure(questEngineBaseUrl, questEngineApiKey, questFetchRadiusMeters);
            TimePortalConfig.Configure(timePortalBaseUrl, timePortalApiKey, timePortalCacheSeconds);

            if (pinsEnabled)
            {
                var pins = gameObject.AddComponent<MapPinLayer>();
                pins.Init(this, pinCount, interactRangeMeters, pinMinMeters, pinMaxMeters,
                    pinRefillMinMeters, pinRefillMaxMeters,
                    pinWorldHeight, pinCardGap, pinCardScale,
                    pinTeardropSprite, pinChatSprite, pinGemSprite, pinClockSprite);
            }

            if (energyHudEnabled)
                EnergyHud.Create(energySprite);

            if (compassEnabled)
                CompassHud.Create(this);

            if (profileMenuEnabled)
            {
                MapMenu.Create(profileSprite);
                ProfileView.Create(); // owns the profile sheet + level-up popup
            }

            // Award experience for moving around (real GPS, or WASD in the editor).
            gameObject.AddComponent<MovementXpTracker>().Init(this);

            // If the player just completed a delivery quest in AR, celebrate now (after
            // ProfileView is listening): refill energy and grant the level-up XP.
            ApplyPendingQuestReward();

            if (string.IsNullOrEmpty(mapboxToken))
                Debug.LogWarning("[Map] No Mapbox token set — tiles will not load.");
        }

        /// <summary>Apply a deferred delivery-quest reward: refill energy to full, then grant
        /// the XP (which raises <see cref="ProfileView"/>'s level-up popup, noting the energy).
        /// The flag stays set across the XP grant so the popup can read it.</summary>
        void ApplyPendingQuestReward()
        {
            if (!QuestRewards.CelebratePending) return;

            EnergyController.Instance.Gain(EnergyModel.Max); // refill to full
            PlayerProgress.Instance.AddXp(QuestRewards.PendingXp);

            QuestRewards.CelebratePending = false;
            QuestRewards.PendingXp = 0;
        }

        void Update()
        {
            if (_gps == null || !_gps.HasFix) return;
            CurrentLocation = _gps.Current;

            bool firstFix = !HasOrigin;
            if (!HasOrigin)
            {
                WebMercator.LatLonToTile(CurrentLocation, Zoom, out _originX, out _originY);
                HasOrigin = true;
            }

            // Where GPS says we are, in world space. The marker glides toward this rather
            // than snapping, so the discrete device fixes read as smooth movement; the
            // camera then follows the smoothed marker, not the raw target. CurrentLocation
            // stays the raw reading so tiles/pins still key off the true position.
            Vector3 target = GeoToUnity(CurrentLocation);
            Vector3 p = target;
            if (playerMarker != null)
            {
                // Snap on the first fix (so we don't slide in from the world origin) and
                // when smoothing is disabled; otherwise ease toward the new reading.
                if (firstFix || playerFollowSmoothTime <= 0f)
                    _followVel = Vector3.zero;
                else
                    p = Vector3.SmoothDamp(playerMarker.position, target, ref _followVel,
                                           playerFollowSmoothTime);
                playerMarker.position = p;
            }

            HandleCameraInput();
            UpdateCamera(p);
        }

        /// <summary>Hold left-click / a finger and drag to orbit (yaw) and tilt (pitch) the view.</summary>
        void HandleCameraInput()
        {
            // Don't orbit when the drag is over UI (e.g. scrolling the profile sheet or
            // any open menu) — the menu should consume that gesture, not the camera.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 delta;
#if ENABLE_INPUT_SYSTEM
            var pointer = Pointer.current;
            if (pointer == null || !pointer.press.isPressed) return;
            delta = pointer.delta.ReadValue();
#else
            if (!Input.GetMouseButton(0)) return;
            delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#endif
            if (delta.sqrMagnitude < 0.0001f) return;

            _yaw += delta.x * orbitYawSpeed;
            // Drag up -> look toward the horizon (lower pitch); drag down -> more top-down.
            _pitch = Mathf.Clamp(_pitch - delta.y * orbitPitchSpeed, minPitch, maxPitch);
        }

        void UpdateCamera(Vector3 target)
        {
            if (mapCamera == null) return;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 forward = rot * Vector3.forward;            // camera looks along this
            mapCamera.transform.position = target - forward * cameraDistance;
            mapCamera.transform.rotation = rot;
        }

        // --- Public coordinate API (used by tiles, buildings, interactive points) ---

        /// <summary>Convert a fractional-tile coordinate (at <see cref="Zoom"/>) to a Unity position.</summary>
        public Vector3 TileToUnity(double tileX, double tileY) =>
            new Vector3((float)((tileX - _originX) * UnitsPerTile), 0f,
                        (float)((_originY - tileY) * UnitsPerTile));

        /// <summary>Convert a real-world coordinate to a Unity position on the map. The seam for interactive points.</summary>
        public Vector3 GeoToUnity(LatLon c)
        {
            WebMercator.LatLonToTile(c, Zoom, out double fx, out double fy);
            return TileToUnity(fx, fy);
        }

        /// <summary>Convert a Unity position on the map back to a real-world coordinate.</summary>
        public LatLon UnityToGeo(Vector3 p)
        {
            double fx = _originX + p.x / UnitsPerTile;
            double fy = _originY - p.z / UnitsPerTile;
            return WebMercator.TileToLatLon(fx, fy, Zoom);
        }

        /// <summary>Metres per Unity unit at the current map origin — handy for sizing real-world content.</summary>
        public double MetersPerUnit =>
            WebMercator.MetersPerTile(CurrentLocation.Latitude, Zoom) / UnitsPerTile;

        /// <summary>Shape of <c>Assets/Resources/MapboxSecrets.json</c>. Optional file;
        /// missing or empty just means "no token here", the same as leaving it out.</summary>
        [System.Serializable]
        class MapboxSecretsFile { public string mapboxToken; }

        /// <summary>Load the Mapbox access token from the gitignored Resources file, if
        /// present. Never throws — a missing or malformed file returns empty.</summary>
        static string LoadMapboxTokenFromResources()
        {
            try
            {
                var text = Resources.Load<TextAsset>("MapboxSecrets");
                if (text == null || string.IsNullOrWhiteSpace(text.text)) return "";
                var parsed = JsonUtility.FromJson<MapboxSecretsFile>(text.text);
                return (parsed?.mapboxToken ?? "").Trim();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Map] could not read Resources/MapboxSecrets.json: {e.Message}");
                return "";
            }
        }

        public string RasterTileUrl(int x, int y, int z)
        {
            int n = 1 << z;
            x = ((x % n) + n) % n; // wrap longitude across the antimeridian
            return $"https://api.mapbox.com/styles/v1/mapbox/{mapStyle}/tiles/{tilePixelSize}/{z}/{x}/{y}" +
                   $"?access_token={mapboxToken}";
        }

        public string VectorTileUrl(int x, int y, int z)
        {
            int n = 1 << z;
            x = ((x % n) + n) % n; // wrap longitude
            return $"https://api.mapbox.com/v4/mapbox.mapbox-streets-v8/{z}/{x}/{y}.vector.pbf" +
                   $"?access_token={mapboxToken}";
        }

        Transform CreateDefaultMarker()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "PlayerMarker";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // A third thinner on X/Z than tall, so it blocks less of the map.
            go.transform.localScale = new Vector3(1.33f, 2f, 1.33f);

            var mr = go.GetComponent<MeshRenderer>();
            if (markerMaterial != null) mr.sharedMaterial = markerMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }
    }
}
