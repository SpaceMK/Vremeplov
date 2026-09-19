using System.Collections.Generic;

namespace TalesTensor.Map.Mvt
{
    public enum GeomType { Unknown = 0, Point = 1, LineString = 2, Polygon = 3 }

    /// <summary>One decoded feature: its geometry command stream and attribute tags.</summary>
    public class MvtFeature
    {
        public GeomType Type;
        public readonly List<uint> Geometry = new();
        public readonly List<int> Tags = new(); // alternating key/value indices into the layer
    }

    /// <summary>One decoded layer: features plus the shared key/value tables.</summary>
    public class MvtLayer
    {
        public string Name;
        public uint Extent = 4096;
        public readonly List<string> Keys = new();
        public readonly List<object> Values = new(); // string / double / bool
        public readonly List<MvtFeature> Features = new();

        /// <summary>Look up an attribute on a feature by key name, or null if absent.</summary>
        public object GetAttribute(MvtFeature f, string key)
        {
            for (int i = 0; i + 1 < f.Tags.Count; i += 2)
            {
                int k = f.Tags[i];
                if (k >= 0 && k < Keys.Count && Keys[k] == key)
                {
                    int v = f.Tags[i + 1];
                    return v >= 0 && v < Values.Count ? Values[v] : null;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// Decodes a Mapbox Vector Tile (MVT) protobuf into layers and features.
    /// See https://github.com/mapbox/vector-tile-spec for the schema.
    /// </summary>
    public static class MvtTile
    {
        public static List<MvtLayer> Decode(byte[] data)
        {
            var layers = new List<MvtLayer>();
            var pbf = new PbfReader(data);
            int field;
            while ((field = pbf.ReadTag(out int wt)) != -1)
            {
                if (field == 3 && wt == 2) layers.Add(DecodeLayer(pbf.ReadMessage()));
                else pbf.Skip(wt);
            }
            return layers;
        }

        static MvtLayer DecodeLayer(PbfReader pbf)
        {
            var layer = new MvtLayer();
            int field;
            while ((field = pbf.ReadTag(out int wt)) != -1)
            {
                switch (field)
                {
                    case 1: layer.Name = pbf.ReadString(); break;
                    case 2: layer.Features.Add(DecodeFeature(pbf.ReadMessage())); break;
                    case 3: layer.Keys.Add(pbf.ReadString()); break;
                    case 4: layer.Values.Add(DecodeValue(pbf.ReadMessage())); break;
                    case 5: layer.Extent = (uint)pbf.ReadVarint(); break;
                    default: pbf.Skip(wt); break; // version (15) and anything else
                }
            }
            return layer;
        }

        static MvtFeature DecodeFeature(PbfReader pbf)
        {
            var f = new MvtFeature();
            int field;
            while ((field = pbf.ReadTag(out int wt)) != -1)
            {
                switch (field)
                {
                    case 2:
                        var tmp = new List<uint>();
                        pbf.ReadPackedUInt32(tmp);
                        foreach (uint t in tmp) f.Tags.Add((int)t);
                        break;
                    case 3: f.Type = (GeomType)pbf.ReadVarint(); break;
                    case 4: pbf.ReadPackedUInt32(f.Geometry); break;
                    default: pbf.Skip(wt); break; // id (1) and anything else
                }
            }
            return f;
        }

        static object DecodeValue(PbfReader pbf)
        {
            object val = null;
            int field;
            while ((field = pbf.ReadTag(out int wt)) != -1)
            {
                switch (field)
                {
                    case 1: val = pbf.ReadString(); break;
                    case 2: val = (double)pbf.ReadFloat(); break;
                    case 3: val = pbf.ReadDouble(); break;
                    case 4: val = (double)(long)pbf.ReadVarint(); break; // int64
                    case 5: val = (double)pbf.ReadVarint(); break;       // uint64
                    case 6: val = (double)pbf.ReadSInt64(); break;       // sint64
                    case 7: val = pbf.ReadBool(); break;
                    default: pbf.Skip(wt); break;
                }
            }
            return val;
        }
    }
}
