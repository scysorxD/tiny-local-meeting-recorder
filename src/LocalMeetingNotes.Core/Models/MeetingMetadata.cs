namespace LocalMeetingNotes.Core.Models;

public sealed record MeetingMetadata(
    string Title,
    IReadOnlyList<string>? Participants = null,
    string? Context = null,
    IReadOnlyList<string>? References = null);
