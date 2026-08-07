namespace LocalMeetingNotes.Core.Models;

public sealed record TrackTranscript(IReadOnlyList<TranscriptSegment> Segments);
