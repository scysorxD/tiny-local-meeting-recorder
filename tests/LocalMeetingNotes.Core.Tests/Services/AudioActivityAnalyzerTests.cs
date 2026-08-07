using FluentAssertions;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public class AudioActivityAnalyzerTests : IDisposable
{
    private readonly string _tempRoot;

    public AudioActivityAnalyzerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void HasSignificantActivity_all_zero_pcm_is_false()
    {
        var pcm = new byte[3200];
        var format = new WaveFormatInfo(SampleRate: 16000, Channels: 1, BitsPerSample: 16);

        AudioActivityAnalyzer.HasSignificantActivity(pcm, format).Should().BeFalse();
    }

    [Fact]
    public void HasSignificantActivity_loud_synthetic_pcm_is_true()
    {
        var pcm = CreateSineWavePcm16(amplitude: 20000, sampleCount: 1600);
        var format = new WaveFormatInfo(SampleRate: 16000, Channels: 1, BitsPerSample: 16);

        AudioActivityAnalyzer.HasSignificantActivity(pcm, format).Should().BeTrue();
    }

    [Fact]
    public void HasSignificantActivity_low_noise_pcm_is_false()
    {
        var pcm = CreateSineWavePcm16(amplitude: 50, sampleCount: 1600);
        var format = new WaveFormatInfo(SampleRate: 16000, Channels: 1, BitsPerSample: 16);

        AudioActivityAnalyzer.HasSignificantActivity(pcm, format).Should().BeFalse();
    }

    [Fact]
    public void HasSignificantActivity_wav_path_silence_is_false()
    {
        var path = Path.Combine(_tempRoot, "silence.wav");
        WriteWav(path, CreateSineWavePcm16(amplitude: 0, sampleCount: 1600));

        AudioActivityAnalyzer.HasSignificantActivity(path).Should().BeFalse();
    }

    [Fact]
    public void HasSignificantActivity_wav_path_loud_signal_is_true()
    {
        var path = Path.Combine(_tempRoot, "loud.wav");
        WriteWav(path, CreateSineWavePcm16(amplitude: 20000, sampleCount: 1600));

        AudioActivityAnalyzer.HasSignificantActivity(path).Should().BeTrue();
    }

    private static byte[] CreateSineWavePcm16(short amplitude, int sampleCount)
    {
        var pcm = new byte[sampleCount * 2];
        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * i / 100));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2, 2), sample);
        }

        return pcm;
    }

    private static void WriteWav(string path, byte[] pcmData, int sampleRate = 16000)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        var dataSize = pcmData.Length;
        var blockAlign = (short)2;
        var byteRate = sampleRate * blockAlign;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(pcmData);
    }
}
