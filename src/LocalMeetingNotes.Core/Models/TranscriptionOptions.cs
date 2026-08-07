namespace LocalMeetingNotes.Core.Models;

public sealed record TranscriptionOptions(
    string ModelPath,
    string Language = "auto",
    int ThreadCount = 4);
