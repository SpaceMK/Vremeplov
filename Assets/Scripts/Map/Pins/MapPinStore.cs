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
    /// Also holds the set of pin ids claimed this session so sequential quest chains
    /// (see <see cref="PinDefinition.unlocksAfterId"/>) can survive the map ↔ AR
    /// scene transition — the map scene tears down, but the claim record does not.
    /// </summary>
    public static class MapPinStore
    {
        static MapPinSave _save;
        static readonly HashSet<string> _claimed = new HashSet<string>();

        /// <summary>True once a pin set has been generated this session.</summary>
        public static bool Exists() => _save != null;

        /// <summary>The session's set, or null if nothing has been generated yet.</summary>
        public static MapPinSave Load() => _save;

        public static void Save(MapPinSave save)
        {
            if (save != null) _save = save;
        }

        /// <summary>Record that the pin with this id has been claimed this session, so
        /// any chained pins that depend on it become unlocked.</summary>
        public static void MarkClaimed(string id)
        {
            if (!string.IsNullOrEmpty(id)) _claimed.Add(id);
        }

        /// <summary>True if the pin with this id has been claimed this session.</summary>
        public static bool IsClaimed(string id)
        {
            return !string.IsNullOrEmpty(id) && _claimed.Contains(id);
        }

        /// <summary>Drop every remembered claim (so any chained quest that gets
        /// regenerated starts fresh). Does not touch the pin set itself.</summary>
        public static void ClearClaimed() => _claimed.Clear();

        public static void Clear()
        {
            _save = null;
            _claimed.Clear();
        }
    }
}
