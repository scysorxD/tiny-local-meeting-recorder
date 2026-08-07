using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Interfaces;

public interface ISessionRepository
{
    Task SaveAsync(MeetingSession session, CancellationToken ct = default);

    Task<MeetingSession?> LoadAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default);

    Task<IReadOnlyList<MeetingSession>> LoadProcessingAsync(string meetingsRoot, CancellationToken ct = default);

    Task SaveCheckpointAsync(string meetingsRoot, Guid sessionId, TrackTranscriptCheckpoint checkpoint, CancellationToken ct = default);

    Task<TrackTranscriptCheckpoint?> LoadCheckpointAsync(string meetingsRoot, Guid sessionId, string track, CancellationToken ct = default);

    Task DeleteProcessingFolderAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default);
}
