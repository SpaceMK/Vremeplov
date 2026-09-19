using BinanceTheme;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TalesTensor.Map
{
    /// <summary>
    /// A small compass pinned to the bottom-right of the map screen. The red/grey needle
    /// (and the N/E/S/W letters) rotate with the camera so the red end always points to
    /// true north.
    ///
    /// Built entirely in code (no prefab), created by <see cref="MapController"/>.
    /// Reads the camera heading each frame from <see cref="MapController.CameraHeadingRad"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class CompassHud : MonoBehaviour
    {
        const float Size = 150f;       // dial diameter (reference px)
        const float NeedleSize = 120f; // needle artwork box inside the dial
        const float Margin = 40f;      // gap from the screen's safe-area corner

        MapController _map;
        RectTransform _dial;       // rotates with the heading
        RectTransform _safeArea;
        Rect _appliedSafeArea;

        public static CompassHud Create(MapController map)
        {
            // Start inactive so Awake (which builds the UI) waits until the map is wired.
            var go = new GameObject("CompassHud");
            go.SetActive(false);
            var hud = go.AddComponent<CompassHud>();
            hud._map = map;
            go.SetActive(true);
            return hud;
        }

        void Awake() => Build();

        void Build()
        {
            var canvasGo = new GameObject("CompassHudCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 95; // just under the energy HUD (100)
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // Keep the compass clear of the notch / rounded corners.
            _safeArea = UiFactory.Rect("SafeArea", canvasGo.transform);
            ApplySafeArea();

            // Bottom-right anchored container.
            var compass = UiFactory.Rect("Compass", _safeArea);
            UiFactory.Anchor(compass,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-Margin, Margin), new Vector2(Size, Size));

            // Dial face: a faint ring (slightly larger, line colour) under the surface disc.
            var ring = UiFactory.Icon("Ring", compass, ProceduralIcons.Circle(), BinancePalette.Line);
            UiFactory.Stretch(ring.rectTransform);
            var face = UiFactory.Icon("Face", compass, ProceduralIcons.Circle(),
                Alpha(BinancePalette.Surface, 0.92f));
            UiFactory.Anchor(face.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(Size - 8f, Size - 8f));

            // Rotating needle group (red north + grey south), pivoting about the centre.
            _dial = UiFactory.Rect("Dial", compass);
            UiFactory.Stretch(_dial);

            var north = UiFactory.Icon("North", _dial, ProceduralIcons.Needle(), BinancePalette.Negative);
            CentreNeedle(north.rectTransform, 0f);
            var south = UiFactory.Icon("South", _dial, ProceduralIcons.Needle(), BinancePalette.TextSecondary);
            CentreNeedle(south.rectTransform, 180f);

            // Cardinal letters ride the dial too, so each stays at its true heading.
            Cardinal("N", Vector2.up, BinancePalette.Negative);
            Cardinal("E", Vector2.right, BinancePalette.TextPrimary);
            Cardinal("S", Vector2.down, BinancePalette.TextPrimary);
            Cardinal("W", Vector2.left, BinancePalette.TextPrimary);
        }

        void CentreNeedle(RectTransform rt, float zRotation)
        {
            UiFactory.Anchor(rt,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(NeedleSize, NeedleSize));
            rt.localRotation = Quaternion.Euler(0f, 0f, zRotation);
        }

        void Cardinal(string letter, Vector2 dir, Color color)
        {
            const float radius = 58f; // sits between the needle tip and the ring
            var t = UiFactory.Text("Card_" + letter, _dial, letter, 28f, color, TextAlignmentOptions.Center);
            UiFactory.Anchor(t.rectTransform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                dir * radius, new Vector2(34f, 34f));
        }

        void Update()
        {
            if (_map == null || _dial == null) return;
            ApplySafeArea();
            // Heading is the camera yaw; rotating the dial by +heading sweeps the red north
            // needle the right way (e.g. face east -> north appears to the left).
            float headingDeg = _map.CameraHeadingRad * Mathf.Rad2Deg;
            _dial.localRotation = Quaternion.Euler(0f, 0f, headingDeg);
        }

        /// <summary>Shrink the HUD container to the device safe area (re-applied on rotation).</summary>
        void ApplySafeArea()
        {
            if (_safeArea == null) return;
            Rect sa = Screen.safeArea;
            if (sa == _appliedSafeArea || Screen.width == 0 || Screen.height == 0) return;
            _appliedSafeArea = sa;

            Vector2 min = sa.position;
            Vector2 max = sa.position + sa.size;
            min.x /= Screen.width; min.y /= Screen.height;
            max.x /= Screen.width; max.y /= Screen.height;
            _safeArea.anchorMin = min;
            _safeArea.anchorMax = max;
            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
        }

        static Color Alpha(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
