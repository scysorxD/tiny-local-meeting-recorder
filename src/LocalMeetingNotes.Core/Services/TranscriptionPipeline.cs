using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Services;

public sealed class TranscriptionPipeline
{
    private readonly ISessionRepository _repository;
    private readonly ITranscriptionEngine _engine;
    private readonly IMeetingNoteWriter _noteWriter;
    private readonly ISettingsStore _settingsStore;
    private readonly IModelCatalog _modelCatalog;
    private readonly SessionStateMachine _stateMachine;

    public TranscriptionPipeline(
        ISessionRepository repository,
        ITranscriptionEngine engine,
        IMeetingNoteWriter noteWriter,
        ISettingsStore settingsStore,
        IModelCatalog modelCatalog,
        SessionStateMachine stateMachine)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _noteWriter = noteWriter ?? throw new ArgumentNullException(nameof(noteWriter));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
    }

    public event EventHandler<SessionProgressEventArgs>? ProgressChanged;

    public async Task ProcessAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");

        try
        {
            if (!HasUsableModel(session))
            {
                await MoveToWaitingForModelAsync(session, ct);
                return;
            }

            var micSegments = await TranscribeTrackAsync(session, "Mic", session.MicWavPath, SessionStatus.TranscribingMic, ct);
            var systemSegments = await TranscribeTrackAsync(session, "System", session.SystemWavPath, SessionStatus.TranscribingSystem, ct);

            await TransitionAndSaveAsync(session, SessionStatus.Merging, ct);
            var merged = TranscriptMerger.Merge(
                micSegments,
                systemSegments,
                TimeSpan.FromMilliseconds(session.MicCaptureStartOffsetMs),
                TimeSpan.FromMilliseconds(session.SystemCaptureStartOffsetMs));

            await TransitionAndSaveAsync(session, SessionStatus.WritingNote, ct);
            await _noteWriter.WriteAsync(session, merged, ct);

            _stateMachine.Transition(session, SessionStatus.Completed);
            Publish(session);

            if (_settingsStore.Current.DeleteAudioAfterSuccess)
            {
                await _repository.DeleteProcessingFolderAsync(session.MeetingsRoot, session.SessionId, ct);
            }
            else
            {
                await _repository.SaveAsync(session, ct);
            }
        }
        catch (OperationCanceledException)
        {
            await MarkInterruptedAsync(session, ct);
            throw;
        }
        catch (Exception exception)
        {
            await MarkFailedAsync(session, exception, ct);
        }
    }

    public async Task<bool> PrepareRetryAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await LoadSessionAsync(sessionId, ct)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var settings = _settingsStore.Current;
        var modelPath = _modelCatalog.ResolveSelectedModelPath(settings);

        if (string.IsNullOrWhiteSpace(modelPath) || !_modelCatalog.ValidateFile(modelPath).IsValid)
        {
            await MoveToWaitingForModelAsync(session, ct);
            return false;
        }

        session.ModelPath = modelPath;
        session.ModelFileName = Path.GetFileName(modelPath);
        session.Language = settings.Language;
        session.Error = null;
        session.ErrorCategory = null;
        session.RetryCount++;

        if (session.Status != SessionStatus.Queued)
        {
            _stateMachine.Transition(session, SessionStatus.Queued);
        }

        await _repository.SaveAsync(session, ct);
        Publish(session);
        return true;
    }

    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeTrackAsync(
        MeetingSession session,
        string track,
        string? wavPath,
        SessionStatus status,
        CancellationToken ct)
    {
        await TransitionAndSaveAsync(session, status, ct);

        var checkpoint = await _repository.LoadCheckpointAsync(session.MeetingsRoot, session.SessionId, track, ct);
        if (IsCompatible(checkpoint, session))
        {
            return checkpoint!.Segments
                .Select(segment => new TranscriptSegment(
                    TimeSpan.FromMilliseconds(segment.StartMs),
                    TimeSpan.FromMilliseconds(segment.EndMs),
                    segment.Text))
                .ToList();
        }

        if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath) || !AudioActivityAnalyzer.HasSignificantActivity(wavPath))
        {
            return [];
        }

        var progress = new Progress<TranscriptionProgress>(value =>
            ProgressChanged?.Invoke(this, new SessionProgressEventArgs(session.SessionId, status, value.Percentage)));
        var transcript = await _engine.TranscribeAsync(
            wavPath,
            new TranscriptionOptions(session.ModelPath!, session.Language, _settingsStore.Current.TranscriptionThreads),
            progress,
            ct);

        var saved = new TrackTranscriptCheckpoint
        {
            Track = track,
            ModelFileName = session.ModelFileName!,
            Language = session.Language,
            AudioDurationMs = transcript.Segments.Count == 0
                ? 0
                : (long)transcript.Segments.Max(segment => segment.End.TotalMilliseconds),
            Segments = transcript.Segments
                .Select(segment => new CheckpointSegment(
                    (long)segment.Start.TotalMilliseconds,
                    (long)segment.End.TotalMilliseconds,
                    segment.Text))
                .ToList(),
        };

        await _repository.SaveCheckpointAsync(session.MeetingsRoot, session.SessionId, saved, ct);
        return transcript.Segments;
    }

    private bool HasUsableModel(MeetingSession session) =>
        !string.IsNullOrWhiteSpace(session.ModelPath) &&
        _modelCatalog.ValidateFile(session.ModelPath).IsValid;

    private static bool IsCompatible(TrackTranscriptCheckpoint? checkpoint, MeetingSession session) =>
        checkpoint is not null &&
        string.Equals(checkpoint.ModelFileName, session.ModelFileName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(checkpoint.Language, session.Language, StringComparison.OrdinalIgnoreCase);

    private async Task<MeetingSession?> LoadSessionAsync(Guid sessionId, CancellationToken ct) =>
        await _repository.LoadAsync(_settingsStore.Current.MeetingsFolder, sessionId, ct);

    private async Task MoveToWaitingForModelAsync(MeetingSession session, CancellationToken ct)
    {
        if (session.Status is SessionStatus.Failed or SessionStatus.Interrupted)
        {
            _stateMachine.Transition(session, SessionStatus.Queued);
        }

        if (session.Status != SessionStatus.WaitingForModel)
        {
            _stateMachine.Transition(session, SessionStatus.WaitingForModel);
        }

        await _repository.SaveAsync(session, ct);
        Publish(session);
    }

    private async Task TransitionAndSaveAsync(MeetingSession session, SessionStatus target, CancellationToken ct)
    {
        _stateMachine.Transition(session, target);
        await _repository.SaveAsync(session, ct);
        Publish(session);
    }

    private async Task MarkInterruptedAsync(MeetingSession session, CancellationToken ct)
    {
        if (session.Status is SessionStatus.TranscribingMic or SessionStatus.TranscribingSystem)
        {
            _stateMachine.Transition(session, SessionStatus.Interrupted);
            await _repository.SaveAsync(session, ct);
            Publish(session);
        }
    }

    private async Task MarkFailedAsync(MeetingSession session, Exception exception, CancellationToken ct)
    {
        if (session.Status != SessionStatus.Failed && session.Status != SessionStatus.Completed)
        {
            var failedDuringNoteWrite = session.Status == SessionStatus.WritingNote;
            _stateMachine.Transition(session, SessionStatus.Failed);
            session.Error = exception.Message;
            session.ErrorCategory = failedDuringNoteWrite
                ? ErrorCategory.MarkdownWriteFailure
                : ErrorCategory.TranscriptionFailure;
            await _repository.SaveAsync(session, ct);
            Publish(session);
        }
    }

    private void Publish(MeetingSession session) =>
        ProgressChanged?.Invoke(this, new SessionProgressEventArgs(session.SessionId, session.Status));
}
