using UnityEngine;

namespace BinanceTheme
{
    /// <summary>
    /// Binance-inspired color palette: bold "Binance Yellow" on a monochrome dark
    /// trading-floor surface stack. These are the publicly observable brand colors
    /// and act as the static fallback when no <see cref="BinanceTheme"/> asset is wired up.
    ///
    /// Source-of-truth tokens are documented in Assets/BinanceTheme/README.md.
    /// </summary>
    public static class BinancePalette
    {
        // Brand accent — "Binance Yellow"
        public static readonly Color Yellow      = Hex("FCD535"); // primary accent / CTA fill
        public static readonly Color YellowDark  = Hex("F0B90B"); // pressed / hover

        // Monochrome surface stack (deepest -> raised)
        public static readonly Color Background   = Hex("0B0E11"); // app canvas
        public static readonly Color Surface      = Hex("181A20"); // panels / sheets
        public static readonly Color SurfaceRaised = Hex("1E2329"); // cards / inputs
        public static readonly Color Line         = Hex("2B3139"); // borders / dividers / secondary button

        // Text
        public static readonly Color TextPrimary   = Hex("EAECEF"); // headings & body
        public static readonly Color TextSecondary = Hex("848E9C"); // muted / captions
        public static readonly Color TextDisabled  = Hex("5E6673");
        public static readonly Color OnYellow      = Hex("0B0E11"); // text/icon on a yellow fill

        // Semantic (market) colors
        public static readonly Color Positive = Hex("0ECB81"); // buy / up
        public static readonly Color Negative = Hex("F6465D"); // sell / down

        /// <summary>Parse a "RRGGBB" or "RRGGBBAA" hex string into a Color.</summary>
        public static Color Hex(string hex)
        {
            hex = hex.Trim().TrimStart('#');
            byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            byte a = hex.Length >= 8 ? System.Convert.ToByte(hex.Substring(6, 2), 16) : (byte)255;
            return new Color32(r, g, b, a);
        }
    }
}
