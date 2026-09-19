using System;
using System.Collections.Generic;

namespace TalesTensor.Map
{
    /// <summary>
    /// The current set of interactive map pins for the local player. Held only for the
    /// running app session (see <see cref="MapPinStore"/>): the points survive leaving
    /// the map (into menus or other scenes) so re-entering restores the same set, but a
    /// fresh app boot scatters a new batch. Pins carry their real-world
    /// <see cref="LatLon"/> coordinates, so they reload at the right place through
    /// <see cref="MapController.GeoToUnity"/> wherever the next GPS fix lands.
    /// </summary>
    [Serializable]
    public class MapPinSave
    {
        /// <summary>True once a set has been generated. Distinguishes a first-ever
        /// visit (generate) from a fully-claimed map (stay empty — don't regenerate).</summary>
        public bool generated;

        /// <summary>The live, not-yet-claimed pins. Claiming a pin removes it here so
        /// it never respawns.</summary>
        public List<PinDefinition> pins = new List<PinDefinition>();
    }

    /// <summary>
    /// Session-only store for the map's pins. Kept in memory for the lifetime of the
    /// running app process, so the set persists across scene loads but is gone on a
    /// fresh boot — points reset every launch and only carry over within the same
    /// session. (Deliberately NOT PlayerPrefs, which would survive app restarts.)
    /// </summary>
    public static class MapPinStore
    {
        static MapPinSave _save;

        /// <summary>True once a pin set has been generated this session.</summary>
        public static bool Exists() => _save != null;

        /// <summary>The session's set, or null if nothing has been generated yet.</summary>
        public static MapPinSave Load() => _save;

        public static void Save(MapPinSave save)
        {
            if (save != null) _save = save;
        }

        public static void Clear() => _save = null;
    }
}
