namespace LocalMeetingNotes.Core.Models;

public sealed record MergedTranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    Speaker Speaker,
    string Text);
