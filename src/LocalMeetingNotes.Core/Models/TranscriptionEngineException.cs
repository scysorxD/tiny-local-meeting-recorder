namespace LocalMeetingNotes.Core.Models;

public sealed class TranscriptionEngineException : Exception
{
    public TranscriptionEngineException(
        ErrorCategory errorCategory,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        ErrorCategory = errorCategory;
    }

    public ErrorCategory ErrorCategory { get; }
}
