using TMPro;
using UnityEngine;

namespace BinanceTheme
{
    /// <summary>
    /// Designer-editable theme asset. Holds the palette (seeded from
    /// <see cref="BinancePalette"/>) plus the generated TMP font assets per weight.
    /// Create one via <c>Assets ▸ Create ▸ Binance Theme ▸ Theme</c> or the
    /// <c>Tools ▸ Binance Theme</c> menu.
    /// </summary>
    [CreateAssetMenu(fileName = "BinanceTheme", menuName = "Binance Theme/Theme", order = 0)]
    public class BinanceTheme : ScriptableObject
    {
        [Header("Brand accent")]
        public Color yellow      = BinancePalette.Yellow;
        public Color yellowDark  = BinancePalette.YellowDark;

        [Header("Surfaces")]
        public Color background    = BinancePalette.Background;
        public Color surface       = BinancePalette.Surface;
        public Color surfaceRaised = BinancePalette.SurfaceRaised;
        public Color line          = BinancePalette.Line;

        [Header("Text")]
        public Color textPrimary   = BinancePalette.TextPrimary;
        public Color textSecondary = BinancePalette.TextSecondary;
        public Color textDisabled  = BinancePalette.TextDisabled;
        public Color onYellow      = BinancePalette.OnYellow;

        [Header("Semantic")]
        public Color positive = BinancePalette.Positive;
        public Color negative = BinancePalette.Negative;

        [Header("Fonts (TMP SDF assets — generate via Tools ▸ Binance Theme)")]
        [Tooltip("Default body weight. Binance ships 'BinancePlex', which is derived from IBM Plex Sans.")]
        public TMP_FontAsset bodyFont;
        public TMP_FontAsset headingFont; // SemiBold / Bold

        /// <summary>Resolve the foreground color for a text role.</summary>
        public Color TextColor(UIRole role)
        {
            switch (role)
            {
                case UIRole.Muted:    return textSecondary;
                case UIRole.Accent:   return yellow;
                case UIRole.Positive: return positive;
                case UIRole.Negative: return negative;
                default:              return textPrimary; // Heading / Body / Auto
            }
        }
    }
}
