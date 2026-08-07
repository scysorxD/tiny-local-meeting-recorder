using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Interfaces;

public interface ITranscriptionQueue
{
    void Enqueue(Guid sessionId);

    Task RetryAsync(Guid sessionId, CancellationToken ct = default);

    event EventHandler<SessionProgressEventArgs>? ProgressChanged;
}
