namespace BinanceTheme
{
    /// <summary>
    /// Semantic role assigned to a UI element so the theme knows how to color and
    /// (for text) which font weight to apply. Drives both <see cref="ThemedElement"/>
    /// and the auto-inference in <see cref="BinanceThemeController"/>.
    /// </summary>
    public enum UIRole
    {
        /// <summary>Let the controller infer a sensible role from the component type.</summary>
        Auto,

        // Surfaces (Image / RawImage backgrounds)
        Background,
        Surface,
        SurfaceRaised,
        Divider,

        // Buttons
        PrimaryButton,   // Binance Yellow fill, dark label
        SecondaryButton, // dark line fill, light label

        // Text (TMP_Text / Text)
        Heading,
        Body,
        Muted,
        Accent,

        // Semantic
        Positive,
        Negative,

        /// <summary>Do not touch this element (opt-out).</summary>
        Ignore
    }
}
