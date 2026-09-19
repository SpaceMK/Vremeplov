using System;

namespace TalesTensor.Quests
{
    /// <summary>
    /// Typed models of the Time Portal Builder's quest-workspace responses, as consumed by
    /// <see cref="TimePortalClient"/>:
    /// <list type="bullet">
    ///   <item><c>GET /api/quests</c> → <see cref="TimePortalQuestList"/></item>
    ///   <item><c>GET /api/quests/{id}</c> → <see cref="TimePortalQuest"/> (with <c>stops</c>)</item>
    /// </list>
    /// Only the fields the client needs are declared — Unity's <c>JsonUtility</c> ignores
    /// the rest (research text, prompts, style bundles). Field names are snake_case to match
    /// the JSON exactly, since JsonUtility has no name mapping.
    ///
    /// The join back to the Quest Engine: <see cref="TimePortalQuest.quest_engine_id"/> is the
    /// Quest Engine quest or proposal id the workspace was created for, and
    /// <see cref="TimePortalStop.quest_engine_stop_id"/> is the Quest Engine <c>sceneId</c>.
    /// </summary>
    [Serializable]
    public class TimePortalQuestList
    {
        public TimePortalQuest[] quests;
    }

    [Serializable]
    public class TimePortalQuest
    {
        public string id;
        public string quest_engine_id;
        public string title;
        public string status;            // "active" | "complete"
        public int total_stops;          // only on the list endpoint
        public int completed_stops;      // only on the list endpoint
        public TimePortalStop[] stops;   // only on the detail endpoint
    }

    [Serializable]
    public class TimePortalStop
    {
        public string id;
        public string quest_engine_stop_id;
        public int stop_index;
        public string location;
        // The stop's real-world coordinates, copied verbatim from the Quest Engine on
        // handoff — the Time Portal never re-geocodes them, so they match the anchor point
        // the scene was designed for.
        public double lat;
        public double lng;
        public string status;            // "pending" | "in_progress" | "video_ready"
        public string portal_id;
        public TimePortalPortal portal;  // null until the stop has been started
    }

    /// <summary>The Time Portal's own <c>portals</c> row. Serves two shapes: nested inside a
    /// stop (from <c>GET /api/quests/{id}</c>) where only the render state matters, and at the
    /// top level (from <c>GET /api/portals</c>) where <c>lat</c>/<c>lng</c>/<c>location</c>/<c>era</c>
    /// carry the anchor. <c>quest_stop_id</c> distinguishes a quest-stop portal (its stop
    /// already surfaces it) from a standalone one (created directly in the creator tool,
    /// surfaced by <see cref="TimePortalClient.FetchStandalonePortals"/>). <c>video_url</c>
    /// is the public R2 MP4 and only exists once a render has completed.</summary>
    [Serializable]
    public class TimePortalPortal
    {
        public string id;
        public string status;
        public string video_url;
        public string approved_image_url; // the creator-approved first frame (poster)
        public string streetview_url;
        // Populated by /api/portals; absent (default) when nested inside a stop payload.
        public double lat;
        public double lng;
        public string location;
        public string era;
        public string quest_id;
        public string quest_stop_id;
    }

    /// <summary>Wrapper for <c>GET /api/portals</c>: every portal the Time Portal has, quest
    /// stop ones included. Filter on <see cref="TimePortalPortal.quest_stop_id"/> to keep
    /// only standalones (a quest-stop portal is already surfaced via its stop).</summary>
    [Serializable]
    public class TimePortalPortalList
    {
        public TimePortalPortal[] portals;
    }
}
