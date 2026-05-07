using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Minimal WAV encoder/decoder for microphone capture and TTS playback.
/// Supports PCM 16-bit WAV data.
/// </summary>
public static class LabScene2WavUtility
{
    public static byte[] FromAudioClip(AudioClip clip, int sampleFrames = -1)
    {
        if (clip == null)
        {
            return Array.Empty<byte>();
        }

        int frames = sampleFrames > 0 ? Mathf.Min(sampleFrames, clip.samples) : clip.samples;
        float[] samples = new float[frames * clip.channels];
        clip.GetData(samples, 0);

        short[] intData = new short[samples.Length];
        byte[] bytesData = new byte[samples.Length * 2];
        const float rescaleFactor = 32767f;

        for (int index = 0; index < samples.Length; index++)
        {
            intData[index] = (short)Mathf.Clamp(samples[index] * rescaleFactor, short.MinValue, short.MaxValue);
            byte[] byteArr = BitConverter.GetBytes(intData[index]);
            byteArr.CopyTo(bytesData, index * 2);
        }

        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int byteRate = clip.frequency * clip.channels * 2;
            int subChunk2Size = bytesData.Length;

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + subChunk2Size);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(byteRate);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(subChunk2Size);
            writer.Write(bytesData);

            writer.Flush();
            return stream.ToArray();
        }
    }

    public static AudioClip ToAudioClip(byte[] wavData, string clipName)
    {
        if (wavData == null || wavData.Length < 44)
        {
            return null;
        }

        try
        {
            using (MemoryStream stream = new MemoryStream(wavData))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                string riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (riff != "RIFF")
                {
                    return null;
                }

                reader.ReadUInt32();
                string wave = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (wave != "WAVE")
                {
                    return null;
                }

                short channels = 1;
                int sampleRate = 24000;
                short bitsPerSample = 16;
                byte[] audioData = null;

                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    uint declaredChunkSize = reader.ReadUInt32();
                    long readableChunkSize = ResolveReadableChunkSize(reader, declaredChunkSize);

                    if (chunkId == "fmt ")
                    {
                        if (readableChunkSize < 16)
                        {
                            return null;
                        }

                        short audioFormat = reader.ReadInt16();
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt16();
                        bitsPerSample = reader.ReadInt16();

                        int extraFormatBytes = (int)(readableChunkSize - 16);
                        if (extraFormatBytes > 0)
                        {
                            reader.ReadBytes(extraFormatBytes);
                        }

                        if (audioFormat != 1)
                        {
                            return null;
                        }

                        SkipChunkPadding(reader, declaredChunkSize);
                    }
                    else if (chunkId == "data")
                    {
                        if (readableChunkSize <= 0 || readableChunkSize > int.MaxValue)
                        {
                            return null;
                        }

                        audioData = reader.ReadBytes((int)readableChunkSize);
                        break;
                    }
                    else
                    {
                        if (readableChunkSize > int.MaxValue)
                        {
                            return null;
                        }

                        reader.ReadBytes((int)readableChunkSize);
                        SkipChunkPadding(reader, declaredChunkSize);
                    }
                }

                if (audioData == null || audioData.Length == 0 || bitsPerSample != 16 || channels <= 0)
                {
                    return null;
                }

                int totalSamples = audioData.Length / 2;
                float[] floatData = new float[totalSamples];
                for (int i = 0; i < totalSamples; i++)
                {
                    short sample = BitConverter.ToInt16(audioData, i * 2);
                    floatData[i] = sample / 32768f;
                }

                int sampleFrames = totalSamples / channels;
                AudioClip clip = AudioClip.Create(clipName, sampleFrames, channels, sampleRate, false);
                clip.SetData(floatData, 0);
                return clip;
            }
        }
        catch
        {
            return null;
        }
    }

    private static long ResolveReadableChunkSize(BinaryReader reader, uint declaredChunkSize)
    {
        long remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
        if (declaredChunkSize == uint.MaxValue || declaredChunkSize > remainingBytes)
        {
            return remainingBytes;
        }

        return declaredChunkSize;
    }

    private static void SkipChunkPadding(BinaryReader reader, uint declaredChunkSize)
    {
        if ((declaredChunkSize & 1u) == 0u || declaredChunkSize == uint.MaxValue)
        {
            return;
        }

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            reader.ReadByte();
        }
    }
}
