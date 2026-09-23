using System.Collections.Generic;
using UnityEngine;

namespace TalesTensor.Map
{
    /// <summary>
    /// Generates simple white-silhouette icon <see cref="Sprite"/>s at runtime so the
    /// feature ships with no art-asset dependency. Shapes are drawn as the union of a
    /// few analytic primitives (discs, capsules, polygons), 4x supersampled for clean
    /// edges, on a transparent background — callers tint them via <c>Image.color</c> /
    /// <c>SpriteRenderer.color</c>. Every result is cached and reused.
    ///
    /// Each consumer also exposes a public <c>Sprite</c> field; when that is assigned
    /// in the inspector it overrides the generated fallback, so real art drops in with
    /// no code change.
    /// </summary>
    public static class ProceduralIcons
    {
        const int Size = 128;
        static readonly Dictionary<string, Sprite> _cache = new();

        public static Sprite Lightning() => Get("bolt", Size, DrawLightning, new Vector2(0.5f, 0.5f));
        public static Sprite ChatBubble() => Get("chat", Size, DrawChat, new Vector2(0.5f, 0.5f));
        public static Sprite Gem() => Get("gem", Size, DrawGem, new Vector2(0.5f, 0.5f));
        public static Sprite Clock() => Get("clock", Size, DrawClock, new Vector2(0.5f, 0.5f));
        public static Sprite Profile() => Get("profile", Size, DrawProfile, new Vector2(0.5f, 0.5f));

        /// <summary>A classic map "teardrop" pin (used as the white outer border). Pivot is the tip.</summary>
        public static Sprite Teardrop() => Get("teardrop", Size, DrawTeardrop, new Vector2(0.5f, 0f));

        /// <summary>A teardrop inset within <see cref="Teardrop"/> — the coloured fill that leaves an
        /// even white border when drawn over the outline at the same transform. Shares the tip pivot.</summary>
        public static Sprite TeardropInner() => Get("teardrop_inner", Size, DrawTeardropInner, new Vector2(0.5f, 0f));

        /// <summary>A soft rounded panel, for card / tooltip backgrounds. Centre pivot.</summary>
        public static Sprite RoundedPanel() => Get("panel", 64, c => DrawRoundRect(c, 64, 0.04f, 0.04f, 0.96f, 0.96f, 0.18f), new Vector2(0.5f, 0.5f));

        /// <summary>A filled circle, for dial / badge backgrounds. Centre pivot.</summary>
        public static Sprite Circle() => Get("circle", Size, m => Fill(m, Disc(0.5f, 0.5f, 0.48f)), new Vector2(0.5f, 0.5f));

        /// <summary>A hollow ring — the portal glyph. Centre pivot.</summary>
        public static Sprite Ring() => Get("ring", Size, m => Fill(m, Annulus(0.5f, 0.5f, 0.48f, 0.28f)), new Vector2(0.5f, 0.5f));

        /// <summary>A rolled parchment scroll (letter). Centre pivot.</summary>
        public static Sprite Scroll() => Get("scroll", Size, DrawScroll, new Vector2(0.5f, 0.5f));

        /// <summary>A five-petal flower / bud — Gonxhe's Flower. Centre pivot.</summary>
        public static Sprite Flower() => Get("flower", Size, DrawFlower, new Vector2(0.5f, 0.5f));

        static void DrawFlower(bool[,] mask)
        {
            // A round centre ringed by five petals.
            Inside centre = Disc(0.5f, 0.5f, 0.15f);
            const float petalR = 0.18f;   // petal radius
            const float ringR = 0.27f;    // petal centre offset from the middle
            Fill(mask, (x, y) =>
            {
                if (centre(x, y)) return true;
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.PI / 2f + i * (Mathf.PI * 2f / 5f); // first petal points up
                    float px = 0.5f + Mathf.Cos(a) * ringR;
                    float py = 0.5f + Mathf.Sin(a) * ringR;
                    if ((x - px) * (x - px) + (y - py) * (y - py) <= petalR * petalR) return true;
                }
                return false;
            });
        }

        static void DrawScroll(bool[,] mask)
        {
            // Parchment body capped by a rolled cylinder at the top and bottom.
            Inside body = RoundRect(0.30f, 0.22f, 0.70f, 0.78f, 0.04f);
            Inside topRoll = Capsule(0.20f, 0.79f, 0.80f, 0.79f, 0.09f);
            Inside bottomRoll = Capsule(0.20f, 0.21f, 0.80f, 0.21f, 0.09f);
            Fill(mask, (x, y) => body(x, y) || topRoll(x, y) || bottomRoll(x, y));
        }

        /// <summary>A slim needle pointing up: apex at the top, base at the centre. Centre
        /// pivot so it sweeps about the dial centre when its container is rotated.</summary>
        public static Sprite Needle() => Get("needle", Size, DrawNeedle, new Vector2(0.5f, 0.5f));

        static void DrawNeedle(bool[,] mask)
        {
            var p = new[] { new Vector2(0.5f, 0.92f), new Vector2(0.42f, 0.5f), new Vector2(0.58f, 0.5f) };
            Fill(mask, (x, y) => PointInPoly(p, x, y));
        }

        // --- primitive inside-tests (normalised 0..1 space, y up) ---

        delegate bool Inside(float x, float y);

