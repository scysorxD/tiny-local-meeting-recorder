using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Files;

public sealed record MeetingHistoryItem(
    string Title,
    string Status,
    DateTimeOffset SortKey,
    string Path,
    Guid? SessionId,
    bool IsCompletedNote);

public sealed class MeetingHistoryScanner
{
    public async Task<IReadOnlyList<MeetingHistoryItem>> ScanAsync(
        string meetingsRoot,
        Func<string, CancellationToken, Task<IReadOnlyList<MeetingSession>>> loadProcessing,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(meetingsRoot);
        ArgumentNullException.ThrowIfNull(loadProcessing);

        var items = new List<MeetingHistoryItem>();
        var processing = await loadProcessing(meetingsRoot, ct);
        foreach (var session in processing)
        {
            items.Add(new MeetingHistoryItem(
                session.Metadata.Title,
                session.Status.ToString(),
                session.StartedAt ?? DateTimeOffset.MinValue,
                meetingsRoot,
                session.SessionId,
                IsCompletedNote: false));
        }

        if (Directory.Exists(meetingsRoot))
        {
            foreach (var path in Directory.EnumerateFiles(meetingsRoot, "*.md", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                var writeTime = File.GetLastWriteTimeUtc(path);
                items.Add(new MeetingHistoryItem(
                    Path.GetFileNameWithoutExtension(path),
                    "Completed",
                    new DateTimeOffset(writeTime, TimeSpan.Zero),
                    path,
                    SessionId: null,
                    IsCompletedNote: true));
            }
        }

        return items
            .OrderByDescending(item => item.SortKey)
            .ToList();
    }
}
