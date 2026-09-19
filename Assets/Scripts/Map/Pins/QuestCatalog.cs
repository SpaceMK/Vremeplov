using UnityEngine;

namespace TalesTensor.Map
{
    /// <summary>
    /// The placeholder Scottish-history quest names used for AR pins. Shared so the map
    /// (pin generation) and the AR scene (e.g. picking a different quest to deliver a
    /// letter to) draw from the same list.
    /// </summary>
    public static class QuestCatalog
    {
        public static readonly string[] Names =
        {
            "The Wallace Uprising", "Bannockburn's Dawn", "The Stone of Destiny",
            "Mary's Last Letter", "The Jacobite Cipher", "Secrets of Holyrood",
        };

        /// <summary>A random quest name that isn't <paramref name="current"/>, or null if
        /// there are none. Falls back to any differing name, then the first.</summary>
        public static string RandomOther(string current)
        {
            if (Names.Length == 0) return null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string pick = Names[Random.Range(0, Names.Length)];
                if (pick != current) return pick;
            }
            foreach (var n in Names) if (n != current) return n;
            return Names[0];
        }
    }
}
