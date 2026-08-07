using LocalMeetingNotes.Core.Files;

namespace LocalMeetingNotes.Core.Services;

public sealed class WavFileValidator
{
    public WavValidationResult Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid("WAV path is empty.");
        }

        if (!File.Exists(path))
        {
            return Invalid("WAV file does not exist.");
        }

        if (new FileInfo(path).Length == 0)
        {
            return Invalid("WAV file is empty.");
        }

        try
        {
            WavPcm16Reader.ValidateReadablePcm16Mono(path);
            return new WavValidationResult(true);
        }
        catch (Exception exception) when (exception is InvalidDataException or FileNotFoundException)
        {
            return Invalid(exception.Message);
        }
    }

    private static WavValidationResult Invalid(string message) => new(false, message);
}

public sealed record WavValidationResult(bool IsValid, string? Message = null);
