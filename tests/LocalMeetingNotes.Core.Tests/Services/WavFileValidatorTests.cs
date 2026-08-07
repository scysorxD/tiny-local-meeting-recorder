using FluentAssertions;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public class WavFileValidatorTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly WavFileValidator _validator = new();

    public WavFileValidatorTests()
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
    public void Validate_missing_file_is_invalid()
    {
        var result = _validator.Validate(Path.Combine(_tempRoot, "missing.wav"));

        result.IsValid.Should().BeFalse();
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Validate_empty_file_is_invalid()
    {
        var path = Path.Combine(_tempRoot, "empty.wav");
        File.WriteAllBytes(path, []);

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_non_wav_content_is_invalid()
    {
        var path = Path.Combine(_tempRoot, "bad.wav");
        File.WriteAllText(path, "not a wav");

        var result = _validator.Validate(path);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_valid_pcm16_mono_wav_is_valid()
    {
        var path = Path.Combine(_tempRoot, "valid.wav");
        WriteValidWav(path);

        var result = _validator.Validate(path);

        result.IsValid.Should().BeTrue();
    }

    private static void WriteValidWav(string path)
    {
        var pcm = new byte[320];
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16_000);
        writer.Write(16_000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }
}
