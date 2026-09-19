using UnityEngine;

namespace TalesTensor.Map
{
    /// <summary>The kind of experience a map pin loads when entered.</summary>
    public enum PinType
    {
        /// <summary>An AR conversation experience; carries a quest name.</summary>
        ArChat,
        /// <summary>A Chrono Booster pickup — claim it for an energy-restoring item.</summary>
        Collect,
        /// <summary>A time-limited event.</summary>
        TimedEvent,
        /// <summary>A premium reward pin (orange): pricey to claim, yields a real-world voucher.</summary>
        Premium,
        /// <summary>A Portal: enter to step into the AR portal experience (a video seen through
        /// an oval gateway) rather than the King conversation.</summary>
        Portal,
    }

    /// <summary>Which generated silhouette a pin type uses for its glyph.</summary>
    public enum PinGlyph { Chat, Gem, Clock, Bolt, Portal }

    /// <summary>
    /// Presentation data per <see cref="PinType"/>: the accent colour that tints the
    /// pin body, the glyph drawn on it, and the words shown on the expanded card.
    /// Kept in one place so colours/labels stay consistent across pins and cards.
    /// </summary>
    public readonly struct PinTypeInfo
    {
        public readonly string Label;
        public readonly Color Accent;
        public readonly PinGlyph Glyph;
        public readonly string Blurb;

        PinTypeInfo(string label, Color accent, PinGlyph glyph, string blurb)
        {
            Label = label;
            Accent = accent;
            Glyph = glyph;
            Blurb = blurb;
        }

        public static PinTypeInfo For(PinType type) => type switch
        {
            PinType.ArChat => new PinTypeInfo(
                "AR Experience", new Color32(0x3D, 0x9B, 0xF0, 0xFF), PinGlyph.Chat,
                "An augmented-reality conversation awaits."),
            PinType.Collect => new PinTypeInfo(
                "Chrono Booster", new Color32(0xFC, 0xD5, 0x35, 0xFF), PinGlyph.Bolt,
                "A Chrono Booster — claim it, then use it to restore energy."),
            PinType.TimedEvent => new PinTypeInfo(
                "Timed Event", new Color32(0xF6, 0x46, 0x5D, 0xFF), PinGlyph.Clock,
                "Collect it during its window for 15 energy or a premium ice-cream voucher."),
            PinType.Premium => new PinTypeInfo(
                "Premium Reward", new Color32(0xF7, 0x93, 0x1E, 0xFF), PinGlyph.Gem,
                "A real-world reward: 20% off a drink at Awesome Coffee Shop."),
            PinType.Portal => new PinTypeInfo(
                "Portal", new Color32(0x9B, 0x5D, 0xE5, 0xFF), PinGlyph.Portal,
                "Step through the portal into another world."),
            _ => new PinTypeInfo("Experience", Color.white, PinGlyph.Gem, ""),
        };

        public Sprite GlyphSprite() => Glyph switch
        {
            PinGlyph.Chat => ProceduralIcons.ChatBubble(),
            PinGlyph.Gem => ProceduralIcons.Gem(),
            PinGlyph.Clock => ProceduralIcons.Clock(),
            PinGlyph.Bolt => ProceduralIcons.Lightning(),
            PinGlyph.Portal => ProceduralIcons.Ring(),
            _ => ProceduralIcons.Gem(),
        };
    }
}
