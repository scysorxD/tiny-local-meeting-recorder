using System.Globalization;
using System.Text;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Services;

public static class MarkdownNoteBuilder
{
    public static string Build(MeetingSession session, IReadOnlyList<MergedTranscriptSegment> segments)
    {
        var builder = new StringBuilder();
        var title = session.Metadata.Title;

        builder.AppendLine($"# {title}");
        builder.AppendLine();

        if (session.StartedAt is { } startedAt)
        {
            builder.AppendLine($"**Date:** {startedAt:yyyy-MM-dd}");
            builder.AppendLine($"**Started:** {startedAt:HH:mm:ss}");

            if (session.StoppedAt is { } stoppedAt)
            {
                builder.AppendLine($"**Ended:** {stoppedAt:HH:mm:ss}");
                builder.AppendLine($"**Duration:** {FormatDuration(stoppedAt - startedAt)}");
            }
        }

        if (session.Metadata.Participants is { Count: > 0 } participants)
        {
            builder.AppendLine($"**Participants:** {string.Join(", ", participants)}");
        }

        if (session.Metadata.References is { Count: > 0 } references)
        {
            builder.AppendLine($"**Reference:** {references[0]}");
        }

        if (!string.IsNullOrWhiteSpace(session.ModelFileName))
        {
            builder.AppendLine($"**Whisper model:** {session.ModelFileName}");
        }

        if (!string.IsNullOrWhiteSpace(session.Language))
        {
            builder.AppendLine($"**Language:** {session.Language}");
        }

        if (!string.IsNullOrWhiteSpace(session.Metadata.Context))
        {
            builder.AppendLine();
            builder.AppendLine("## Context");
            builder.AppendLine();
            builder.AppendLine(session.Metadata.Context.Trim());
        }

        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("## Transcript");
        builder.AppendLine();

        foreach (var segment in segments)
        {
            var speaker = segment.Speaker == Speaker.You ? "You" : "Remote";
            builder.AppendLine($"[{FormatTimestamp(segment.Start)}] **{speaker}:** {segment.Text}");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    private static string FormatTimestamp(TimeSpan timestamp)
    {
        if (timestamp < TimeSpan.Zero)
        {
            timestamp = TimeSpan.Zero;
        }

        return timestamp.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }
}
