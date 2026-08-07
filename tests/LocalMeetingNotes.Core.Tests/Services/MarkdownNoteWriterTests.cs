using System.Text;
using FluentAssertions;
using LocalMeetingNotes.Core.Interfaces;
using LocalMeetingNotes.Core.Models;
using LocalMeetingNotes.Core.Services;

namespace LocalMeetingNotes.Core.Tests.Services;

public class MarkdownNoteWriterTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly IMeetingNoteWriter _writer = new MeetingNoteWriter();

    public MarkdownNoteWriterTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "LocalMeetingNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_full_metadata_produces_expected_markdown()
    {
        var session = CreateSession(
            title: "Payment API",
            participants: ["John", "Maria"],
            context: "Review the new payment endpoint and clarify error-handling behavior.",
            references: ["https://example.com/payment-api"],
            startedAt: new DateTimeOffset(2026, 8, 7, 11, 0, 14, TimeSpan.Zero),
            stoppedAt: new DateTimeOffset(2026, 8, 7, 11, 47, 52, TimeSpan.Zero),
            modelFileName: "ggml-base.bin",
            language: "auto");

        var segments = new List<MergedTranscriptSegment>
        {
            new(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), Speaker.You, "So my question about the endpoint is..."),
            new(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(20), Speaker.Remote, "Currently we're returning..."),
            new(TimeSpan.FromSeconds(21), TimeSpan.FromSeconds(30), Speaker.You, "Perfect. And what happens when...")
        };

        var path = await _writer.WriteAsync(session, segments, CancellationToken.None);
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);

        path.Should().Be(Path.Combine(_tempRoot, "2026-08-07_1100 - Payment API.md"));
        content.Should().Contain("# Payment API");
        content.Should().Contain("**Date:** 2026-08-07");
        content.Should().Contain("**Started:** 11:00:14");
        content.Should().Contain("**Ended:** 11:47:52");
        content.Should().Contain("**Duration:** 00:47:38");
        content.Should().Contain("**Participants:** John, Maria");
        content.Should().Contain("**Reference:** https://example.com/payment-api");
        content.Should().Contain("**Whisper model:** ggml-base.bin");
        content.Should().Contain("**Language:** auto");
        content.Should().Contain("## Context");
        content.Should().Contain("Review the new payment endpoint");
        content.Should().Contain("[00:00:04] **You:** So my question about the endpoint is...");
        content.Should().Contain("[00:00:09] **Remote:** Currently we're returning...");
        content.Should().Contain("[00:00:21] **You:** Perfect. And what happens when...");
    }

    [Fact]
    public async Task WriteAsync_minimal_metadata_omits_optional_lines()
    {
        var session = CreateSession(
            title: "Standup",
            startedAt: new DateTimeOffset(2026, 8, 7, 9, 15, 0, TimeSpan.Zero),
            stoppedAt: new DateTimeOffset(2026, 8, 7, 9, 20, 0, TimeSpan.Zero));

        var path = await _writer.WriteAsync(session, [], CancellationToken.None);
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);

        content.Should().Contain("# Standup");
        content.Should().Contain("**Date:** 2026-08-07");
        content.Should().Contain("**Started:** 09:15:00");
        content.Should().Contain("**Ended:** 09:20:00");
        content.Should().Contain("**Duration:** 00:05:00");
        content.Should().NotContain("**Participants:**");
        content.Should().NotContain("**Reference:**");
        content.Should().NotContain("**Whisper model:**");
        content.Should().NotContain("## Context");
        content.Should().Contain("## Transcript");
    }

    [Fact]
    public async Task WriteAsync_preserves_utf8_content()
    {
        var session = CreateSession(
            title: "Réunion",
            startedAt: new DateTimeOffset(2026, 8, 7, 14, 0, 0, TimeSpan.Zero),
            stoppedAt: new DateTimeOffset(2026, 8, 7, 14, 5, 0, TimeSpan.Zero));

        var segments = new List<MergedTranscriptSegment>
        {
            new(TimeSpan.Zero, TimeSpan.FromSeconds(5), Speaker.You, "Bonjour — ça va très bien 🎤")
        };

        var path = await _writer.WriteAsync(session, segments, CancellationToken.None);
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8);

        content.Should().Contain("Réunion");
        content.Should().Contain("Bonjour — ça va très bien 🎤");
    }

    [Fact]
    public async Task WriteAsync_writes_atomically_without_temp_file_left_behind()
    {
        var session = CreateSession(
            title: "Atomic Test",
            startedAt: new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero),
            stoppedAt: new DateTimeOffset(2026, 8, 7, 10, 1, 0, TimeSpan.Zero));

        var path = await _writer.WriteAsync(session, [], CancellationToken.None);

        File.Exists(path).Should().BeTrue();
        Directory.GetFiles(_tempRoot, "*.tmp").Should().BeEmpty();
        (await File.ReadAllTextAsync(path, Encoding.UTF8)).Should().Contain("# Atomic Test");
    }

    [Fact]
    public async Task WriteAsync_throws_when_both_base_and_disambiguated_files_exist()
    {
        var sessionId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var session = CreateSession(
            title: "Duplicate",
            startedAt: new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            stoppedAt: new DateTimeOffset(2026, 8, 7, 12, 1, 0, TimeSpan.Zero),
            sessionId: sessionId);

        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "2026-08-07_1200 - Duplicate.md"), "existing content");
        await File.WriteAllTextAsync(Path.Combine(_tempRoot, "2026-08-07_1200 - Duplicate (a1b2c3d4).md"), "existing disambiguated");

        var act = () => _writer.WriteAsync(session, [], CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task WriteAsync_uses_disambiguator_when_target_exists()
    {
        var session = CreateSession(
            title: "Duplicate",
            startedAt: new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero),
            stoppedAt: new DateTimeOffset(2026, 8, 7, 12, 1, 0, TimeSpan.Zero),
            sessionId: Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890"));

        var existingPath = Path.Combine(_tempRoot, "2026-08-07_1200 - Duplicate.md");
        await File.WriteAllTextAsync(existingPath, "existing content");

        var path = await _writer.WriteAsync(session, [], CancellationToken.None);

        path.Should().Be(Path.Combine(_tempRoot, "2026-08-07_1200 - Duplicate (a1b2c3d4).md"));
        File.Exists(existingPath).Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Contain("# Duplicate");
    }

    private MeetingSession CreateSession(
        string title,
        DateTimeOffset startedAt,
        DateTimeOffset? stoppedAt = null,
        IReadOnlyList<string>? participants = null,
        string? context = null,
        IReadOnlyList<string>? references = null,
        string? modelFileName = null,
        string language = "auto",
        Guid? sessionId = null)
    {
        return new MeetingSession
        {
            SessionId = sessionId ?? Guid.NewGuid(),
            Metadata = new MeetingMetadata(title, participants, context, references),
            StartedAt = startedAt,
            StoppedAt = stoppedAt,
            MeetingsRoot = _tempRoot,
            ModelFileName = modelFileName,
            Language = language
        };
    }
}
