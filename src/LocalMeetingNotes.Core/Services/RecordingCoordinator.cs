using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Services;

public sealed class RecordingCoordinator
{
    private readonly IAudioCaptureService _capture;
    private readonly ISessionRepository _repository;
    private readonly ISettingsStore _settingsStore;
    private readonly IModelCatalog _modelCatalog;
    private readonly SessionStateMachine _stateMachine;
    private readonly WavFileValidator _wavValidator;
    private AppSettings? _settingsSnapshot;

    public RecordingCoordinator(
        IAudioCaptureService capture,
        ISessionRepository repository,
        ISettingsStore settingsStore,
        IModelCatalog modelCatalog,
        SessionStateMachine stateMachine,
        WavFileValidator wavValidator)
    {
        _capture = capture;
        _repository = repository;
        _settingsStore = settingsStore;
        _modelCatalog = modelCatalog;
        _stateMachine = stateMachine;
        _wavValidator = wavValidator;
    }

    public MeetingSession? CurrentSession { get; private set; }

    public async Task<MeetingSession> StartAsync(MeetingMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (CurrentSession is not null)
        {
            throw new InvalidOperationException("A recording is already active.");
        }

        _settingsSnapshot = CloneSettings(_settingsStore.Current);
        var meetingsRoot = _settingsSnapshot.MeetingsFolder;

        var session = new MeetingSession
        {
            SessionId = Guid.NewGuid(),
            Metadata = metadata,
            Status = SessionStatus.Draft,
            MeetingsRoot = meetingsRoot,
            Language = _settingsSnapshot.Language,
            MicrophoneDeviceId = _settingsSnapshot.Microphone.DeviceId,
            SystemOutputDeviceId = _settingsSnapshot.SystemOutput.DeviceId,
        };

        session.MicWavPath = SessionPaths.MicWav(meetingsRoot, session.SessionId);
        session.SystemWavPath = SessionPaths.SystemWav(meetingsRoot, session.SessionId);

        _stateMachine.Transition(session, SessionStatus.Recording);
        session.StartedAt = DateTimeOffset.UtcNow;

        var request = new RecordingRequest(
            ResolveMicrophoneDeviceId(_settingsSnapshot),
            ResolveRenderDeviceId(_settingsSnapshot),
            session.MicWavPath,
            session.SystemWavPath);

        try
        {
            await _capture.StartAsync(request, ct);
        }
        catch (Exception exception)
        {
            _stateMachine.Transition(session, SessionStatus.Failed);
            session.ErrorCategory = ErrorCategory.RecordingStartFailure;
            session.Error = exception.Message;
            await _repository.SaveAsync(session, ct);
            _settingsSnapshot = null;
            throw;
        }

        await _repository.SaveAsync(session, ct);
        CurrentSession = session;
        return session;
    }

    public async Task<MeetingSession> StopAsync(CancellationToken ct = default)
    {
        if (CurrentSession is null || CurrentSession.Status != SessionStatus.Recording)
        {
            throw new InvalidOperationException("No active recording to stop.");
        }

        var session = CurrentSession;
        var settings = _settingsSnapshot ?? _settingsStore.Current;

        _stateMachine.Transition(session, SessionStatus.Stopping);
        await _repository.SaveAsync(session, ct);

        RecordingResult result;
        try
        {
            result = await _capture.StopAsync(ct);
        }
        catch (Exception exception)
        {
            _stateMachine.Transition(session, SessionStatus.Failed);
            session.ErrorCategory = ErrorCategory.RecordingStopFailure;
            session.Error = exception.Message;
            session.StoppedAt = DateTimeOffset.UtcNow;
            await _repository.SaveAsync(session, ct);
            CurrentSession = null;
            _settingsSnapshot = null;
            throw;
        }

        session.MicCaptureStartOffsetMs = (long)result.MicrophoneStartOffset.TotalMilliseconds;
        session.SystemCaptureStartOffsetMs = (long)result.SystemStartOffset.TotalMilliseconds;
        session.StoppedAt = DateTimeOffset.UtcNow;

        var validationError = ValidateCapturedTracks(result);
        if (validationError is not null)
        {
            _stateMachine.Transition(session, SessionStatus.Failed);
            session.ErrorCategory = ErrorCategory.AudioFileInvalid;
            session.Error = validationError;
            await _repository.SaveAsync(session, ct);
            CurrentSession = null;
            _settingsSnapshot = null;
            return session;
        }

        var modelPath = _modelCatalog.ResolveSelectedModelPath(settings);
        if (modelPath is not null)
        {
            session.ModelPath = modelPath;
            session.ModelFileName = Path.GetFileName(modelPath);
            _stateMachine.Transition(session, SessionStatus.Queued);
        }
        else
        {
            _stateMachine.Transition(session, SessionStatus.WaitingForModel);
        }

        await _repository.SaveAsync(session, ct);
        CurrentSession = null;
        _settingsSnapshot = null;
        return session;
    }

    private string? ValidateCapturedTracks(RecordingResult result)
    {
        if (result.MicrophoneCaptured)
        {
            var micValidation = _wavValidator.Validate(result.MicrophoneWavPath);
            if (!micValidation.IsValid)
            {
                return $"Microphone WAV invalid: {micValidation.Message}";
            }
        }

        if (result.SystemCaptured)
        {
            var systemValidation = _wavValidator.Validate(result.SystemWavPath);
            if (!systemValidation.IsValid)
            {
                return $"System WAV invalid: {systemValidation.Message}";
            }
        }

        return null;
    }

    private static string? ResolveMicrophoneDeviceId(AppSettings settings) =>
        settings.Microphone.Mode == SettingsDefaults.MicrophoneModeDevice
            ? settings.Microphone.DeviceId
            : null;

    private static string? ResolveRenderDeviceId(AppSettings settings) =>
        settings.SystemOutput.Mode == SettingsDefaults.SystemOutputModeDevice
            ? settings.SystemOutput.DeviceId
            : null;

    private static AppSettings CloneSettings(AppSettings settings) =>
        new()
        {
            MeetingsFolder = settings.MeetingsFolder,
            ModelsFolder = settings.ModelsFolder,
            SelectedModel = settings.SelectedModel,
            Language = settings.Language,
            TranscriptionThreads = settings.TranscriptionThreads,
            Microphone = new DeviceSelection
            {
                Mode = settings.Microphone.Mode,
                DeviceId = settings.Microphone.DeviceId,
            },
            SystemOutput = new DeviceSelection
            {
                Mode = settings.SystemOutput.Mode,
                DeviceId = settings.SystemOutput.DeviceId,
            },
            DeleteAudioAfterSuccess = settings.DeleteAudioAfterSuccess,
            StartMinimized = settings.StartMinimized,
            CloseToTray = settings.CloseToTray,
        };
}
