using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Services;

public sealed class RecoveryService : IRecoveryService
{
    private readonly ISessionRepository _repository;
    private readonly ISettingsStore _settingsStore;
    private readonly SessionStateMachine _stateMachine;
    private readonly WavFileValidator _wavValidator;

    public RecoveryService(
        ISessionRepository repository,
        ISettingsStore settingsStore,
        SessionStateMachine stateMachine,
        WavFileValidator wavValidator)
    {
        _repository = repository;
        _settingsStore = settingsStore;
        _stateMachine = stateMachine;
        _wavValidator = wavValidator;
    }

    public async Task<RecoveryResult> RecoverAsync(CancellationToken ct = default)
    {
        var meetingsRoot = _settingsStore.Current.MeetingsFolder;
        var sessions = await _repository.LoadProcessingAsync(meetingsRoot, ct);
        var recovered = new List<RecoveredSession>(sessions.Count);

        foreach (var session in sessions)
        {
            var result = RecoverSession(session);
            await _repository.SaveAsync(result.Session, ct);
            recovered.Add(result);
        }

        return new RecoveryResult(recovered);
    }

    private RecoveredSession RecoverSession(MeetingSession session)
    {
        return session.Status switch
        {
            SessionStatus.Queued => Surface(session, shouldRequeue: true),
            SessionStatus.WaitingForModel or SessionStatus.Failed => Surface(session, shouldRequeue: false),
            SessionStatus.TranscribingMic or SessionStatus.TranscribingSystem =>
                InterruptForTranscription(session),
            SessionStatus.Merging or SessionStatus.WritingNote =>
                InterruptForTranscription(session, useStateMachine: false),
            SessionStatus.Recording or SessionStatus.Stopping =>
                RecoverRecordingSession(session),
            _ => Surface(session, shouldRequeue: false),
        };
    }

    private static RecoveredSession Surface(MeetingSession session, bool shouldRequeue) =>
        new(session, RecoveryAction.Surfaced, shouldRequeue);

    private RecoveredSession InterruptForTranscription(MeetingSession session, bool useStateMachine = true)
    {
        if (useStateMachine)
        {
            _stateMachine.Transition(session, SessionStatus.Interrupted);
        }
        else
        {
            session.Status = SessionStatus.Interrupted;
        }

        return new(session, RecoveryAction.InterruptedForTranscription, ShouldRequeue: true);
    }

    private RecoveredSession RecoverRecordingSession(MeetingSession session)
    {
        if (!HasRecoverableAudio(session))
        {
            session.ErrorCategory = ErrorCategory.AudioFileInvalid;
            session.Error = "Recovery failed: no valid audio files were found.";
            _stateMachine.Transition(session, SessionStatus.Failed);
            return new(session, RecoveryAction.MissingAudio, ShouldRequeue: false);
        }

        _stateMachine.Transition(session, SessionStatus.Interrupted);
        return new(
            session,
            RecoveryAction.InterruptedForRecording,
            ShouldRequeue: false,
            OffersRecoveredAudioTranscription: true);
    }

    private bool HasRecoverableAudio(MeetingSession session)
    {
        var micValid = session.MicWavPath is not null && _wavValidator.Validate(session.MicWavPath).IsValid;
        var systemValid = session.SystemWavPath is not null && _wavValidator.Validate(session.SystemWavPath).IsValid;
        return micValid || systemValid;
    }
}
