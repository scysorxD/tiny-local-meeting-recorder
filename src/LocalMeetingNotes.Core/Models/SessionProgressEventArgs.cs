namespace LocalMeetingNotes.Core.Models;

public sealed class SessionProgressEventArgs : EventArgs
{
    public SessionProgressEventArgs(Guid sessionId, SessionStatus status, int? percentage = null)
    {
        SessionId = sessionId;
        Status = status;
        Percentage = percentage;
    }

    public Guid SessionId { get; }

    public SessionStatus Status { get; }

    public int? Percentage { get; }
}
