namespace LocalMeetingNotes.Core.Models;

public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);
