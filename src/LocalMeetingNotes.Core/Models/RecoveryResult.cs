namespace LocalMeetingNotes.Core.Models;

public sealed record RecoveryResult(IReadOnlyList<RecoveredSession> Sessions);
