using System;
using TalesTensor.Quests;

namespace TalesTensor.Map
{
    /// <summary>
    /// The data describing one interactive map pin: what it is, where it sits in the
    /// real world, and (for AR pins) its quest name. Either generated locally by
    /// <see cref="MapPinLayer"/> (offline demo pins) or built from a live Quest Engine
    /// portal (<see cref="portal"/> set) fetched via <see cref="NearbyPortalsClient"/>.
    /// </summary>
    [Serializable]
    public class PinDefinition
    {
        public string id;
        public PinType type;
        public LatLon location;
        /// <summary>Only meaningful for <see cref="PinType.ArChat"/>.</summary>
        public string questName;
        /// <summary>Human-readable stop name shown on the card and in unlock messages
        /// (e.g. "Stone Bridge"). Optional — falls back to <see cref="questName"/> or
        /// the type blurb when absent.</summary>
        public string displayName;
        /// <summary>If set, this pin is a locked stop in a sequential quest chain: it
        /// can only be entered once the pin with this id has been claimed. The claim
        /// set lives on <see cref="MapPinStore"/> and persists across the map ↔ AR
        /// scene transition for the app session.</summary>
        public string unlocksAfterId;
        /// <summary>Display name of the prerequisite stop, so the "Complete X first"
        /// message and the locked card can name it without a cross-pin lookup.</summary>
        public string unlocksAfterName;
        /// <summary>1-based position in a sequential chain (0 if this pin isn't part of
        /// one). Paired with <see cref="chainLength"/> to render "Stop N of M" on the
        /// card so the player can see where they are in the walk.</summary>
        public int chainIndex;
        /// <summary>Total stops in the chain this pin belongs to (0 if not in one).</summary>
        public int chainLength;

        /// <summary>True when this pin advertises a "Stop N of M" position.</summary>
        public bool IsInChain => chainLength > 0 && chainIndex > 0;

        /// <summary>2–3 sentence historical blurb rendered on the card underneath the
        /// stop name. Optional — cards fall back to the type blurb when absent.</summary>
        public string narrative;

        /// <summary>Pre-computed "where to go next" line for the card (e.g. "Next: NAMA
        /// Department Store · ~55 m N" or "Final stop of the walk."). Set at generation
        /// time so the pin doesn't need to peek at other pins to render its card.</summary>
        public string nextHint;

        /// <summary>Portal video for this stop when no live Time Portal render is
        /// available, as a path under StreamingAssets (e.g. "Portals/Palata.mp4").
        /// Optional — Portal pins without one play the shared default offline video.</summary>
        public string offlineVideo;
        /// <summary>Availability window as minutes-from-midnight; only meaningful for
        /// <see cref="PinType.TimedEvent"/>. A zero-length window means "no window".</summary>
        public int windowStartMinutes;
        public int windowEndMinutes;
        /// <summary>Energy spent to claim/enter this pin. Item pins are cheap (1-5).</summary>
        public int energyCost;
        /// <summary>The live Quest Engine scene this pin represents, or null for a locally
        /// generated demo pin. When set, entering the pin uses this real scene's data
        /// (character to converse with, or reconstruction to show) instead of demo content.</summary>
        public NearbyPortal portal;

        /// <summary>True when this pin is backed by a live Quest Engine portal.</summary>
        public bool IsLive => portal != null;

        /// <summary>True when this pin is chained behind an as-yet-unclaimed prerequisite.
        /// A pin with no <see cref="unlocksAfterId"/> is always unlocked.</summary>
        public bool IsLocked => !string.IsNullOrEmpty(unlocksAfterId)
                                && !MapPinStore.IsClaimed(unlocksAfterId);

        public PinDefinition(string id, PinType type, LatLon location, string questName = null,
            int windowStartMinutes = 0, int windowEndMinutes = 0, int energyCost = 0)
        {
            this.id = id;
            this.type = type;
            this.location = location;
            this.questName = questName;
            this.windowStartMinutes = windowStartMinutes;
            this.windowEndMinutes = windowEndMinutes;
            this.energyCost = energyCost;
        }

        public bool HasWindow => windowEndMinutes > windowStartMinutes;

        /// <summary>Build a pin from a live Quest Engine portal. <c>ar_interaction</c>
        /// scenes become <see cref="PinType.ArChat"/> (a live character conversation);
        /// <c>portal_video</c> scenes become <see cref="PinType.Portal"/> (a reconstruction
        /// gateway). The scene's real GPS point is used for placement.</summary>
        public static PinDefinition FromPortal(NearbyPortal p, int arCost, int portalCost)
        {
            bool ar = p.IsArInteraction;
            var loc = new LatLon(p.location.lat, p.location.lng);
            return new PinDefinition(
                id: $"live_{p.sceneId}",
                type: ar ? PinType.ArChat : PinType.Portal,
                location: loc,
                questName: p.quest != null ? p.quest.name : null,
                energyCost: ar ? arCost : portalCost)
            {
                portal = p,
            };
        }
    }
}
