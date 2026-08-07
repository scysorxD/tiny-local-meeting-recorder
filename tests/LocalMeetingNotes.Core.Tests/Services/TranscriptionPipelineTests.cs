using FluentAssertions;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;
using LocalMeetingNotes.Core.Settings;

namespace LocalMeetingNotes.Core.Tests.Services;

public sealed class TranscriptionPipelineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
    private readonly FakeSessionRepository _repository = new();
    private readonly FakeTranscriptionEngine _engine = new();
    private readonly FakeMeetingNoteWriter _writer = new();
    private readonly FakeSettingsStore _settings;
    private readonly FakeModelCatalog _models = new();
    private readonly TranscriptionPipeline _pipeline;

    public TranscriptionPipelineTests()
    {
        Directory.CreateDirectory(_root);
        _settings = new FakeSettingsStore(CreateSettings(_root));
        _pipeline = new TranscriptionPipeline(
            _repository,
            _engine,
            _writer,
            _settings,
            _models,
            new SessionStateMachine());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessAsync_reuses_matching_checkpoints_then_writes_note_and_deletes_audio()
    {
        var session = CreateQueuedSession();
        _repository.Sessions[session.SessionId] = session;
        _repository.Checkpoints[(session.SessionId, "mic")] = Checkpoint("Mic", "mic checkpoint");
        _repository.Checkpoints[(session.SessionId, "system")] = Checkpoint("System", "system checkpoint");

        await _pipeline.ProcessAsync(session.SessionId);

        _engine.Calls.Should().BeEmpty();
        _writer.WrittenSegments.Select(segment => segment.Text).Should().Contain(["mic checkpoint", "system checkpoint"]);
        _repository.DeletedSessions.Should().Contain(session.SessionId);
        _repository.Sessions[session.SessionId].Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_transcribes_mic_then_system_and_saves_checkpoints()
    {
        var session = CreateQueuedSession();
        WriteWav(session.MicWavPath!, 1_000);
        WriteWav(session.SystemWavPath!, 1_000);
        _repository.Sessions[session.SessionId] = session;
        _engine.Results.Enqueue(new TrackTranscript([new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "mic")]));
        _engine.Results.Enqueue(new TrackTranscript([new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "system")]));

        await _pipeline.ProcessAsync(session.SessionId);

        _engine.Calls.Select(call => call.WavPath).Should().Equal(session.MicWavPath, session.SystemWavPath);
        _repository.Checkpoints.Keys.Should().Contain((session.SessionId, "mic")).And.Contain((session.SessionId, "system"));
        _repository.Sessions[session.SessionId].Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_skips_silent_tracks_without_invoking_whisper()
    {
        var session = CreateQueuedSession();
        WriteWav(session.MicWavPath!, 0);
        WriteWav(session.SystemWavPath!, 0);
        _repository.Sessions[session.SessionId] = session;

        await _pipeline.ProcessAsync(session.SessionId);

        _engine.Calls.Should().BeEmpty();
        _writer.WrittenSegments.Should().BeEmpty();
        _repository.Sessions[session.SessionId].Status.Should().Be(SessionStatus.Completed);
    }

    [Fact]
    public async Task ProcessAsync_when_transcription_fails_marks_session_failed_and_preserves_audio()
    {
        var session = CreateQueuedSession();
        WriteWav(session.MicWavPath!, 1_000);
        WriteWav(session.SystemWavPath!, 1_000);
        _repository.Sessions[session.SessionId] = session;
        _engine.Exception = new InvalidOperationException("Whisper failed.");

        await _pipeline.ProcessAsync(session.SessionId);

        _repository.Sessions[session.SessionId].Status.Should().Be(SessionStatus.Failed);
        _repository.Sessions[session.SessionId].ErrorCategory.Should().Be(ErrorCategory.TranscriptionFailure);
        _repository.DeletedSessions.Should().BeEmpty();
        File.Exists(session.MicWavPath!).Should().BeTrue();
    }

    [Fact]
    public async Task PrepareRetryAsync_resolves_model_and_requeues_waiting_session()
    {
        var session = CreateQueuedSession();
        session.Status = SessionStatus.WaitingForModel;
        session.ModelPath = null;
        session.ModelFileName = null;
        _repository.Sessions[session.SessionId] = session;
        _models.ResolvedPath = Path.Combine(_root, "models", "ggml-small.bin");

        var available = await _pipeline.PrepareRetryAsync(session.SessionId);

        available.Should().BeTrue();
        _repository.Sessions[session.SessionId].Status.Should().Be(SessionStatus.Queued);
        _repository.Sessions[session.SessionId].ModelPath.Should().Be(_models.ResolvedPath);
        _repository.Sessions[session.SessionId].ModelFileName.Should().Be("ggml-small.bin");
        _repository.Sessions[session.SessionId].RetryCount.Should().Be(1);
    }

    private MeetingSession CreateQueuedSession()
    {
        var id = Guid.NewGuid();
        return new MeetingSession
        {
            SessionId = id,
            Metadata = new MeetingMetadata("Pipeline test"),
            Status = SessionStatus.Queued,
            MeetingsRoot = _root,
            ModelPath = Path.Combine(_root, "models", "ggml-base.bin"),
            ModelFileName = "ggml-base.bin",
            Language = "en",
            MicWavPath = Path.Combine(_root, $"{id}-mic.wav"),
            SystemWavPath = Path.Combine(_root, $"{id}-system.wav"),
        };
    }

    private static TrackTranscriptCheckpoint Checkpoint(string track, string text) =>
        new()
        {
            Track = track,
            ModelFileName = "ggml-base.bin",
            Language = "en",
            Segments = [new CheckpointSegment(0, 1_000, text)],
        };

    private static AppSettings CreateSettings(string root) =>
        new()
        {
            MeetingsFolder = root,
            ModelsFolder = Path.Combine(root, "models"),
            SelectedModel = "ggml-base.bin",
            Language = "en",
            TranscriptionThreads = 2,
            DeleteAudioAfterSuccess = true,
        };

    private static void WriteWav(string path, short sample)
    {
        var pcm = Enumerable.Repeat(sample, 160).SelectMany(BitConverter.GetBytes).ToArray();
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
        writer.Write(32_000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }

    private sealed class FakeSessionRepository : ISessionRepository
    {
        public Dictionary<Guid, MeetingSession> Sessions { get; } = [];
        public Dictionary<(Guid SessionId, string Track), TrackTranscriptCheckpoint> Checkpoints { get; } = [];
        public List<Guid> DeletedSessions { get; } = [];

        public Task SaveAsync(MeetingSession session, CancellationToken ct = default)
        {
            Sessions[session.SessionId] = session;
            return Task.CompletedTask;
        }

        public Task<MeetingSession?> LoadAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult(Sessions.GetValueOrDefault(sessionId));

        public Task<IReadOnlyList<MeetingSession>> LoadProcessingAsync(string meetingsRoot, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MeetingSession>>(Sessions.Values.ToList());

        public Task SaveCheckpointAsync(string meetingsRoot, Guid sessionId, TrackTranscriptCheckpoint checkpoint, CancellationToken ct = default)
        {
            Checkpoints[(sessionId, checkpoint.Track.ToLowerInvariant())] = checkpoint;
            return Task.CompletedTask;
        }

        public Task<TrackTranscriptCheckpoint?> LoadCheckpointAsync(string meetingsRoot, Guid sessionId, string track, CancellationToken ct = default) =>
            Task.FromResult<TrackTranscriptCheckpoint?>(Checkpoints.GetValueOrDefault((sessionId, track.ToLowerInvariant())));

        public Task DeleteProcessingFolderAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default)
        {
            DeletedSessions.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTranscriptionEngine : ITranscriptionEngine
    {
        public Queue<TrackTranscript> Results { get; } = [];
        public List<(string WavPath, TranscriptionOptions Options)> Calls { get; } = [];
        public Exception? Exception { get; set; }

        public Task<TrackTranscript> TranscribeAsync(string wavPath, TranscriptionOptions options, IProgress<TranscriptionProgress>? progress, CancellationToken cancellationToken)
        {
            Calls.Add((wavPath, options));
            if (Exception is not null)
            {
                return Task.FromException<TrackTranscript>(Exception);
            }

            return Task.FromResult(Results.Dequeue());
        }
    }

    private sealed class FakeMeetingNoteWriter : IMeetingNoteWriter
    {
        public IReadOnlyList<MergedTranscriptSegment> WrittenSegments { get; private set; } = [];

        public Task<string> WriteAsync(MeetingSession session, IReadOnlyList<MergedTranscriptSegment> segments, CancellationToken cancellationToken = default)
        {
            WrittenSegments = segments;
            return Task.FromResult(Path.Combine(session.MeetingsRoot, "note.md"));
        }
    }

    private sealed class FakeSettingsStore(AppSettings settings) : ISettingsStore
    {
        public AppSettings Current { get; private set; } = settings;
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
        public ModelValidationResult ValidateFile(string path) => new(true);
        public string? ResolveSelectedModelPath(AppSettings settings) => ResolvedPath;
    }
}
