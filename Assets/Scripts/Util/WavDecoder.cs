using UnityEngine;

namespace OsuUnity.Util
{
    /// <summary>
    /// Minimal RIFF/WAVE PCM decoder that turns raw .wav bytes into an <see cref="AudioClip"/> without
    /// going through Unity's FMOD loader. Unity's <c>DownloadHandlerAudioClip</c> logs an uncatchable
    /// "Unsupported file or audio format" error for some valid skin WAVs (uncommon chunk layouts), so we
    /// parse the chunks ourselves. Handles integer PCM (8/16/24/32-bit) and IEEE float (32-bit).
    /// Returns null when the data isn't WAV we can read; the caller treats that as silence.
    /// </summary>
    public static class WavDecoder
    {
        public static AudioClip Decode(byte[] bytes, string name)
        {
            if (bytes == null || bytes.Length < 44) return null;
            if (bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F') return null;
            if (bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E') return null;

            int audioFormat = 0, channels = 0, sampleRate = 0, bitsPerSample = 0;
            int dataOffset = -1, dataLength = 0;

            // Walk the chunk list after the 12-byte RIFF/WAVE header.
            int pos = 12;
            while (pos + 8 <= bytes.Length)
            {
                string id = new string(new[] { (char)bytes[pos], (char)bytes[pos + 1], (char)bytes[pos + 2], (char)bytes[pos + 3] });
                int size = ReadInt32(bytes, pos + 4);
                int body = pos + 8;
                if (size < 0 || body + size > bytes.Length) size = bytes.Length - body; // tolerate bad sizes

                if (id == "fmt " && size >= 16)
                {
                    audioFormat = ReadInt16(bytes, body);
                    channels = ReadInt16(bytes, body + 2);
                    sampleRate = ReadInt32(bytes, body + 4);
                    bitsPerSample = ReadInt16(bytes, body + 14);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = size;
                }

                pos = body + size + (size & 1); // chunks are word-aligned
            }

            if (dataOffset < 0 || dataLength <= 0 || channels <= 0 || sampleRate <= 0) return null;

            int bytesPerSample = bitsPerSample / 8;
            if (bytesPerSample <= 0) return null;
            int totalSamples = dataLength / bytesPerSample;        // across all channels
            if (totalSamples <= 0) return null;

            var data = new float[totalSamples];
            bool isFloat = audioFormat == 3;

            for (int i = 0; i < totalSamples; i++)
            {
                int o = dataOffset + i * bytesPerSample;
                if (o + bytesPerSample > bytes.Length) { totalSamples = i; break; }
                data[i] = isFloat ? ReadFloatSample(bytes, o, bitsPerSample)
                                  : ReadPcmSample(bytes, o, bitsPerSample);
            }

            if (totalSamples <= 0) return null;
            int frames = totalSamples / channels;
            if (frames <= 0) return null;

            var clip = AudioClip.Create(name, frames, channels, sampleRate, false);
            // SetData wants exactly frames*channels; trim if a partial trailing frame was dropped.
            if (data.Length != frames * channels)
            {
                var trimmed = new float[frames * channels];
                System.Array.Copy(data, trimmed, trimmed.Length);
                data = trimmed;
            }
            clip.SetData(data, 0);
            return clip;
        }

        private static float ReadPcmSample(byte[] b, int o, int bits)
        {
            switch (bits)
            {
                case 8:  return (b[o] - 128) / 128f;                       // 8-bit WAV is unsigned
                case 16: return (short)(b[o] | (b[o + 1] << 8)) / 32768f;
                case 24:
                    int v24 = (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16));
                    if ((v24 & 0x800000) != 0) v24 |= unchecked((int)0xFF000000); // sign-extend
                    return v24 / 8388608f;
                case 32:
                    int v32 = ReadInt32(b, o);
                    return v32 / 2147483648f;
                default: return 0f;
            }
        }

        private static float ReadFloatSample(byte[] b, int o, int bits)
        {
            if (bits == 32) return System.BitConverter.ToSingle(b, o);
            if (bits == 64) return (float)System.BitConverter.ToDouble(b, o);
            return 0f;
        }

        private static int ReadInt16(byte[] b, int o) => b[o] | (b[o + 1] << 8);

        private static int ReadInt32(byte[] b, int o) =>
            b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);
    }
}
