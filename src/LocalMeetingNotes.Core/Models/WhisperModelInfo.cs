namespace LocalMeetingNotes.Core.Models;

public sealed record WhisperModelInfo(
    string FileName,
    string FullPath,
    long SizeBytes,
    bool IsValid);
