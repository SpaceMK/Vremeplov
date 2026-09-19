using UnityEngine;

namespace BinanceTheme
{
    /// <summary>
    /// Tags a single UI element with an explicit <see cref="UIRole"/> so the theme
    /// styles it precisely (e.g. mark the main CTA as <see cref="UIRole.PrimaryButton"/>).
    /// Add it where auto-inference isn't enough; everything else can be left to the
    /// <see cref="BinanceThemeController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class ThemedElement : MonoBehaviour
    {
        public UIRole role = UIRole.Auto;

        [Tooltip("Optional explicit theme. If empty, the parent BinanceThemeController's theme is used.")]
        public BinanceTheme themeOverride;

        public void Apply(BinanceTheme fallbackTheme)
        {
            ThemeApplier.Apply(themeOverride != null ? themeOverride : fallbackTheme, gameObject, role);
        }
    }
}
