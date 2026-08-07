namespace LocalMeetingNotes.Core.Models;

public sealed class RecordingStartException : Exception
{
    public RecordingStartException(
        string message,
        Exception? microphoneFailure,
        Exception? systemFailure)
        : base(message, microphoneFailure ?? systemFailure)
    {
        MicrophoneFailure = microphoneFailure;
        SystemFailure = systemFailure;
    }

    public Exception? MicrophoneFailure { get; }

    public Exception? SystemFailure { get; }
}
