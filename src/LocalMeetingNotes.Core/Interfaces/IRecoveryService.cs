using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Interfaces;

public interface IRecoveryService
{
    Task<RecoveryResult> RecoverAsync(CancellationToken ct = default);
}
