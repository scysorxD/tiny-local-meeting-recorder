namespace LocalMeetingNotes.Core.Models;

public enum SessionStatus
{
    Draft,
    Recording,
    Stopping,
    Queued,
    WaitingForModel,
    TranscribingMic,
    TranscribingSystem,
    Merging,
    WritingNote,
    Completed,
    Failed,
    Interrupted,
}
