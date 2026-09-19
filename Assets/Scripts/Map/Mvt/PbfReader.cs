using System.Collections.Generic;
using System.Text;

namespace TalesTensor.Map.Mvt
{
    /// <summary>
    /// Minimal protobuf wire-format reader, just enough to decode Mapbox Vector
    /// Tiles. Operates over a slice of a byte buffer so embedded messages can be
    /// read without copying.
    /// </summary>
    public class PbfReader
    {
        readonly byte[] _buf;
        int _pos;
        readonly int _end;

        public PbfReader(byte[] buffer, int start, int length)
        {
            _buf = buffer;
            _pos = start;
            _end = start + length;
        }

        public PbfReader(byte[] buffer) : this(buffer, 0, buffer.Length) { }

        /// <summary>Read the next field tag. Returns the field number, or -1 at end of message.</summary>
        public int ReadTag(out int wireType)
        {
            if (_pos >= _end) { wireType = -1; return -1; }
            ulong key = ReadVarint();
            wireType = (int)(key & 0x7);
            return (int)(key >> 3);
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                byte b = _buf[_pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return result;
        }

        public long ReadSInt64()
        {
            ulong n = ReadVarint();
            return (long)(n >> 1) ^ -(long)(n & 1); // zigzag decode
        }

        public double ReadDouble()
        {
            double v = System.BitConverter.ToDouble(_buf, _pos);
            _pos += 8;
            return v;
        }

        public float ReadFloat()
        {
            float v = System.BitConverter.ToSingle(_buf, _pos);
            _pos += 4;
            return v;
        }

        public bool ReadBool() => ReadVarint() != 0;

        public string ReadString()
        {
            int len = (int)ReadVarint();
            string s = Encoding.UTF8.GetString(_buf, _pos, len);
            _pos += len;
            return s;
        }

        /// <summary>Read a length-delimited sub-message as its own bounded reader.</summary>
        public PbfReader ReadMessage()
        {
            int len = (int)ReadVarint();
            var sub = new PbfReader(_buf, _pos, len);
            _pos += len;
            return sub;
        }

        /// <summary>Read a packed repeated uint32 field, appending into <paramref name="into"/>.</summary>
        public void ReadPackedUInt32(List<uint> into)
        {
            int len = (int)ReadVarint();
            int packEnd = _pos + len;
            while (_pos < packEnd) into.Add((uint)ReadVarint());
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(); break;
                case 1: _pos += 8; break;
                case 2: _pos += (int)ReadVarint(); break;
                case 5: _pos += 4; break;
                default: throw new System.Exception("Unsupported protobuf wire type " + wireType);
            }
        }
    }
}
