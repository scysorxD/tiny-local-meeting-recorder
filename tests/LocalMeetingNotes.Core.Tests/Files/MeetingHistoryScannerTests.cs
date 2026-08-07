using FluentAssertions;
using LocalMeetingNotes.Core.Files;
using LocalMeetingNotes.Core.Models;

namespace LocalMeetingNotes.Core.Tests.Files;

public class MeetingHistoryScannerTests
{
    [Fact]
    public async Task ScanAsync_orders_completed_and_processing_descending()
    {
        var root = Path.Combine(Path.GetTempPath(), "lmn-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var older = Path.Combine(root, "older.md");
            var newer = Path.Combine(root, "newer.md");
            await File.WriteAllTextAsync(older, "# older");
            await File.WriteAllTextAsync(newer, "# newer");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(-1));

            var session = new MeetingSession
            {
                SessionId = Guid.NewGuid(),
                Metadata = new MeetingMetadata("Live", [], null, []),
                Status = SessionStatus.Queued,
                MeetingsRoot = root,
                StartedAt = DateTimeOffset.UtcNow,
            };

            var scanner = new MeetingHistoryScanner();
            var items = await scanner.ScanAsync(
                root,
                (_, _) => Task.FromResult<IReadOnlyList<MeetingSession>>([session]));

            items.Should().HaveCount(3);
            items[0].Title.Should().Be("Live");
            items[1].Title.Should().Be("newer");
            items[2].Title.Should().Be("older");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
