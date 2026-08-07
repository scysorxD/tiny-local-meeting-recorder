using FluentAssertions;
using LocalMeetingNotes.App.Transcription;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.IntegrationTests.Transcription;

public class WhisperTranscriptionEngineTests
{
    [WhisperModelFact]
    public async Task TranscribeAsync_transcribes_a_wav_when_a_local_model_is_configured()
    {
        var modelPath = Environment.GetEnvironmentVariable("LOCALMEETINGNOTES_WHISPER_MODEL")!;

        var wavPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        try
        {
            await File.WriteAllBytesAsync(wavPath, CreateSilentWave(seconds: 1));
            var engine = new WhisperTranscriptionEngine();

            var transcript = await engine.TranscribeAsync(
                wavPath,
                new TranscriptionOptions(modelPath, "en", 1),
                progress: null,
                CancellationToken.None);

            transcript.Segments.Should().OnlyContain(segment => !string.IsNullOrWhiteSpace(segment.Text));
        }
        finally
        {
            File.Delete(wavPath);
        }
    }

    private static byte[] CreateSilentWave(int seconds)
    {
        const int sampleRate = 16_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var dataLength = sampleRate * seconds * channels * (bitsPerSample / 8);
        var wave = new byte[44 + dataLength];

        using var writer = new BinaryWriter(new MemoryStream(wave));
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataLength);

        return wave;
    }
}

public sealed class WhisperModelFactAttribute : FactAttribute
{
    public WhisperModelFactAttribute()
    {
        var modelPath = Environment.GetEnvironmentVariable("LOCALMEETINGNOTES_WHISPER_MODEL");
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath) ||
            !string.Equals(Path.GetExtension(modelPath), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set LOCALMEETINGNOTES_WHISPER_MODEL to an existing Whisper .bin model to run this test.";
        }
    }
}
