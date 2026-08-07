using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Services;

public sealed class MeetingNoteWriter : IMeetingNoteWriter
{
    public async Task<string> WriteAsync(
        MeetingSession session,
        IReadOnlyList<MergedTranscriptSegment> segments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(segments);

        if (session.StartedAt is not { } startedAt)
        {
            throw new InvalidOperationException("Meeting session must have StartedAt before writing a note.");
        }

        var content = MarkdownNoteBuilder.Build(session, segments);
        var fileName = ResolveFileName(session, startedAt);
        var destinationPath = Path.Combine(session.MeetingsRoot, fileName);

        await AtomicFile.WriteUtf8Async(destinationPath, content, cancellationToken);
        return destinationPath;
    }

    private static string ResolveFileName(MeetingSession session, DateTimeOffset startedAt)
    {
        var baseFileName = FilenameSanitizer.BuildNoteFileName(startedAt, session.Metadata.Title);
        var basePath = Path.Combine(session.MeetingsRoot, baseFileName);

        if (!File.Exists(basePath))
        {
            return baseFileName;
        }

        var disambiguator = session.SessionId.ToString("N")[..8];
        var disambiguatedFileName = FilenameSanitizer.BuildNoteFileName(
            startedAt,
            session.Metadata.Title,
            disambiguator);
        var disambiguatedPath = Path.Combine(session.MeetingsRoot, disambiguatedFileName);

        if (File.Exists(disambiguatedPath))
        {
            throw new IOException($"Meeting note file already exists: '{disambiguatedPath}'.");
        }

        return disambiguatedFileName;
    }
}
