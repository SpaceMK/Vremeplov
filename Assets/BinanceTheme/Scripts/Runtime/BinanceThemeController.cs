using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BinanceTheme
{
    /// <summary>
    /// Drop one on a Canvas (or any UI root) to re-skin everything beneath it to the
    /// Binance look in a single pass — buttons, panels, text and fonts. Elements tagged
    /// with a <see cref="ThemedElement"/> use their explicit role; everything else is
    /// inferred. Runs at runtime (Awake) and can be invoked from the editor menu.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class BinanceThemeController : MonoBehaviour
    {
        [Tooltip("Theme asset to apply. If empty, a default theme is built from BinancePalette (colors only — no font).")]
        public BinanceTheme theme;

        [Tooltip("Re-skin automatically when the scene starts.")]
        public bool applyOnAwake = true;

        [Tooltip("Tint the main camera's solid background to the theme background. " +
                 "Leave OFF for AR scenes (the camera feed must stay visible).")]
        public bool tintCameraBackground = false;

        void Awake()
        {
            if (applyOnAwake) Apply();
        }

        /// <summary>Apply the theme to this object and all UI descendants.</summary>
        public void Apply()
        {
            var t = ResolveTheme();
            var handled = new HashSet<GameObject>();

            // 1) Selectables (buttons etc.) own their whole subtree's coloring.
            foreach (var sel in GetComponentsInChildren<Selectable>(true))
            {
                var te = sel.GetComponent<ThemedElement>();
                UIRole role = te != null ? te.role : UIRole.Auto;
                ThemeApplier.Apply(t, sel.gameObject, role);
                foreach (var g in sel.GetComponentsInChildren<Graphic>(true))
                    handled.Add(g.gameObject);
            }

            // 2) Explicitly tagged elements (non-selectable).
            foreach (var te in GetComponentsInChildren<ThemedElement>(true))
            {
                if (handled.Contains(te.gameObject)) continue;
                te.Apply(t);
                handled.Add(te.gameObject);
            }

            // 3) Everything else, inferred.
            foreach (var g in GetComponentsInChildren<Graphic>(true))
            {
                if (handled.Contains(g.gameObject)) continue;
                ThemeApplier.Apply(t, g.gameObject, UIRole.Auto);
                handled.Add(g.gameObject);
            }

            if (tintCameraBackground && Camera.main != null)
            {
                Camera.main.clearFlags = CameraClearFlags.SolidColor;
                Camera.main.backgroundColor = t.background;
            }
        }

        /// <summary>Returns the assigned theme, or a transient palette-only default.</summary>
        public BinanceTheme ResolveTheme()
        {
            if (theme != null) return theme;
            // Colors-only fallback so the controller still works before a theme asset exists.
            return ScriptableObject.CreateInstance<BinanceTheme>();
        }
    }
}
