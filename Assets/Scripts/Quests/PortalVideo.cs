using System;

namespace TalesTensor.Quests
{
    /// <summary>
    /// Render state of the reconstruction video for a <c>portal_video</c> scene, resolved from
    /// the Time Portal Builder (see <see cref="TimePortalClient"/>). Attached to
    /// <see cref="NearbyPortal.video"/>; null for <c>ar_interaction</c> scenes and for scenes
    /// the Time Portal has no workspace for.
    ///
    /// Deliberately a top-level object rather than a field inside <see cref="SceneData"/>:
    /// <c>data</c> is the authored Encounter Card, while this is the asynchronous render
    /// lifecycle. Field names match the <c>video: { status, url, firstFrameUrl }</c> object
    /// the deployed Quest Engine returns per portal from <c>/portals/nearby</c> exactly, so
    /// JsonUtility fills it in directly; the Time Portal lookup then only runs for scenes
    /// the Quest Engine's cache doesn't (yet) have a ready video for.
    /// </summary>
    [Serializable]
    public class PortalVideo
    {
        /// <summary>Time Portal render status — <c>"video_ready"</c> is the only state with a
        /// playable <see cref="url"/>; earlier states (<c>researching</c>,
        /// <c>generating_clips</c>, …) mean it's still being made.</summary>
        public string status;
        /// <summary>Public MP4 URL of the rendered reconstruction, or null/empty.</summary>
        public string url;
        /// <summary>The approved first frame, usable as a poster before the video buffers.</summary>
        public string firstFrameUrl;

        public const string StatusReady = "video_ready";

        /// <summary>A video exists to play. Keyed on <see cref="url"/> alone: the backends only
        /// set it once a render has finished, while <see cref="status"/> can lag or read
        /// <c>cancelled</c> for a portal whose video was already produced (observed on live
        /// data), so the status is informational, not the gate.</summary>
        public bool IsReady => !string.IsNullOrEmpty(url);
    }
}
