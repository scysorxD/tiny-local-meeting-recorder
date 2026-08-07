using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Files;

public sealed class FileSessionRepository : ISessionRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task SaveAsync(MeetingSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        session.MicWavPath = SessionPaths.MicWav(session.MeetingsRoot, session.SessionId);
        session.SystemWavPath = SessionPaths.SystemWav(session.MeetingsRoot, session.SessionId);

        var path = SessionPaths.SessionJson(session.MeetingsRoot, session.SessionId);
        var dto = SessionJsonDto.FromSession(session);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await AtomicFile.WriteUtf8Async(path, json, ct, overwrite: true);
    }

    public async Task<MeetingSession?> LoadAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default)
    {
        var path = SessionPaths.SessionJson(meetingsRoot, sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, ct);
        var dto = JsonSerializer.Deserialize<SessionJsonDto>(json, JsonOptions);
        return dto?.ToSession(meetingsRoot);
    }

    public async Task<IReadOnlyList<MeetingSession>> LoadProcessingAsync(string meetingsRoot, CancellationToken ct = default)
    {
        var processingRoot = SessionPaths.ProcessingRoot(meetingsRoot);
        if (!Directory.Exists(processingRoot))
        {
            return [];
        }

        var sessions = new List<MeetingSession>();
        foreach (var directory in Directory.EnumerateDirectories(processingRoot))
        {
            ct.ThrowIfCancellationRequested();

            if (!Guid.TryParse(Path.GetFileName(directory), out var sessionId))
            {
                continue;
            }

            var sessionJson = Path.Combine(directory, "session.json");
            if (!File.Exists(sessionJson))
            {
                continue;
            }

            var loaded = await LoadAsync(meetingsRoot, sessionId, ct);
            if (loaded is not null)
            {
                sessions.Add(loaded);
            }
        }

        return sessions;
    }

    public async Task SaveCheckpointAsync(
        string meetingsRoot,
        Guid sessionId,
        TrackTranscriptCheckpoint checkpoint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var trackKey = checkpoint.Track.Equals("Mic", StringComparison.OrdinalIgnoreCase) ? "mic"
            : checkpoint.Track.Equals("System", StringComparison.OrdinalIgnoreCase) ? "system"
            : throw new ArgumentException($"Unknown checkpoint track '{checkpoint.Track}'.", nameof(checkpoint));

        var path = SessionPaths.TranscriptCheckpoint(meetingsRoot, sessionId, trackKey);
        var json = JsonSerializer.Serialize(checkpoint, JsonOptions);
        await AtomicFile.WriteUtf8Async(path, json, ct, overwrite: true);
    }

    public async Task<TrackTranscriptCheckpoint?> LoadCheckpointAsync(
        string meetingsRoot,
        Guid sessionId,
        string track,
        CancellationToken ct = default)
    {
        var path = SessionPaths.TranscriptCheckpoint(meetingsRoot, sessionId, track);
        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<TrackTranscriptCheckpoint>(json, JsonOptions);
    }

    public Task DeleteProcessingFolderAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var folder = SessionPaths.SessionFolder(meetingsRoot, sessionId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        return Task.CompletedTask;
    }

    private sealed class SessionJsonDto
    {
        public Guid SessionId { get; set; }

        public string Title { get; set; } = string.Empty;

        public List<string>? Participants { get; set; }

        public string? Context { get; set; }

        public List<string>? References { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public DateTimeOffset? StoppedAt { get; set; }

        public SessionStatus Status { get; set; }

        public string? MicDeviceId { get; set; }

        public string? MicDeviceName { get; set; }

        public string? SystemDeviceId { get; set; }

        public string? SystemDeviceName { get; set; }

        public string? MicWavPath { get; set; }

        public string? SystemWavPath { get; set; }

        public long MicCaptureStartedOffsetMs { get; set; }

        public long SystemCaptureStartedOffsetMs { get; set; }

        public string? ModelPath { get; set; }

        public string? ModelFileName { get; set; }

        public string Language { get; set; } = "auto";

        public string? Error { get; set; }

        public ErrorCategory? ErrorCategory { get; set; }

        public int RetryCount { get; set; }

        public string? MeetingsRoot { get; set; }

        public static SessionJsonDto FromSession(MeetingSession session) =>
            new()
            {
                SessionId = session.SessionId,
                Title = session.Metadata.Title,
                Participants = session.Metadata.Participants?.ToList(),
                Context = session.Metadata.Context,
                References = session.Metadata.References?.ToList(),
                StartedAt = session.StartedAt,
                StoppedAt = session.StoppedAt,
                Status = session.Status,
                MicDeviceId = session.MicrophoneDeviceId,
                MicDeviceName = session.MicrophoneDeviceName,
                SystemDeviceId = session.SystemOutputDeviceId,
                SystemDeviceName = session.SystemOutputDeviceName,
                MicWavPath = session.MicWavPath,
                SystemWavPath = session.SystemWavPath,
                MicCaptureStartedOffsetMs = session.MicCaptureStartOffsetMs,
                SystemCaptureStartedOffsetMs = session.SystemCaptureStartOffsetMs,
                ModelPath = session.ModelPath,
                ModelFileName = session.ModelFileName,
                Language = session.Language,
                Error = session.Error,
                ErrorCategory = session.ErrorCategory,
                RetryCount = session.RetryCount,
                MeetingsRoot = session.MeetingsRoot,
            };

        public MeetingSession ToSession(string meetingsRoot)
        {
            var root = MeetingsRoot ?? meetingsRoot;

            return new MeetingSession
            {
                SessionId = SessionId,
                Metadata = new MeetingMetadata(Title, Participants, Context, References),
                StartedAt = StartedAt,
                StoppedAt = StoppedAt,
                Status = Status,
                MicrophoneDeviceId = MicDeviceId,
                MicrophoneDeviceName = MicDeviceName,
                SystemOutputDeviceId = SystemDeviceId,
                SystemOutputDeviceName = SystemDeviceName,
                MicWavPath = MicWavPath ?? SessionPaths.MicWav(root, SessionId),
                SystemWavPath = SystemWavPath ?? SessionPaths.SystemWav(root, SessionId),
                MicCaptureStartOffsetMs = MicCaptureStartedOffsetMs,
                SystemCaptureStartOffsetMs = SystemCaptureStartedOffsetMs,
                ModelPath = ModelPath,
                ModelFileName = ModelFileName,
                Language = Language,
                Error = Error,
                ErrorCategory = ErrorCategory,
                RetryCount = RetryCount,
                MeetingsRoot = root,
            };
        }
    }
}
