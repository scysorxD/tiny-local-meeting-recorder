using System.Text;

namespace LocalMeetingNotes.Core.Services;

public static class FilenameSanitizer
{
    private const int MaxFileNameLength = 255;
    public const int MaxSanitizedTitleLength = MaxFileNameLength - 23 - 11;

    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static string Sanitize(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return "Untitled Meeting";
        }

        var trimmed = title.Trim();
        var builder = new StringBuilder(trimmed.Length);

        foreach (var ch in trimmed)
        {
            builder.Append(Array.IndexOf(InvalidFileNameChars, ch) >= 0 ? '_' : ch);
        }

        var sanitized = builder.ToString().TrimEnd('.', ' ');

        if (sanitized.Length == 0)
        {
            return "Untitled Meeting";
        }

        if (sanitized.Length > MaxSanitizedTitleLength)
        {
            sanitized = sanitized[..MaxSanitizedTitleLength].TrimEnd('.', ' ');
        }

        return sanitized.Length == 0 ? "Untitled Meeting" : sanitized;
    }

    public static string BuildNoteFileName(DateTimeOffset startedAt, string title, string? disambiguator = null)
    {
        var sanitizedTitle = Sanitize(title);
        var timestamp = startedAt.ToString("yyyy-MM-dd_HHmm");
        var baseName = $"{timestamp} - {sanitizedTitle}";

        if (string.IsNullOrWhiteSpace(disambiguator))
        {
            return $"{baseName}.md";
        }

        return $"{baseName} ({disambiguator}).md";
    }
}
