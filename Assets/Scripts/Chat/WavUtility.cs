using System;
using System.Text;
using UnityEngine;

namespace TalesTensor.Chat
{
    /// <summary>
    /// Decodes raw WAV bytes into an <see cref="AudioClip"/>. Ported from the original
    /// TalesTensor project: the chatbot's TTS streams WAV with a -1 'data' size field,
    /// which Unity's built-in loader (FMOD) refuses, so we parse the PCM ourselves.
    /// </summary>
    public static class WavUtility
    {
        public static AudioClip ToAudioClip(byte[] wav, string clipName = "ChatAudio")
        {
            if (wav == null || wav.Length < 44)
                throw new ArgumentException("WAV byte array is too small to be valid.");

            if (Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE")
                throw new ArgumentException("Not a RIFF/WAVE file.");

            int channels = 0;
            int sampleRate = 0;
            int bitsPerSample = 0;
            int formatCode = 1;
            int dataOffset = -1;
            int dataSize = 0;

            int pos = 12;
            while (pos + 8 <= wav.Length)
            {
                string chunkId = Encoding.ASCII.GetString(wav, pos, 4);
                int chunkSize = BitConverter.ToInt32(wav, pos + 4);
                int chunkBody = pos + 8;

                if (chunkId == "fmt ")
                {
                    formatCode = BitConverter.ToInt16(wav, chunkBody + 0);
                    channels = BitConverter.ToInt16(wav, chunkBody + 2);
                    sampleRate = BitConverter.ToInt32(wav, chunkBody + 4);
                    bitsPerSample = BitConverter.ToInt16(wav, chunkBody + 14);
                }
                else if (chunkId == "data")
                {
                    dataOffset = chunkBody;
                    dataSize = chunkSize;
                    // Some streaming TTS servers emit 0xFFFFFFFF (-1 as int32) for the
                    // 'data' chunk size when they don't know the final length up front.
                    // Fall back to the remaining bytes in the buffer.
                    int remaining = wav.Length - dataOffset;
                    if (dataSize <= 0 || dataSize > remaining) dataSize = remaining;
                    break;
                }

                pos = chunkBody + chunkSize;
                if ((chunkSize & 1) == 1) pos++;
            }

            if (dataOffset < 0)
                throw new ArgumentException("No 'data' chunk found in WAV.");
            if (channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                throw new ArgumentException("Invalid WAV format header.");

            int bytesPerSample = bitsPerSample / 8;
            int totalSamples = dataSize / bytesPerSample;
            float[] floats = new float[totalSamples];

            if (formatCode == 1 && bitsPerSample == 16)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    short s = BitConverter.ToInt16(wav, dataOffset + i * 2);
                    floats[i] = s / 32768f;
                }
            }
            else if (formatCode == 1 && bitsPerSample == 8)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    floats[i] = (wav[dataOffset + i] - 128) / 128f;
                }
            }
            else if (formatCode == 1 && bitsPerSample == 24)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    int b0 = wav[dataOffset + i * 3 + 0];
                    int b1 = wav[dataOffset + i * 3 + 1];
                    int b2 = (sbyte)wav[dataOffset + i * 3 + 2];
                    int sample = (b2 << 16) | (b1 << 8) | b0;
                    floats[i] = sample / 8388608f;
                }
            }
            else if (formatCode == 3 && bitsPerSample == 32)
            {
                for (int i = 0; i < totalSamples; i++)
                    floats[i] = BitConverter.ToSingle(wav, dataOffset + i * 4);
            }
            else
            {
                throw new ArgumentException($"Unsupported WAV format (code {formatCode}, {bitsPerSample}-bit).");
            }

            int lengthSamplesPerChannel = totalSamples / channels;
            AudioClip clip = AudioClip.Create(clipName, lengthSamplesPerChannel, channels, sampleRate, false);
            clip.SetData(floats, 0);
            return clip;
        }
    }
}
