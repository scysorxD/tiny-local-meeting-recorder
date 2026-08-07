namespace LocalMeetingNotes.Core.Models;

public sealed record RecoveredSession(
    MeetingSession Session,
    RecoveryAction Action,
    bool ShouldRequeue,
    bool OffersRecoveredAudioTranscription = false);
