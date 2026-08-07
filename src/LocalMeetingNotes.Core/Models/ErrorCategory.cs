namespace LocalMeetingNotes.Core.Models;

public enum ErrorCategory
{
    ModelMissing,
    ModelInvalid,
    NativeRuntimeLoadFailure,
    RecordingStartFailure,
    RecordingStopFailure,
    AudioFileInvalid,
    TranscriptionFailure,
    OutputFolderPermissionDenied,
    MarkdownWriteFailure,
    DiskSpaceLow,
    Unknown,
}
