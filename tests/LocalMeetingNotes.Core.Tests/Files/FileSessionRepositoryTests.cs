using FluentAssertions;
using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Tests.Files;

public class FileSessionRepositoryTests : IDisposable
{
    private readonly string _meetingsRoot;
    private readonly ISessionRepository _repository = new FileSessionRepository();

    public FileSessionRepositoryTests()
    {
        _meetingsRoot = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_meetingsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_meetingsRoot))
        {
            Directory.Delete(_meetingsRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_then_LoadAsync_roundtrips_session()
    {
        var sessionId = Guid.NewGuid();
        var original = CreateSession(
            sessionId,
            SessionStatus.Queued,
            startedAt: new DateTimeOffset(2026, 8, 7, 11, 0, 0, TimeSpan.FromHours(-3)),
            stoppedAt: new DateTimeOffset(2026, 8, 7, 11, 45, 0, TimeSpan.FromHours(-3)));

        await _repository.SaveAsync(original);

        var loaded = await _repository.LoadAsync(_meetingsRoot, sessionId);

        loaded.Should().NotBeNull();
        loaded!.SessionId.Should().Be(original.SessionId);
        loaded.Metadata.Title.Should().Be(original.Metadata.Title);
        loaded.Metadata.Participants.Should().BeEquivalentTo(original.Metadata.Participants);
        loaded.Metadata.Context.Should().Be(original.Metadata.Context);
        loaded.Metadata.References.Should().BeEquivalentTo(original.Metadata.References);
        loaded.StartedAt.Should().Be(original.StartedAt);
        loaded.StoppedAt.Should().Be(original.StoppedAt);
        loaded.Status.Should().Be(original.Status);
        loaded.MicrophoneDeviceId.Should().Be(original.MicrophoneDeviceId);
        loaded.MicrophoneDeviceName.Should().Be(original.MicrophoneDeviceName);
        loaded.SystemOutputDeviceId.Should().Be(original.SystemOutputDeviceId);
        loaded.SystemOutputDeviceName.Should().Be(original.SystemOutputDeviceName);
        loaded.MicWavPath.Should().Be(SessionPaths.MicWav(_meetingsRoot, sessionId));
        loaded.SystemWavPath.Should().Be(SessionPaths.SystemWav(_meetingsRoot, sessionId));
        loaded.MicCaptureStartOffsetMs.Should().Be(original.MicCaptureStartOffsetMs);
        loaded.SystemCaptureStartOffsetMs.Should().Be(original.SystemCaptureStartOffsetMs);
        loaded.ModelPath.Should().Be(original.ModelPath);
        loaded.ModelFileName.Should().Be(original.ModelFileName);
        loaded.Language.Should().Be(original.Language);
        loaded.Error.Should().Be(original.Error);
        loaded.ErrorCategory.Should().Be(original.ErrorCategory);
        loaded.RetryCount.Should().Be(original.RetryCount);
        loaded.MeetingsRoot.Should().Be(_meetingsRoot);

        var sessionJsonPath = SessionPaths.SessionJson(_meetingsRoot, sessionId);
        File.Exists(sessionJsonPath).Should().BeTrue();
        var json = await File.ReadAllTextAsync(sessionJsonPath);
        json.Should().Contain("\"sessionId\"");
        json.Should().Contain("\"title\"");
    }

    [Fact]
    public async Task LoadProcessingAsync_returns_all_sessions_with_session_json()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await _repository.SaveAsync(CreateSession(firstId, SessionStatus.Recording));
        await _repository.SaveAsync(CreateSession(secondId, SessionStatus.WaitingForModel, title: "Second meeting"));

        Directory.CreateDirectory(Path.Combine(SessionPaths.ProcessingRoot(_meetingsRoot), Guid.NewGuid().ToString("D")));

        var sessions = await _repository.LoadProcessingAsync(_meetingsRoot);

        sessions.Should().HaveCount(2);
        sessions.Select(s => s.SessionId).Should().BeEquivalentTo([firstId, secondId]);
    }

    [Fact]
    public async Task SaveCheckpointAsync_then_LoadCheckpointAsync_roundtrips()
    {
        var sessionId = Guid.NewGuid();
        await _repository.SaveAsync(CreateSession(sessionId, SessionStatus.TranscribingMic));

        var checkpoint = new TrackTranscriptCheckpoint
        {
            Track = "Mic",
            AudioDurationMs = 123_456,
            ModelFileName = "ggml-base.bin",
            Language = "auto",
            Segments =
            [
                new CheckpointSegment(1_000, 4_300, "Hello there"),
                new CheckpointSegment(5_000, 8_200, "General Kenobi"),
            ],
        };

        await _repository.SaveCheckpointAsync(_meetingsRoot, sessionId, checkpoint);

        var loaded = await _repository.LoadCheckpointAsync(_meetingsRoot, sessionId, "mic");

        loaded.Should().NotBeNull();
        loaded!.Track.Should().Be("Mic");
        loaded.AudioDurationMs.Should().Be(123_456);
        loaded.ModelFileName.Should().Be("ggml-base.bin");
        loaded.Language.Should().Be("auto");
        loaded.Segments.Should().HaveCount(2);
        loaded.Segments[0].StartMs.Should().Be(1_000);
        loaded.Segments[0].EndMs.Should().Be(4_300);
        loaded.Segments[0].Text.Should().Be("Hello there");

        File.Exists(SessionPaths.MicTranscript(_meetingsRoot, sessionId)).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteProcessingFolderAsync_removes_session_folder()
    {
        var sessionId = Guid.NewGuid();
        await _repository.SaveAsync(CreateSession(sessionId, SessionStatus.Completed));
        await _repository.SaveCheckpointAsync(
            _meetingsRoot,
            sessionId,
            new TrackTranscriptCheckpoint
            {
                Track = "System",
                AudioDurationMs = 10,
                ModelFileName = "ggml-base.bin",
                Segments = [new CheckpointSegment(0, 10, "x")],
            });

        var folder = SessionPaths.SessionFolder(_meetingsRoot, sessionId);
        Directory.Exists(folder).Should().BeTrue();

        await _repository.DeleteProcessingFolderAsync(_meetingsRoot, sessionId);

        Directory.Exists(folder).Should().BeFalse();
        (await _repository.LoadAsync(_meetingsRoot, sessionId)).Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_returns_null_when_session_missing()
    {
        var loaded = await _repository.LoadAsync(_meetingsRoot, Guid.NewGuid());
        loaded.Should().BeNull();
    }

    private MeetingSession CreateSession(
        Guid sessionId,
        SessionStatus status,
        string title = "Payment API",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? stoppedAt = null)
    {
        return new MeetingSession
        {
            SessionId = sessionId,
            Metadata = new MeetingMetadata(
                title,
                ["John", "Maria"],
                "Review new payment endpoint.",
                ["https://example.com/payment-api"]),
            StartedAt = startedAt,
            StoppedAt = stoppedAt,
            Status = status,
            MicrophoneDeviceId = "mic-id",
            MicrophoneDeviceName = "Headset Mic",
            SystemOutputDeviceId = "render-id",
            SystemOutputDeviceName = "Speakers",
            MicCaptureStartOffsetMs = 0,
            SystemCaptureStartOffsetMs = 42,
            ModelPath = @"C:\models\ggml-base.bin",
            ModelFileName = "ggml-base.bin",
            Language = "auto",
            Error = status == SessionStatus.Failed ? "Transcription failed" : null,
            ErrorCategory = status == SessionStatus.Failed ? ErrorCategory.TranscriptionFailure : null,
            RetryCount = 1,
            MeetingsRoot = _meetingsRoot,
        };
    }
}
