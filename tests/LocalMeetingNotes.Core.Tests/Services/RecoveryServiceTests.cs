using FluentAssertions;
using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Tests.Services;

public class RecoveryServiceTests : IDisposable
{
    private readonly string _meetingsRoot;
    private readonly FakeSessionRepository _repository = new();
    private readonly FakeSettingsStore _settingsStore = new();
    private readonly SessionStateMachine _stateMachine = new();
    private readonly WavFileValidator _wavValidator = new();
    private readonly RecoveryService _recoveryService;

    public RecoveryServiceTests()
    {
        _meetingsRoot = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_meetingsRoot);
        _settingsStore.Current = CreateSettings(_meetingsRoot);
        _recoveryService = new RecoveryService(
            _repository,
            _settingsStore,
            _stateMachine,
            _wavValidator);
    }

    public void Dispose()
    {
        if (Directory.Exists(_meetingsRoot))
        {
            Directory.Delete(_meetingsRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(SessionStatus.Queued, true)]
    [InlineData(SessionStatus.WaitingForModel, false)]
    [InlineData(SessionStatus.Failed, false)]
    public async Task RecoverAsync_surfaces_queued_waiting_for_model_and_failed_without_status_change(
        SessionStatus status,
        bool shouldRequeue)
    {
        var session = SeedSession(status);

        var result = await _recoveryService.RecoverAsync();

        result.Sessions.Should().ContainSingle();
        var recovered = result.Sessions[0];
        recovered.Session.SessionId.Should().Be(session.SessionId);
        recovered.Session.Status.Should().Be(status);
        recovered.Action.Should().Be(RecoveryAction.Surfaced);
        recovered.ShouldRequeue.Should().Be(shouldRequeue);
        recovered.OffersRecoveredAudioTranscription.Should().BeFalse();
        _repository.SavedSessions.Should().ContainSingle(s =>
            s.SessionId == session.SessionId && s.Status == status);
    }

    [Theory]
    [InlineData(SessionStatus.TranscribingMic)]
    [InlineData(SessionStatus.TranscribingSystem)]
    public async Task RecoverAsync_transcribing_sessions_mark_interrupted_and_request_requeue(SessionStatus status)
    {
        var session = SeedSession(status);

        var result = await _recoveryService.RecoverAsync();

        result.Sessions.Should().ContainSingle();
        var recovered = result.Sessions[0];
        recovered.Session.Status.Should().Be(SessionStatus.Interrupted);
        recovered.Action.Should().Be(RecoveryAction.InterruptedForTranscription);
        recovered.ShouldRequeue.Should().BeTrue();
        recovered.OffersRecoveredAudioTranscription.Should().BeFalse();
        _repository.SavedSessions.Should().ContainSingle(s =>
            s.SessionId == session.SessionId && s.Status == SessionStatus.Interrupted);
    }

    [Theory]
    [InlineData(SessionStatus.Recording)]
    [InlineData(SessionStatus.Stopping)]
    public async Task RecoverAsync_recording_or_stopping_with_valid_wavs_preserves_audio_and_offers_recovery(
        SessionStatus status)
    {
        var session = SeedSession(status);
        WriteValidWav(session.MicWavPath!);
        WriteValidWav(session.SystemWavPath!);

        var result = await _recoveryService.RecoverAsync();

        result.Sessions.Should().ContainSingle();
        var recovered = result.Sessions[0];
        recovered.Session.Status.Should().Be(SessionStatus.Interrupted);
        recovered.Session.MicWavPath.Should().Be(session.MicWavPath);
        recovered.Session.SystemWavPath.Should().Be(session.SystemWavPath);
        recovered.Action.Should().Be(RecoveryAction.InterruptedForRecording);
        recovered.ShouldRequeue.Should().BeFalse();
        recovered.OffersRecoveredAudioTranscription.Should().BeTrue();
        File.Exists(session.MicWavPath!).Should().BeTrue();
        File.Exists(session.SystemWavPath!).Should().BeTrue();
    }

    [Fact]
    public async Task RecoverAsync_recording_with_missing_audio_reports_missing_audio_outcome()
    {
        var session = SeedSession(SessionStatus.Recording);

        var result = await _recoveryService.RecoverAsync();

        result.Sessions.Should().ContainSingle();
        var recovered = result.Sessions[0];
        recovered.Session.Status.Should().Be(SessionStatus.Failed);
        recovered.Action.Should().Be(RecoveryAction.MissingAudio);
        recovered.ShouldRequeue.Should().BeFalse();
        recovered.OffersRecoveredAudioTranscription.Should().BeFalse();
        recovered.Session.ErrorCategory.Should().Be(ErrorCategory.AudioFileInvalid);
        recovered.Session.Error.Should().NotBeNullOrWhiteSpace();
    }

    private MeetingSession SeedSession(SessionStatus status)
    {
        var sessionId = Guid.NewGuid();
        var session = new MeetingSession
        {
            SessionId = sessionId,
            Metadata = new MeetingMetadata("Recovery test"),
            Status = status,
            MeetingsRoot = _meetingsRoot,
            MicWavPath = SessionPaths.MicWav(_meetingsRoot, sessionId),
            SystemWavPath = SessionPaths.SystemWav(_meetingsRoot, sessionId),
            Error = status == SessionStatus.Failed ? "Previous failure" : null,
            ErrorCategory = status == SessionStatus.Failed ? ErrorCategory.TranscriptionFailure : null,
        };

        Directory.CreateDirectory(SessionPaths.SessionFolder(_meetingsRoot, sessionId));
        _repository.Sessions.Add(session);
        return session;
    }

    private static AppSettings CreateSettings(string meetingsRoot)
    {
        var settings = SettingsDefaults.Create(AppContext.BaseDirectory);
        settings.MeetingsFolder = meetingsRoot;
        return settings;
    }

    private static void WriteValidWav(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pcm = new byte[3200];
        for (var i = 0; i < pcm.Length; i += 2)
        {
            pcm[i] = 0x10;
            pcm[i + 1] = 0x00;
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(16_000);
        writer.Write(16_000 * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public List<MeetingSession> Sessions { get; } = [];

        public List<MeetingSession> SavedSessions { get; } = [];

        public Task SaveAsync(MeetingSession session, CancellationToken ct = default)
        {
            SavedSessions.Add(Clone(session));
            return Task.CompletedTask;
        }

        public Task<MeetingSession?> LoadAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult<MeetingSession?>(Sessions.FirstOrDefault(s => s.SessionId == sessionId));

        public Task<IReadOnlyList<MeetingSession>> LoadProcessingAsync(string meetingsRoot, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MeetingSession>>(Sessions.Where(s => s.MeetingsRoot == meetingsRoot).Select(Clone).ToList());

        public Task SaveCheckpointAsync(string meetingsRoot, Guid sessionId, TrackTranscriptCheckpoint checkpoint, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<TrackTranscriptCheckpoint?> LoadCheckpointAsync(string meetingsRoot, Guid sessionId, string track, CancellationToken ct = default) =>
            Task.FromResult<TrackTranscriptCheckpoint?>(null);

        public Task DeleteProcessingFolderAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default) =>
            Task.CompletedTask;

        private static MeetingSession Clone(MeetingSession session) =>
            new()
            {
                SessionId = session.SessionId,
                Metadata = session.Metadata,
                StartedAt = session.StartedAt,
                StoppedAt = session.StoppedAt,
                Status = session.Status,
                MicrophoneDeviceId = session.MicrophoneDeviceId,
                MicrophoneDeviceName = session.MicrophoneDeviceName,
                SystemOutputDeviceId = session.SystemOutputDeviceId,
                SystemOutputDeviceName = session.SystemOutputDeviceName,
                MicWavPath = session.MicWavPath,
                SystemWavPath = session.SystemWavPath,
                MicCaptureStartOffsetMs = session.MicCaptureStartOffsetMs,
                SystemCaptureStartOffsetMs = session.SystemCaptureStartOffsetMs,
                ModelPath = session.ModelPath,
                ModelFileName = session.ModelFileName,
                Language = session.Language,
                Error = session.Error,
                ErrorCategory = session.ErrorCategory,
                RetryCount = session.RetryCount,
                MeetingsRoot = session.MeetingsRoot,
            };
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public AppSettings Current { get; set; } = SettingsDefaults.Create(AppContext.BaseDirectory);

        public event EventHandler<AppSettings>? SettingsReloaded;

        public Task<AppSettings> LoadAsync(CancellationToken ct = default) => Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken ct = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }
}
