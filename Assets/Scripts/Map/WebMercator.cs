using UnityEngine;

namespace TalesTensor.Map
{
    /// <summary>A geographic coordinate in WGS84 degrees.</summary>
    [System.Serializable]
    public struct LatLon
    {
        public double Latitude;
        public double Longitude;

        public LatLon(double latitude, double longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public override string ToString() => $"({Latitude:F6}, {Longitude:F6})";
    }

    /// <summary>
    /// Web Mercator (EPSG:3857) slippy-tile math — the bridge between real-world
    /// lat/lon and the flat Unity map. All map content is positioned in "fractional
    /// tile" space at a fixed zoom, then offset from a chosen origin and scaled into
    /// Unity units (see <see cref="MapController"/>), which lets the player sit near
    /// the world origin while the map streams around them.
    /// </summary>
    public static class WebMercator
    {
        /// <summary>
        /// Fractional tile X/Y for a coordinate at <paramref name="zoom"/>.
        /// Integer part is the tile index; the fraction is the position within it.
        /// </summary>
        public static void LatLonToTile(LatLon c, int zoom, out double tileX, out double tileY)
        {
            double n = 1 << zoom;
            double latRad = c.Latitude * System.Math.PI / 180.0;
            tileX = (c.Longitude + 180.0) / 360.0 * n;
            tileY = (1.0 - System.Math.Log(System.Math.Tan(latRad) + 1.0 / System.Math.Cos(latRad)) / System.Math.PI) / 2.0 * n;
        }

        /// <summary>Inverse of <see cref="LatLonToTile"/>.</summary>
        public static LatLon TileToLatLon(double tileX, double tileY, int zoom)
        {
            double n = 1 << zoom;
            double lon = tileX / n * 360.0 - 180.0;
            double latRad = System.Math.Atan(System.Math.Sinh(System.Math.PI * (1.0 - 2.0 * tileY / n)));
            return new LatLon(latRad * 180.0 / System.Math.PI, lon);
        }

        /// <summary>Ground resolution (metres along one tile edge) at a given latitude and zoom.</summary>
        public static double MetersPerTile(double latitude, int zoom)
        {
            const double earthCircumference = 40075016.686; // metres at the equator
            double n = 1 << zoom;
            return earthCircumference * System.Math.Cos(latitude * System.Math.PI / 180.0) / n;
        }
    }
}
