using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Services;

public static class AudioActivityAnalyzer
{
    private const double RmsThreshold = 300.0;
    private const short PeakThreshold = 800;

    public static bool HasSignificantActivity(ReadOnlySpan<byte> pcm16Mono, WaveFormatInfo format)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (format.Channels != 1 || format.BitsPerSample != 16)
        {
            throw new ArgumentException("Only PCM16 mono audio is supported.", nameof(format));
        }

        if (pcm16Mono.Length < 2)
        {
            return false;
        }

        var sampleCount = pcm16Mono.Length / 2;
        double sumSquares = 0;
        var peak = 0;

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(pcm16Mono.Slice(i * 2, 2));
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }

            sumSquares += (double)sample * sample;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        return peak >= PeakThreshold || rms >= RmsThreshold;
    }

    public static bool HasSignificantActivity(string wavPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wavPath);

        var (format, samples) = WavPcm16Reader.ReadMono(wavPath);
        return HasSignificantActivity(samples, format);
    }
}
