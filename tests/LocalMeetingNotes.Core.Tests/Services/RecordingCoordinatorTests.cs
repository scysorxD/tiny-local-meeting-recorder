using FluentAssertions;
using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Tests.Services;

public class RecordingCoordinatorTests : IDisposable
{
    private readonly string _meetingsRoot;
    private readonly FakeAudioCaptureService _capture = new();
    private readonly FakeSessionRepository _repository = new();
    private readonly FakeSettingsStore _settingsStore = new();
    private readonly FakeModelCatalog _modelCatalog = new();
    private readonly SessionStateMachine _stateMachine = new();
    private readonly WavFileValidator _wavValidator = new();
    private readonly RecordingCoordinator _coordinator;

    public RecordingCoordinatorTests()
    {
        _meetingsRoot = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_meetingsRoot);

        _settingsStore.Current = CreateSettings(_meetingsRoot);
        _coordinator = new RecordingCoordinator(
            _capture,
            _repository,
            _settingsStore,
            _modelCatalog,
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

    [Fact]
    public async Task StartAsync_creates_session_transitions_to_recording_and_starts_capture()
    {
        var metadata = new MeetingMetadata("Sprint planning", ["Alice", "Bob"]);

        var session = await _coordinator.StartAsync(metadata);

        session.SessionId.Should().NotBe(Guid.Empty);
        session.Metadata.Should().BeEquivalentTo(metadata);
        session.Status.Should().Be(SessionStatus.Recording);
        session.MeetingsRoot.Should().Be(_meetingsRoot);
        session.StartedAt.Should().NotBeNull();
        session.MicWavPath.Should().Be(SessionPaths.MicWav(_meetingsRoot, session.SessionId));
        session.SystemWavPath.Should().Be(SessionPaths.SystemWav(_meetingsRoot, session.SessionId));
        session.Language.Should().Be(SettingsDefaults.DefaultLanguage);

        _capture.StartCalls.Should().HaveCount(1);
        var request = _capture.StartCalls[0];
        request.MicrophoneWavPath.Should().Be(session.MicWavPath);
        request.SystemWavPath.Should().Be(session.SystemWavPath);
        request.MicrophoneDeviceId.Should().BeNull();
        request.RenderDeviceId.Should().BeNull();

        _repository.SavedSessions.Should().ContainSingle(s => s.SessionId == session.SessionId && s.Status == SessionStatus.Recording);
        _coordinator.CurrentSession.Should().BeSameAs(session);
    }

    [Fact]
    public async Task StartAsync_snapshots_meetings_root_from_settings_at_start()
    {
        var originalRoot = _meetingsRoot;
        var session = await _coordinator.StartAsync(new MeetingMetadata("Root snapshot"));

        _settingsStore.Current = CreateSettings(Path.Combine(_meetingsRoot, "changed"));

        session.MeetingsRoot.Should().Be(originalRoot);
        (await _repository.LoadAsync(originalRoot, session.SessionId))!.MeetingsRoot.Should().Be(originalRoot);
    }

    [Fact]
    public async Task StartAsync_uses_device_ids_from_settings_snapshot()
    {
        _settingsStore.Current = new AppSettings
        {
            MeetingsFolder = _meetingsRoot,
            ModelsFolder = Path.Combine(_meetingsRoot, "models"),
            SelectedModel = SettingsDefaults.DefaultSelectedModel,
            Language = SettingsDefaults.DefaultLanguage,
            TranscriptionThreads = SettingsDefaults.DefaultTranscriptionThreads,
            Microphone = new DeviceSelection
            {
                Mode = SettingsDefaults.MicrophoneModeDevice,
                DeviceId = "mic-42",
            },
            SystemOutput = new DeviceSelection
            {
                Mode = SettingsDefaults.SystemOutputModeDevice,
                DeviceId = "render-99",
            },
        };

        var session = await _coordinator.StartAsync(new MeetingMetadata("Devices"));

        session.MicrophoneDeviceId.Should().Be("mic-42");
        session.SystemOutputDeviceId.Should().Be("render-99");

        var request = _capture.StartCalls.Single();
        request.MicrophoneDeviceId.Should().Be("mic-42");
        request.RenderDeviceId.Should().Be("render-99");
    }

    [Fact]
    public async Task StartAsync_when_already_recording_throws()
    {
        await _coordinator.StartAsync(new MeetingMetadata("First"));

        var act = () => _coordinator.StartAsync(new MeetingMetadata("Second"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StopAsync_with_resolved_model_transitions_to_queued_and_persists_offsets()
    {
        var session = await _coordinator.StartAsync(new MeetingMetadata("Stop with model"));
        var modelPath = Path.Combine(_meetingsRoot, "models", "ggml-base.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        await File.WriteAllBytesAsync(modelPath, [1, 2, 3]);
        _modelCatalog.ResolvedPath = modelPath;

        var micPath = SessionPaths.MicWav(_meetingsRoot, session.SessionId);
        var systemPath = SessionPaths.SystemWav(_meetingsRoot, session.SessionId);
        WriteValidWav(micPath);
        WriteValidWav(systemPath);

        _capture.StopResult = new RecordingResult(
            micPath,
            systemPath,
            MicrophoneCaptured: true,
            SystemCaptured: true,
            MicrophoneStartOffset: TimeSpan.FromMilliseconds(12),
            SystemStartOffset: TimeSpan.FromMilliseconds(34),
            Duration: TimeSpan.FromSeconds(5));

        var stopped = await _coordinator.StopAsync();

        stopped.Status.Should().Be(SessionStatus.Queued);
        stopped.StoppedAt.Should().NotBeNull();
        stopped.MicCaptureStartOffsetMs.Should().Be(12);
        stopped.SystemCaptureStartOffsetMs.Should().Be(34);
        stopped.ModelPath.Should().Be(modelPath);
        stopped.ModelFileName.Should().Be("ggml-base.bin");

        _capture.StopCalls.Should().Be(1);
        File.Exists(micPath).Should().BeTrue();
        File.Exists(systemPath).Should().BeTrue();
        _coordinator.CurrentSession.Should().BeNull();
    }

    [Fact]
    public async Task StopAsync_without_resolved_model_transitions_to_waiting_for_model()
    {
        var session = await _coordinator.StartAsync(new MeetingMetadata("No model"));
        _modelCatalog.ResolvedPath = null;

        var micPath = SessionPaths.MicWav(_meetingsRoot, session.SessionId);
        var systemPath = SessionPaths.SystemWav(_meetingsRoot, session.SessionId);
        WriteValidWav(micPath);
        WriteValidWav(systemPath);

        _capture.StopResult = new RecordingResult(
            micPath,
            systemPath,
            true,
            true,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        var stopped = await _coordinator.StopAsync();

        stopped.Status.Should().Be(SessionStatus.WaitingForModel);
        stopped.ModelPath.Should().BeNull();
    }

    [Fact]
    public async Task StopAsync_when_wav_validation_fails_marks_failed_and_preserves_audio()
    {
        var session = await _coordinator.StartAsync(new MeetingMetadata("Bad wav"));
        _modelCatalog.ResolvedPath = Path.Combine(_meetingsRoot, "model.bin");

        var micPath = SessionPaths.MicWav(_meetingsRoot, session.SessionId);
        var systemPath = SessionPaths.SystemWav(_meetingsRoot, session.SessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(micPath)!);
        await File.WriteAllTextAsync(micPath, "not a wav");
        WriteValidWav(systemPath);

        _capture.StopResult = new RecordingResult(
            micPath,
            systemPath,
            true,
            true,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1));

        var stopped = await _coordinator.StopAsync();

        stopped.Status.Should().Be(SessionStatus.Failed);
        stopped.ErrorCategory.Should().Be(ErrorCategory.AudioFileInvalid);
        stopped.Error.Should().NotBeNullOrWhiteSpace();
        File.Exists(micPath).Should().BeTrue();
        File.Exists(systemPath).Should().BeTrue();
        _repository.DeleteCalls.Should().Be(0);
    }

    [Fact]
    public async Task StopAsync_when_not_recording_throws()
    {
        var act = () => _coordinator.StopAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static AppSettings CreateSettings(string meetingsRoot) =>
        new()
        {
            MeetingsFolder = meetingsRoot,
            ModelsFolder = Path.Combine(meetingsRoot, "models"),
            SelectedModel = SettingsDefaults.DefaultSelectedModel,
            Language = SettingsDefaults.DefaultLanguage,
            TranscriptionThreads = SettingsDefaults.DefaultTranscriptionThreads,
            Microphone = new DeviceSelection { Mode = SettingsDefaults.MicrophoneModeDefaultCommunications },
            SystemOutput = new DeviceSelection { Mode = SettingsDefaults.SystemOutputModeDefault },
        };

    private static void WriteValidWav(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pcm = new byte[320];
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

    private sealed class FakeAudioCaptureService : IAudioCaptureService
    {
        public bool IsRecording { get; private set; }

        public event EventHandler<AudioMeterEventArgs>? MetersUpdated;

        public List<RecordingRequest> StartCalls { get; } = [];

        public int StopCalls { get; private set; }

        public RecordingResult StopResult { get; set; } = new(
            string.Empty,
            string.Empty,
            false,
            false,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero);

        public (float MicrophonePeak, float SystemPeak) GetLivePeaks() => (0f, 0f);

        public Task StartAsync(RecordingRequest request, CancellationToken cancellationToken = default)
        {
            StartCalls.Add(request);
            IsRecording = true;
            return Task.CompletedTask;
        }

        public Task<RecordingResult> StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalls++;
            IsRecording = false;
            return Task.FromResult(StopResult);
        }
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public List<MeetingSession> SavedSessions { get; } = [];

        public int DeleteCalls { get; private set; }

        public Task SaveAsync(MeetingSession session, CancellationToken ct = default)
        {
            SavedSessions.Add(Clone(session));
            return Task.CompletedTask;
        }

        public Task<MeetingSession?> LoadAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default)
        {
            var saved = SavedSessions.LastOrDefault(s => s.SessionId == sessionId && s.MeetingsRoot == meetingsRoot);
            return Task.FromResult(saved is null ? null : Clone(saved));
        }

        public Task<IReadOnlyList<MeetingSession>> LoadProcessingAsync(string meetingsRoot, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MeetingSession>>(SavedSessions.Where(s => s.MeetingsRoot == meetingsRoot).Select(Clone).ToList());

        public Task SaveCheckpointAsync(string meetingsRoot, Guid sessionId, TrackTranscriptCheckpoint checkpoint, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<TrackTranscriptCheckpoint?> LoadCheckpointAsync(string meetingsRoot, Guid sessionId, string track, CancellationToken ct = default) =>
            Task.FromResult<TrackTranscriptCheckpoint?>(null);

        public Task DeleteProcessingFolderAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

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

    private sealed class FakeModelCatalog : IModelCatalog
    {
        public string? ResolvedPath { get; set; }

        public IReadOnlyList<WhisperModelInfo> Discover(string modelsFolder) => [];

        public ModelValidationResult ValidateFile(string path) =>
            File.Exists(path) && new FileInfo(path).Length > 0
                ? new ModelValidationResult(true)
                : new ModelValidationResult(false, ErrorCategory.ModelMissing, "missing");

        public string? ResolveSelectedModelPath(AppSettings settings) => ResolvedPath;
    }
}
