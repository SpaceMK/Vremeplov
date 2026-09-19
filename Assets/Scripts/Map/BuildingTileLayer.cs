using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using TalesTensor.Map.Mvt;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace TalesTensor.Map
{
    /// <summary>
    /// Streams Mapbox vector tiles around the player, extracts building footprints
    /// and heights, and extrudes them into simple flat-shaded "blocks" combined into
    /// one mesh per tile. Pooled/recycled like the raster layer. An independent,
    /// toggleable layer so low-end devices can run the base map only.
    /// Created and driven by <see cref="MapController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingTileLayer : MonoBehaviour
    {
        const int MaxVertsPerTile = 250_000;
        const string BuildingLayerName = "building";

        MapController _map;
        Material _material;
        int _zoom;
        int _radius;
        float _defaultHeight;
        Transform _root;

        readonly Dictionary<long, BTile> _active = new();
        readonly Queue<BTile> _pool = new();
        int _lastCenterX = int.MinValue, _lastCenterY = int.MinValue;

        // Scratch buffers reused across tiles to avoid per-tile GC.
        readonly List<Vector3> _verts = new();
        readonly List<int> _tris = new();

        class BTile
        {
            public GameObject Go;
            public MeshFilter Filter;
            public Mesh Mesh;
            public long Key;
            public Coroutine Load;
        }

        public void Init(MapController map, Material material, int zoom, int radius, float defaultHeight)
        {
            _map = map;
            _material = material;
            _zoom = zoom;
            _radius = Mathf.Max(0, radius);
            _defaultHeight = defaultHeight;
            _root = new GameObject("Buildings").transform;
            _root.SetParent(transform, false);
        }

        void Update()
        {
            if (_map == null || !_map.HasOrigin || _material == null) return;

            // Centre tile at the building zoom (may differ from the raster zoom).
            WebMercator.LatLonToTile(_map.CurrentLocation, _zoom, out double fx, out double fy);
            int cx = Mathf.FloorToInt((float)fx);
            int cy = Mathf.FloorToInt((float)fy);
            if (cx == _lastCenterX && cy == _lastCenterY) return;
            _lastCenterX = cx;
            _lastCenterY = cy;

            var needed = new HashSet<long>();
            for (int dy = -_radius; dy <= _radius; dy++)
            for (int dx = -_radius; dx <= _radius; dx++)
            {
                int tx = cx + dx, ty = cy + dy;
                int n = 1 << _zoom;
                if (ty < 0 || ty >= n) continue;
                long key = TileKey(tx, ty);
                needed.Add(key);
                if (!_active.ContainsKey(key)) Spawn(tx, ty, key);
            }

            var stale = new List<long>();
            foreach (var kv in _active)
                if (!needed.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (long key in stale) Recycle(key);
        }

        void Spawn(int tx, int ty, long key)
        {
            BTile t = _pool.Count > 0 ? _pool.Dequeue() : Create();
            t.Key = key;
            t.Go.SetActive(true);
            t.Go.name = $"Buildings_{_zoom}_{tx}_{ty}";
            t.Mesh.Clear();
            _active[key] = t;
            t.Load = StartCoroutine(LoadTile(t, tx, ty));
        }

        BTile Create()
        {
            var go = new GameObject("Buildings", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root, false);
            var mesh = new Mesh { name = "BuildingTile", indexFormat = IndexFormat.UInt32 };
            var mf = go.GetComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return new BTile { Go = go, Filter = mf, Mesh = mesh };
        }

        void Recycle(long key)
        {
            if (!_active.TryGetValue(key, out var t)) return;
            _active.Remove(key);
            if (t.Load != null) StopCoroutine(t.Load);
            t.Go.SetActive(false);
            _pool.Enqueue(t);
        }

        IEnumerator LoadTile(BTile t, int tx, int ty)
        {
            long key = t.Key;
            string url = _map.VectorTileUrl(tx, ty, _zoom);
            using var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // 404 just means no building data for this tile — common and harmless.
                if (req.responseCode != 404)
                    Debug.LogWarning($"[Buildings] tile {tx},{ty} failed: {req.error}");
                yield break;
            }
            if (t.Key != key || !_active.ContainsKey(key)) yield break; // recycled mid-flight

            byte[] data = req.downloadHandler.data;
            if (data == null || data.Length == 0) yield break;
            if (data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b) data = Gunzip(data);

            // Spread the (main-thread) build over the next frame so the download
            // frame doesn't also pay the decode+triangulate cost.
            yield return null;
            if (t.Key != key || !_active.ContainsKey(key)) yield break;

            BuildTileMesh(t, tx, ty, data);
        }

        void BuildTileMesh(BTile t, int tx, int ty, byte[] data)
        {
            List<MvtLayer> layers;
            try { layers = MvtTile.Decode(data); }
            catch (System.Exception e) { Debug.LogWarning($"[Buildings] decode {tx},{ty}: {e.Message}"); return; }

            MvtLayer building = null;
            foreach (var l in layers) if (l.Name == BuildingLayerName) { building = l; break; }
            if (building == null) return;

            _verts.Clear();
            _tris.Clear();

            double extent = building.Extent <= 0 ? 4096.0 : building.Extent;
            double invExtent = 1.0 / extent;
            int zoomDelta = _map.Zoom - _zoom;
            double scale = zoomDelta >= 0 ? (1 << zoomDelta) : 1.0 / (1 << -zoomDelta);

            double centerLat = WebMercator.TileToLatLon(tx + 0.5, ty + 0.5, _zoom).Latitude;
            double metersPerUnit = WebMercator.MetersPerTile(centerLat, _map.Zoom) / _map.UnitsPerTile;
            double unitsPerMeter = metersPerUnit > 0 ? 1.0 / metersPerUnit : 0.0;

            foreach (var f in building.Features)
            {
                if (f.Type != GeomType.Polygon) continue;
                if (IsTrue(building.GetAttribute(f, "underground"))) continue;
                object extrude = building.GetAttribute(f, "extrude");
                if (extrude is string es && es == "false") continue;

                float heightM = AsFloat(building.GetAttribute(f, "height"), _defaultHeight);
                if (heightM <= 0f) heightM = _defaultHeight;
                float minM = AsFloat(building.GetAttribute(f, "min_height"), 0f);

                float topY = (float)(heightM * unitsPerMeter);
                float baseY = (float)(minM * unitsPerMeter);
                if (topY <= baseY) topY = baseY + (float)(_defaultHeight * unitsPerMeter);

                var rings = DecodeRings(f.Geometry);
                AppendBuildings(rings, tx, ty, invExtent, scale, baseY, topY);

                if (_verts.Count >= MaxVertsPerTile) break;
            }

            var mesh = t.Mesh;
            mesh.Clear();
            if (_verts.Count == 0) return;
            mesh.SetVertices(_verts);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>Group rings into polygons (exterior + holes) and emit extruded blocks.</summary>
        void AppendBuildings(List<List<Vector2>> rings, int tx, int ty, double invExtent, double scale, float baseY, float topY)
        {
            // Walk rings in order: a positive-area ring opens a new polygon; negative
            // rings are holes of the current polygon (MVT winding convention).
            int i = 0;
            while (i < rings.Count)
            {
                var outer = rings[i++];
                // Consume any following holes (negative-area rings) regardless.
                var holes = new List<List<Vector2>>();
                while (i < rings.Count && SignedArea(rings[i]) < 0)
                {
                    if (rings[i].Count >= 3) holes.Add(rings[i]);
                    i++;
                }
                if (outer.Count >= 3)
                    EmitPolygon(outer, holes, tx, ty, invExtent, scale, baseY, topY);
            }
        }

        void EmitPolygon(List<Vector2> outer, List<List<Vector2>> holes, int tx, int ty,
                         double invExtent, double scale, float baseY, float topY)
        {
            // Flatten outer + holes into earcut input, in lockstep with ground positions.
            int total = outer.Count + SumCounts(holes);
            var data = new double[total * 2];
            var ground = new Vector3[total];
            int[] holeIndices = holes.Count > 0 ? new int[holes.Count] : null;

            int p = 0;
            FillRing(outer, data, ground, ref p, tx, ty, invExtent, scale);
            for (int h = 0; h < holes.Count; h++)
            {
                holeIndices[h] = p;
                FillRing(holes[h], data, ground, ref p, tx, ty, invExtent, scale);
            }

            // Top cap.
            List<int> capTris = Earcut.Tessellate(data, holeIndices, 2);
            int topBase = _verts.Count;
            for (int v = 0; v < total; v++)
                _verts.Add(new Vector3(ground[v].x, topY, ground[v].z));

            for (int k = 0; k + 2 < capTris.Count; k += 3)
            {
                int a = capTris[k], b = capTris[k + 1], c = capTris[k + 2];
                Vector3 va = _verts[topBase + a], vb = _verts[topBase + b], vc = _verts[topBase + c];
                // Keep roof facing up.
                if (Vector3.Cross(vb - va, vc - va).y < 0f) { int tmp = b; b = c; c = tmp; }
                _tris.Add(topBase + a); _tris.Add(topBase + b); _tris.Add(topBase + c);
            }

            // Walls: outer ring faces outward; hole walls face into their courtyard.
            EmitWalls(outer, tx, ty, invExtent, scale, baseY, topY, true);
            for (int h = 0; h < holes.Count; h++)
                EmitWalls(holes[h], tx, ty, invExtent, scale, baseY, topY, false);
        }

        void EmitWalls(List<Vector2> ring, int tx, int ty, double invExtent, double scale,
                       float baseY, float topY, bool outward)
        {
            int count = ring.Count;
            float midY = (baseY + topY) * 0.5f;

            // Ring centroid (XZ), used to orient wall normals consistently.
            Vector3 centroid = Vector3.zero;
            for (int v = 0; v < count; v++)
                centroid += LocalToGround(ring[v].x, ring[v].y, tx, ty, invExtent, scale);
            centroid /= Mathf.Max(1, count);
            centroid.y = midY;

            float desired = outward ? 1f : -1f;

            for (int e = 0; e < count; e++)
            {
                Vector2 l0 = ring[e];
                Vector2 l1 = ring[(e + 1) % count];
                Vector3 g0 = LocalToGround(l0.x, l0.y, tx, ty, invExtent, scale);
                Vector3 g1 = LocalToGround(l1.x, l1.y, tx, ty, invExtent, scale);

                int b = _verts.Count;
                _verts.Add(new Vector3(g0.x, baseY, g0.z)); // 0 bottom-left
                _verts.Add(new Vector3(g1.x, baseY, g1.z)); // 1 bottom-right
                _verts.Add(new Vector3(g1.x, topY, g1.z));  // 2 top-right
                _verts.Add(new Vector3(g0.x, topY, g0.z));  // 3 top-left

                // Winding A => normal ∝ cross(edge, up). Flip if it faces the wrong side.
                Vector3 nA = Vector3.Cross(g1 - g0, Vector3.up);
                Vector3 mid = (g0 + g1) * 0.5f; mid.y = midY;
                bool windA = Vector3.Dot(nA, mid - centroid) * desired >= 0f;

                if (windA)
                {
                    _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2);
                    _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);
                }
                else
                {
                    _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 1);
                    _tris.Add(b); _tris.Add(b + 3); _tris.Add(b + 2);
                }
            }
        }

        void FillRing(List<Vector2> ring, double[] data, Vector3[] ground, ref int p,
                      int tx, int ty, double invExtent, double scale)
        {
            for (int v = 0; v < ring.Count; v++)
            {
                data[p * 2] = ring[v].x;
                data[p * 2 + 1] = ring[v].y;
                ground[p] = LocalToGround(ring[v].x, ring[v].y, tx, ty, invExtent, scale);
                p++;
            }
        }

        Vector3 LocalToGround(double lx, double ly, int tx, int ty, double invExtent, double scale)
        {
            double fx = (tx + lx * invExtent) * scale;
            double fy = (ty + ly * invExtent) * scale;
            return _map.TileToUnity(fx, fy);
        }

        /// <summary>Decode an MVT polygon geometry command stream into tile-local rings.</summary>
        static List<List<Vector2>> DecodeRings(List<uint> geom)
        {
            var rings = new List<List<Vector2>>();
            List<Vector2> cur = null;
            int cx = 0, cy = 0, i = 0;
            while (i < geom.Count)
            {
                uint cmdInt = geom[i++];
                int cmd = (int)(cmdInt & 0x7);
                int count = (int)(cmdInt >> 3);

                if (cmd == 1) // MoveTo — starts a new ring
                {
                    for (int k = 0; k < count && i + 1 < geom.Count; k++)
                    {
                        cx += ZigZag(geom[i++]);
                        cy += ZigZag(geom[i++]);
                        cur = new List<Vector2> { new Vector2(cx, cy) };
                        rings.Add(cur);
                    }
                }
                else if (cmd == 2) // LineTo
                {
                    for (int k = 0; k < count && i + 1 < geom.Count; k++)
                    {
                        cx += ZigZag(geom[i++]);
                        cy += ZigZag(geom[i++]);
                        cur?.Add(new Vector2(cx, cy));
                    }
                }
                // cmd == 7 (ClosePath): nothing to add; ring implicitly closes.
            }
            return rings;
        }

        static int ZigZag(uint n) => (int)(n >> 1) ^ -(int)(n & 1);

        static double SignedArea(List<Vector2> ring)
        {
            double sum = 0;
            int n = ring.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
                sum += (ring[j].x * ring[i].y) - (ring[i].x * ring[j].y);
            return sum * 0.5;
        }

        static int SumCounts(List<List<Vector2>> rings)
        {
            int s = 0;
            foreach (var r in rings) s += r.Count;
            return s;
        }

        static bool IsTrue(object o) => o is bool b && b || o is string s && s == "true";

        static float AsFloat(object o, float fallback)
        {
            if (o is double d) return (float)d;
            if (o is float f) return f;
            if (o is string s && float.TryParse(s, out float r)) return r;
            return fallback;
        }

        static byte[] Gunzip(byte[] gz)
        {
            using var input = new MemoryStream(gz);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        static long TileKey(int x, int y) => ((long)(uint)x << 32) | (uint)y;
    }
}
