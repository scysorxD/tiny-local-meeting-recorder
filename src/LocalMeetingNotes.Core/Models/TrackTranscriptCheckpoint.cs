namespace LocalMeetingNotes.Core.Models;

public sealed class TrackTranscriptCheckpoint
{
    public required string Track { get; set; }

    public long AudioDurationMs { get; set; }

    public required string ModelFileName { get; set; }

    public string Language { get; set; } = "auto";

    public List<CheckpointSegment> Segments { get; set; } = [];
}

public sealed record CheckpointSegment(long StartMs, long EndMs, string Text);
