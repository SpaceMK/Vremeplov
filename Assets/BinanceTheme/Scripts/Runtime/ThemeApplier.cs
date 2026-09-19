using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BinanceTheme
{
    /// <summary>
    /// Stateless logic that applies a <see cref="BinanceTheme"/> to a single UI
    /// GameObject according to a <see cref="UIRole"/>. Shared by
    /// <see cref="ThemedElement"/> (one element) and
    /// <see cref="BinanceThemeController"/> (a whole canvas).
    /// </summary>
    public static class ThemeApplier
    {
        public static void Apply(BinanceTheme theme, GameObject go, UIRole role)
        {
            if (theme == null || go == null || role == UIRole.Ignore)
                return;

            var selectable = go.GetComponent<Selectable>();
            var tmp = go.GetComponent<TMP_Text>();
            var legacy = go.GetComponent<Text>();
            var image = go.GetComponent<Image>();
            var raw = go.GetComponent<RawImage>();

            if (role == UIRole.Auto)
                role = Infer(selectable, tmp, legacy, image, raw);

            // Buttons (and other Selectables with a target Image) get a full color block.
            if (selectable != null && (role == UIRole.PrimaryButton || role == UIRole.SecondaryButton))
            {
                StyleButton(theme, selectable, role);
                return;
            }

            if (tmp != null)      { StyleTmp(theme, tmp, role); return; }
            if (legacy != null)   { legacy.color = theme.TextColor(role); return; }
            if (image != null)    { image.color = SurfaceColor(theme, role); return; }
            if (raw != null)      { raw.color = SurfaceColor(theme, role); return; }
        }

        static UIRole Infer(Selectable selectable, TMP_Text tmp, Text legacy, Image image, RawImage raw)
        {
            if (selectable is Button) return UIRole.SecondaryButton; // primary must be explicit
            if (tmp != null)
            {
                bool heading = (tmp.fontStyle & FontStyles.Bold) != 0 || tmp.fontSize >= 28f;
                return heading ? UIRole.Heading : UIRole.Body;
            }
            if (legacy != null)
            {
                bool heading = legacy.fontStyle == FontStyle.Bold || legacy.fontSize >= 28;
                return heading ? UIRole.Heading : UIRole.Body;
            }
            if (image != null || raw != null) return UIRole.Surface;
            return UIRole.Ignore;
        }

        static Color SurfaceColor(BinanceTheme theme, UIRole role)
        {
            switch (role)
            {
                case UIRole.Background:    return theme.background;
                case UIRole.Surface:       return theme.surface;
                case UIRole.SurfaceRaised: return theme.surfaceRaised;
                case UIRole.Divider:       return theme.line;
                case UIRole.Accent:        return theme.yellow;
                case UIRole.Positive:      return theme.positive;
                case UIRole.Negative:      return theme.negative;
                default:                   return theme.surface;
            }
        }

        static void StyleTmp(BinanceTheme theme, TMP_Text tmp, UIRole role)
        {
            tmp.color = theme.TextColor(role);

            var font = role == UIRole.Heading ? theme.headingFont : theme.bodyFont;
            if (font == null) font = theme.bodyFont;
            if (font != null) tmp.font = font;
        }

        static void StyleButton(BinanceTheme theme, Selectable selectable, UIRole role)
        {
            bool primary = role == UIRole.PrimaryButton;

            // Color states only render under ColorTint (template buttons may use SpriteSwap).
            selectable.transition = Selectable.Transition.ColorTint;

            // Fill color lives on the target graphic; the ColorBlock multiplies it per-state.
            var target = selectable.targetGraphic;
            if (target != null)
                target.color = primary ? theme.yellow : theme.line;

            var cb = selectable.colors;
            cb.normalColor      = Color.white;
            cb.highlightedColor = primary ? new Color(0.92f, 0.92f, 0.92f) : new Color(1.12f, 1.12f, 1.12f, 1f);
            cb.pressedColor     = primary ? new Color(0.82f, 0.82f, 0.82f) : new Color(0.85f, 0.85f, 0.85f);
            cb.selectedColor    = cb.highlightedColor;
            cb.disabledColor    = new Color(0.45f, 0.45f, 0.45f, 0.5f);
            cb.colorMultiplier  = 1f;
            cb.fadeDuration     = 0.1f;
            selectable.colors = cb;

            // Label color/font: dark on yellow, light otherwise.
            Color labelColor = primary ? theme.onYellow : theme.textPrimary;
            foreach (var t in selectable.GetComponentsInChildren<TMP_Text>(true))
            {
                var te = t.GetComponent<ThemedElement>();
                if (te != null && te.role == UIRole.Ignore) continue; // opt-out (e.g. a brand glyph)
                t.color = labelColor;
                var font = theme.headingFont != null ? theme.headingFont : theme.bodyFont;
                if (font != null) t.font = font;
            }
            foreach (var t in selectable.GetComponentsInChildren<Text>(true))
            {
                var te = t.GetComponent<ThemedElement>();
                if (te != null && te.role == UIRole.Ignore) continue;
                t.color = labelColor;
            }
        }
    }
}
