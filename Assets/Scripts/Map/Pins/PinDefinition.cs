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
