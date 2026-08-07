namespace LocalMeetingNotes.Core.Interfaces;

using LocalMeetingNotes.Core.Models;

public interface IMeetingNoteWriter
{
    Task<string> WriteAsync(
        MeetingSession session,
        IReadOnlyList<MergedTranscriptSegment> segments,
        CancellationToken cancellationToken = default);
}
