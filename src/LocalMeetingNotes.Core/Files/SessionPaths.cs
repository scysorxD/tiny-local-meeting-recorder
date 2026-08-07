namespace LocalMeetingNotes.Core.Files;

public static class SessionPaths
{
    public static string ProcessingRoot(string meetingsRoot) =>
        Path.Combine(meetingsRoot, ".processing");

    public static string SessionFolder(string meetingsRoot, Guid sessionId) =>
        Path.Combine(ProcessingRoot(meetingsRoot), sessionId.ToString("D"));

    public static string SessionJson(string meetingsRoot, Guid sessionId) =>
        Path.Combine(SessionFolder(meetingsRoot, sessionId), "session.json");

    public static string MicWav(string meetingsRoot, Guid sessionId) =>
        Path.Combine(SessionFolder(meetingsRoot, sessionId), "mic.wav");

    public static string SystemWav(string meetingsRoot, Guid sessionId) =>
        Path.Combine(SessionFolder(meetingsRoot, sessionId), "system.wav");

    public static string MicTranscript(string meetingsRoot, Guid sessionId) =>
        Path.Combine(SessionFolder(meetingsRoot, sessionId), "mic.transcript.json");

    public static string SystemTranscript(string meetingsRoot, Guid sessionId) =>
        Path.Combine(SessionFolder(meetingsRoot, sessionId), "system.transcript.json");

    public static string TranscriptCheckpoint(string meetingsRoot, Guid sessionId, string track) =>
        track.ToLowerInvariant() switch
        {
            "mic" => MicTranscript(meetingsRoot, sessionId),
            "system" => SystemTranscript(meetingsRoot, sessionId),
            _ => throw new ArgumentException($"Unknown track '{track}'. Expected 'mic' or 'system'.", nameof(track)),
        };
}
