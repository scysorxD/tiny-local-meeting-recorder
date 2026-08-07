using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Files;

public static class WavPcm16Reader
{
    public static (WaveFormatInfo Format, byte[] Samples) ReadMono(string wavPath)
    {
        using var stream = File.OpenRead(wavPath);
        using var reader = new BinaryReader(stream);

        if (ReadAscii(reader, 4) != "RIFF")
        {
            throw new InvalidDataException("Missing RIFF header.");
        }

        _ = reader.ReadInt32();
        if (ReadAscii(reader, 4) != "WAVE")
        {
            throw new InvalidDataException("Missing WAVE header.");
        }

        short channels = 0;
        var sampleRate = 0;
        short bitsPerSample = 0;
        byte[]? samples = null;

        while (stream.Position < stream.Length)
        {
            var chunkId = ReadAscii(reader, 4);
            var chunkSize = reader.ReadInt32();

            switch (chunkId)
            {
                case "fmt ":
                    _ = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    _ = reader.ReadInt32();
                    _ = reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();
                    SkipExtraFmtBytes(reader, chunkSize);
                    break;
                case "data":
                    samples = reader.ReadBytes(chunkSize);
                    break;
                default:
                    stream.Seek(chunkSize, SeekOrigin.Current);
                    break;
            }
        }

        if (samples is null)
        {
            throw new InvalidDataException("Missing data chunk.");
        }

        if (channels != 1 || bitsPerSample != 16)
        {
            throw new InvalidDataException("Only PCM16 mono WAV files are supported.");
        }

        return (new WaveFormatInfo(sampleRate, channels, bitsPerSample), samples);
    }

    private static void SkipExtraFmtBytes(BinaryReader reader, int chunkSize)
    {
        const int baseFmtSize = 16;
        if (chunkSize > baseFmtSize)
        {
            reader.ReadBytes(chunkSize - baseFmtSize);
        }
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        return new string(reader.ReadChars(count));
    }
}