        static Sprite Get(string key, int size, System.Action<bool[,]> draw, Vector2 pivot)
        {
            string ck = $"{key}_{size}";
            if (_cache.TryGetValue(ck, out var cached) && cached != null) return cached;

            // High-res inside mask (supersampled), then box-downfilter to coverage.
            const int ss = 4;
            int hi = size * ss;
            var mask = new bool[hi, hi];
            draw(mask);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < ss; sy++)
                for (int sx = 0; sx < ss; sx++)
                    if (mask[y * ss + sy, x * ss + sx]) hits++;
                byte a = (byte)(255 * hits / (ss * ss));
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size),
                pivot, size, 0, SpriteMeshType.FullRect);
            _cache[ck] = sprite;
            return sprite;
        }

        static void Fill(bool[,] mask, Inside inside)
        {
            int n = mask.GetLength(0);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                float v = (y + 0.5f) / n;
                if (inside(u, v)) mask[y, x] = true;
            }
        }

        // --- shape definitions ---

        static void DrawLightning(bool[,] mask)
        {
            // A bolt zig-zag as one filled polygon.
            var p = new[]
            {
                new Vector2(0.58f, 0.95f), new Vector2(0.30f, 0.50f), new Vector2(0.48f, 0.50f),
                new Vector2(0.40f, 0.05f), new Vector2(0.72f, 0.55f), new Vector2(0.52f, 0.55f),
            };
            Fill(mask, (x, y) => PointInPoly(p, x, y));
        }

        static void DrawChat(bool[,] mask)
        {
            Inside body = RoundRect(0.12f, 0.32f, 0.88f, 0.90f, 0.16f);
            var tail = new[] { new Vector2(0.30f, 0.34f), new Vector2(0.30f, 0.08f), new Vector2(0.52f, 0.34f) };
            Fill(mask, (x, y) => body(x, y) || PointInPoly(tail, x, y));
        }

        static void DrawGem(bool[,] mask)
        {
            // Faceted diamond silhouette.
            var p = new[]
            {
                new Vector2(0.50f, 0.95f), new Vector2(0.86f, 0.62f), new Vector2(0.68f, 0.10f),
                new Vector2(0.32f, 0.10f), new Vector2(0.14f, 0.62f),
            };
            Fill(mask, (x, y) => PointInPoly(p, x, y));
        }

        static void DrawClock(bool[,] mask)
        {
            Inside ring = Annulus(0.5f, 0.5f, 0.42f, 0.32f);
            Inside hub = Disc(0.5f, 0.5f, 0.05f);
            Inside hourHand = Capsule(0.5f, 0.5f, 0.5f, 0.74f, 0.045f);   // up
            Inside minHand = Capsule(0.5f, 0.5f, 0.70f, 0.50f, 0.045f);   // right
            Fill(mask, (x, y) => ring(x, y) || hub(x, y) || hourHand(x, y) || minHand(x, y));
        }

        static void DrawProfile(bool[,] mask)
        {
            // A person bust: round head over an upper-half ellipse for the shoulders.
            Inside head = Disc(0.5f, 0.70f, 0.17f);
            Inside shoulders = (x, y) =>
            {
                if (y < 0.20f) return false;
                float nx = (x - 0.5f) / 0.34f, ny = (y - 0.20f) / 0.30f;
                return nx * nx + ny * ny <= 1f;
            };
            Fill(mask, (x, y) => head(x, y) || shoulders(x, y));
        }

        static void DrawTeardrop(bool[,] mask)
        {
            // Round head over a point: union of a disc and a downward triangle that
            // meets the disc tangentially, giving the familiar pin outline.
            Inside head = Disc(0.5f, 0.66f, 0.30f);
            var point = new[] { new Vector2(0.22f, 0.70f), new Vector2(0.78f, 0.70f), new Vector2(0.5f, 0.04f) };
            Fill(mask, (x, y) => head(x, y) || PointInPoly(point, x, y));
        }

        static void DrawTeardropInner(bool[,] mask)
        {
            // Same silhouette as DrawTeardrop, inset ~0.05 in normalised space so the
            // white outline underneath shows through as a roughly even border.
            Inside head = Disc(0.5f, 0.645f, 0.245f);
            var point = new[] { new Vector2(0.28f, 0.685f), new Vector2(0.72f, 0.685f), new Vector2(0.5f, 0.13f) };
            Fill(mask, (x, y) => head(x, y) || PointInPoly(point, x, y));
        }

        static void DrawRoundRect(bool[,] mask, int _, float x0, float y0, float x1, float y1, float r)
        {
            Inside rr = RoundRect(x0, y0, x1, y1, r);
            Fill(mask, rr);
        }

        // --- analytic primitives ---

        static Inside Disc(float cx, float cy, float r) =>
            (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

        static Inside Annulus(float cx, float cy, float rOut, float rIn) =>
            (x, y) =>
            {
                float d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                return d2 <= rOut * rOut && d2 >= rIn * rIn;
            };

        static Inside Capsule(float ax, float ay, float bx, float by, float halfW) =>
            (x, y) => DistToSegment(x, y, ax, ay, bx, by) <= halfW;

        static Inside RoundRect(float x0, float y0, float x1, float y1, float r) =>
            (x, y) =>
            {
                if (x < x0 || x > x1 || y < y0 || y > y1) return false;
                // Clamp to the inner rect inflated by r; outside the corner arcs is excluded.
                float ix = Mathf.Clamp(x, x0 + r, x1 - r);
                float iy = Mathf.Clamp(y, y0 + r, y1 - r);
                return (x - ix) * (x - ix) + (y - iy) * (y - iy) <= r * r;
            };

        static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float t = len2 > 0 ? ((px - ax) * dx + (py - ay) * dy) / len2 : 0f;
            t = Mathf.Clamp01(t);
            float cx = ax + t * dx, cy = ay + t * dy;
            return Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
        }

        static bool PointInPoly(Vector2[] p, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            {
                if ((p[i].y > y) != (p[j].y > y) &&
                    x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                    inside = !inside;
            }
            return inside;
        }
    }
}
