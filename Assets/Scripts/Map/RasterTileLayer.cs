using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace TalesTensor.Map
{
    /// <summary>
    /// Streams Mapbox raster tiles around the player and lays them flat on the XZ
    /// plane as textured quads. Tiles outside the active radius are pooled and
    /// reused, so draw calls and memory stay bounded however far you travel.
    /// Created and driven by <see cref="MapController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class RasterTileLayer : MonoBehaviour
    {
        const int MaxCachedTextures = 192;
        // Upper bound on tile rings so tilting toward the horizon can't try to stream the
        // whole world. (2*5+1)^2 = 121 tiles stays under the texture cache.
        const int MaxRadius = 5;
        // One extra ring beyond the farthest visible ground point, so the map's edge
        // never sits right at the boundary of the view.
        const int RadiusPadding = 1;

        MapController _map;
        readonly Dictionary<long, Tile> _active = new();
        readonly Queue<Tile> _pool = new();
        readonly Dictionary<long, Texture2D> _texCache = new();
        Mesh _quad;
        Material _baseMaterial;
        Transform _root;

        int _lastCenterX = int.MinValue, _lastCenterY = int.MinValue;
        int _lastRadius = -1;

        // Viewport points along the top edge of the frustum (corners + middle), sampled
        // to find the farthest ground the camera can see. Static to avoid per-frame alloc.
        static readonly Vector3[] TopEdgeSamples =
        {
            new Vector3(0f, 1f, 0f), new Vector3(0.5f, 1f, 0f), new Vector3(1f, 1f, 0f),
        };

        class Tile
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public long Key;
            public Coroutine Load;
        }

        public void Init(MapController map, Material baseMaterial)
        {
            _map = map;
            _baseMaterial = baseMaterial;
            _quad = BuildQuad();
            _root = new GameObject("RasterTiles").transform;
            _root.SetParent(transform, false);
        }

        void Update()
        {
            if (_map == null || !_map.HasOrigin) return;

            WebMercator.LatLonToTile(_map.CurrentLocation, _map.Zoom, out double fx, out double fy);
            int cx = Mathf.FloorToInt((float)fx);
            int cy = Mathf.FloorToInt((float)fy);

            // How many rings the current camera tilt needs covered. Recompute the active
            // set whenever the player crosses a tile boundary OR the view reaches farther
            // (e.g. the camera was tilted down toward the horizon).
            int r = ComputeNeededRadius();
            if (cx == _lastCenterX && cy == _lastCenterY && r == _lastRadius) return;
            _lastCenterX = cx;
            _lastCenterY = cy;
            _lastRadius = r;

            var needed = new HashSet<long>();
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int tx = cx + dx, ty = cy + dy;
                if (!IsValidTile(ty, _map.Zoom)) continue;
                long key = TileKey(tx, ty);
                needed.Add(key);
                if (!_active.ContainsKey(key)) Spawn(tx, ty, key);
            }

            var stale = new List<long>();
            foreach (var kv in _active)
                if (!needed.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (long key in stale) Recycle(key);
        }

        /// <summary>
        /// How many tile rings must stay loaded so the map's edge never enters the view.
        /// Projects the top edge of the camera frustum down onto the ground plane and
        /// measures the farthest visible point from the player: the lower the camera
        /// tilts toward the horizon, the farther it sees and the more rings we keep.
        /// Falls back to the inspector <see cref="MapController.rasterRadius"/> as a floor.
        /// </summary>
        int ComputeNeededRadius()
        {
            int floor = Mathf.Max(0, _map.rasterRadius);
            Camera cam = _map.mapCamera;
            float unitsPerTile = _map.UnitsPerTile;
            if (cam == null || unitsPerTile <= 0f) return floor;

            Vector3 player = _map.playerMarker != null
                ? _map.playerMarker.position
                : _map.GeoToUnity(_map.CurrentLocation);

            // Sample the top edge of the frustum (corners + middle). The top reaches
            // farthest across the ground; the corners account for the horizontal spread.
            float maxDist = floor * unitsPerTile;
            bool seesHorizon = false;
            foreach (var vp in TopEdgeSamples)
            {
                Ray ray = cam.ViewportPointToRay(vp);
                if (ray.direction.y < -1e-4f)
                {
                    // Ray angles down to the ground: find where it crosses y = 0.
                    float t = -ray.origin.y / ray.direction.y;
                    Vector3 hit = ray.origin + ray.direction * t;
                    float d = new Vector2(hit.x - player.x, hit.z - player.z).magnitude;
                    if (d > maxDist) maxDist = d;
                }
                else
                {
                    // The top of the view is at or above the horizon — it would see the
                    // edge however many tiles we load, so cover out to the cap.
                    seesHorizon = true;
                }
            }

            int needed = seesHorizon
                ? MaxRadius
                : Mathf.CeilToInt(maxDist / unitsPerTile) + RadiusPadding;
            return Mathf.Clamp(needed, floor, MaxRadius);
        }

        void Spawn(int tx, int ty, long key)
        {
            Tile t = _pool.Count > 0 ? _pool.Dequeue() : Create();
            t.Key = key;
            t.Go.SetActive(true);
            t.Go.name = $"Tile_{_map.Zoom}_{tx}_{ty}";

            Vector3 centre = _map.TileToUnity(tx + 0.5, ty + 0.5);
            t.Go.transform.SetPositionAndRotation(centre, Quaternion.identity);
            t.Go.transform.localScale = new Vector3(_map.UnitsPerTile, 1f, _map.UnitsPerTile);

            _active[key] = t;
            t.Load = StartCoroutine(LoadTexture(t, tx, ty, _map.Zoom));
        }

        Tile Create()
        {
            var go = new GameObject("Tile", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, false);
            go.GetComponent<MeshFilter>().sharedMesh = _quad;
            var mr = go.GetComponent<MeshRenderer>();
            mr.material = new Material(_baseMaterial); // per-tile instance so each gets its own texture
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return new Tile { Go = go, Renderer = mr };
        }

        void Recycle(long key)
        {
            if (!_active.TryGetValue(key, out var t)) return;
            _active.Remove(key);
            if (t.Load != null) StopCoroutine(t.Load);
            t.Go.SetActive(false);
            _pool.Enqueue(t);
        }

        IEnumerator LoadTexture(Tile t, int tx, int ty, int zoom)
        {
            long key = t.Key;
            if (_texCache.TryGetValue(key, out var cached) && cached != null)
            {
                Apply(t, cached);
                yield break;
            }

            string url = _map.RasterTileUrl(tx, ty, zoom);
            using var req = UnityWebRequestTexture.GetTexture(url, true);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[RasterTiles] tile {tx},{ty} failed: {req.error}");
                yield break;
            }

            // The tile may have been recycled while the request was in flight.
            if (t.Key != key || !_active.ContainsKey(key)) yield break;

            var tex = DownloadHandlerTexture.GetContent(req);
            tex.wrapMode = TextureWrapMode.Clamp;
            _texCache[key] = tex;
            Apply(t, tex);
            TrimCache();
        }

        void Apply(Tile t, Texture2D tex)
        {
            var m = t.Renderer.material;
            m.mainTexture = tex;
            m.SetTexture("_BaseMap", tex);
        }

        /// <summary>Evict cached textures for tiles that are no longer active once the cache grows too large.</summary>
        void TrimCache()
        {
            if (_texCache.Count <= MaxCachedTextures) return;
            var remove = new List<long>();
            foreach (var kv in _texCache)
            {
                if (_active.ContainsKey(kv.Key)) continue;
                remove.Add(kv.Key);
                if (_texCache.Count - remove.Count <= MaxCachedTextures) break;
            }
            foreach (long k in remove)
            {
                if (_texCache.TryGetValue(k, out var tex) && tex != null) Destroy(tex);
                _texCache.Remove(k);
            }
        }

        static Mesh BuildQuad()
        {
            // 1x1 quad centred at origin, lying on XZ, facing +Y.
            // +Z is north (top of the tile image), +X is east (right).
            var m = new Mesh { name = "MapQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), // SW
                new Vector3(-0.5f, 0f,  0.5f), // NW
                new Vector3( 0.5f, 0f,  0.5f), // NE
                new Vector3( 0.5f, 0f, -0.5f), // SE
            };
            m.uv = new[]
            {
                new Vector2(0f, 0f), // SW -> bottom-left
                new Vector2(0f, 1f), // NW -> top-left
                new Vector2(1f, 1f), // NE -> top-right
                new Vector2(1f, 0f), // SE -> bottom-right
            };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        static bool IsValidTile(int y, int zoom)
        {
            int n = 1 << zoom;
            return y >= 0 && y < n; // x wraps around the globe; y is clamped to the poles
        }

        static long TileKey(int x, int y) => ((long)(uint)x << 32) | (uint)y;
    }
}
